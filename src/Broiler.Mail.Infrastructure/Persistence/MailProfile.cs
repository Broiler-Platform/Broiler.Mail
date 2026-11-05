using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Owns one process lease for a local profile. Existing configuration files are adopted in place.</summary>
public sealed class MailProfile : IDisposable
{
    private readonly FileStream _lease;
    private MailProfile(string directory, ProfileManifest manifest, FileStream lease)
    { DirectoryPath = directory; Manifest = manifest; _lease = lease; }
    public string DirectoryPath { get; }
    public ProfileManifest Manifest { get; private set; }
    public string AccountsPath => Path.Combine(DirectoryPath, "accounts.json");
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    public string DraftsPath => Path.Combine(DirectoryPath, "drafts.json");
    public string CachePath => Path.Combine(DirectoryPath, "mail-cache.db");

    public static MailProfile Open(string directory)
    {
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
            else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        FileStream lease;
        try { lease = new FileStream(Path.Combine(root, "profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("This Mail profile is already open or its lock is unavailable. Close its other instance before continuing.", error); }
        try
        {
            string manifestPath = Path.Combine(root, "profile.json");
            ProfileManifest manifest;
            if (File.Exists(manifestPath))
            {
                using var stream = File.OpenRead(manifestPath);
                if (stream.Length > 64 * 1024) throw new InvalidDataException("Profile manifest exceeds its supported size.");
                manifest = JsonSerializer.Deserialize(stream, ProfileJsonContext.Default.ProfileManifest)
                    ?? throw new InvalidDataException("Profile manifest is incomplete.");
                if (manifest.SchemaVersion != 1 || manifest.ProfileId == Guid.Empty ||
                    manifest.CacheMaximumBytes < 256 * 1024 || manifest.CacheMaximumBytes > 16L * 1024 * 1024 * 1024 || !Enum.IsDefined(manifest.DownloadPolicy))
                    throw new InvalidDataException("Unsupported profile manifest. Restore a compatible backup; existing files were not reset.");
            }
            else
            {
                if (File.Exists(Path.Combine(root, "mail-cache.db")))
                    throw new InvalidDataException("A cache exists without its profile manifest. Restore the manifest before opening this profile.");
                // Hold legacy write locks too: an old-version settings/draft writer cannot race adoption.
                var locks = new List<FileStream>();
                try
                {
                    string[] files = ["accounts.json", "settings.json", "drafts.json"];
                    foreach (string file in files)
                        locks.Add(new FileStream(Path.Combine(root, file + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                    // Validate everything before creating a manifest or claiming migration succeeded.
                    new JsonAccountStore(Path.Combine(root, "accounts.json")).LoadAsync().GetAwaiter().GetResult();
                    new JsonSettingsStore(Path.Combine(root, "settings.json")).LoadAsync().GetAwaiter().GetResult();
                    new JsonDraftStore(Path.Combine(root, "drafts.json")).LoadAsync().GetAwaiter().GetResult();
                    var existing = files.Where(file => File.Exists(Path.Combine(root, file))).ToArray();
                    string? backup = null;
                    if (existing.Length > 0)
                    {
                        backup = "legacy-" + Guid.NewGuid().ToString("N");
                        string backupRoot = Path.Combine(root, "backups", backup);
                        Directory.CreateDirectory(backupRoot);
                        foreach (string file in existing)
                        {
                            using var input = File.OpenRead(Path.Combine(root, file));
                            using var output = new FileStream(Path.Combine(backupRoot, file), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            input.CopyTo(output); output.Flush(flushToDisk: true);
                        }
                    }
                    manifest = new() { SchemaVersion = 1, ProfileId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
                        CacheMaximumBytes = SqliteMailCache.DefaultMaximumBytes, LegacyBackup = backup };
                    string temporary = Path.Combine(root, "profile-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { JsonSerializer.Serialize(stream, manifest, ProfileJsonContext.Default.ProfileManifest); stream.Flush(flushToDisk: true); }
                        File.Move(temporary, manifestPath); // Commit marker last. Interrupted adoption can safely run again.
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                finally { foreach (var item in locks) item.Dispose(); }
            }
            return new(root, manifest, lease);
        }
        catch (JsonException error) { lease.Dispose(); throw new InvalidDataException("Profile manifest is corrupt; it was not reset.", error); }
        catch { lease.Dispose(); throw; }
    }

    internal void SaveStoragePolicy(ProfileStoragePolicy policy, CancellationToken token)
    {
        string path = Path.Combine(DirectoryPath, "profile.json");
        ProfileManifest current;
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length > 65536) throw new InvalidDataException("Profile manifest exceeds its supported size.");
            try { current = JsonSerializer.Deserialize(stream, ProfileJsonContext.Default.ProfileManifest) ?? throw new InvalidDataException("Profile manifest is incomplete."); }
            catch (JsonException error) { throw new InvalidDataException("Profile manifest is invalid; storage policy was not changed.", error); }
        }
        if (current.SchemaVersion != 1 || current.ProfileId != Manifest.ProfileId || !Enum.IsDefined(current.DownloadPolicy) ||
            current.CacheMaximumBytes < 256 * 1024 || current.CacheMaximumBytes > 16L * 1024 * 1024 * 1024)
            throw new InvalidDataException("The profile changed or uses an unsupported schema; storage policy was not changed.");
        var candidate = current with { CacheMaximumBytes = policy.MaximumBytes, DownloadPolicy = policy.Downloads };
        string temporary = Path.Combine(DirectoryPath, "profile-policy-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, candidate, ProfileJsonContext.Default.ProfileManifest); stream.Flush(flushToDisk: true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
            Manifest = candidate;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() => _lease.Dispose();
}

public sealed record ProfileManifest
{
    [JsonRequired] public int SchemaVersion { get; init; }
    [JsonRequired] public Guid ProfileId { get; init; }
    [JsonRequired] public DateTimeOffset CreatedAt { get; init; }
    [JsonRequired] public long CacheMaximumBytes { get; init; }
    public string? LegacyBackup { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<OfflineDownloadPolicy>))]
    public OfflineDownloadPolicy DownloadPolicy { get; init; } = OfflineDownloadPolicy.OnDemand;
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(ProfileManifest))]
internal partial class ProfileJsonContext : JsonSerializerContext;
