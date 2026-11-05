using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Services;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Verified snapshots and non-overwriting restores. Config locks and the cache gate define the snapshot boundary.</summary>
public sealed partial class ProfileStorageService(MailProfile profile, SqliteMailCache cache) : IProfileStorage, IMailCacheRecovery, IMailSearchIndex, IAccountDownloadedMailStorage, IOrphanDownloadedMailStorage
{
    private static readonly string[] ConfigurationFiles = ["accounts.json", "account-collection.json", "settings.json", "drafts.json", "recipients.json", "local-originals.json", "offline-folders.json", "account-navigation.json", "mail-cache.db.recovery.json", "mail-cache.db.intents.json"];
    private static readonly string[] AllowedFiles = ["profile.json", "accounts.json", "account-collection.json", "settings.json", "drafts.json", "recipients.json", "local-originals.json", "offline-folders.json", "account-navigation.json", "mail-cache.db", "mail-cache.db.recovery.json", "mail-cache.db.intents.json"];
    public async Task<IReadOnlyList<MailUncertainAction>> ReadUncertainActionsAsync(CancellationToken token = default)
    {
        var actions = await cache.ReadUncertainActionsAsync(token).ConfigureAwait(false);
        IReadOnlyList<Broiler.Mail.Core.Accounts.AccountProfile> accounts = File.Exists(Path.Combine(ProfileDirectory, "account-collection.json"))
            ? await new JsonAccountCollectionStore(ProfileDirectory).LoadAsync(token).ConfigureAwait(false)
            : await new JsonAccountStore(profile.AccountsPath).LoadAsync(token).ConfigureAwait(false);
        return actions.Select(action => action with { Description = (accounts.FirstOrDefault(account => account.Id == action.Source.AccountId)?.EmailAddress ?? "Removed account") + " — " + action.Description }).ToArray();
    }
    public async Task DismissUncertainActionAsync(Guid id, CancellationToken token = default)
    {
        if (!(await cache.ReadUncertainActionsAsync(token).ConfigureAwait(false)).Any(action => action.Id == id)) throw new InvalidOperationException("The unresolved action changed; refresh the list.");
        await CreateBackupAsync(Path.Combine(ProfileDirectory, "backups", "uncertain-" + Guid.NewGuid().ToString("N")), token).ConfigureAwait(false);
        await cache.DismissUncertainActionAsync(id, token).ConfigureAwait(false);
    }
    public Task<MailSearchIndexInfo> GetSearchIndexInfoAsync(CancellationToken token = default) => cache.GetSearchIndexInfoAsync(token);
    public Task RebuildSearchIndexAsync(CancellationToken token = default) => cache.RebuildSearchIndexAsync(token);
    public Task DisableSearchIndexAsync(CancellationToken token = default) => cache.DisableSearchIndexAsync(token);
    public int PendingRecoveryCount => cache.PendingRecoveryCount;
    public Task<int> GetPendingRecoveryCountAsync(CancellationToken token = default) => cache.GetPendingRecoveryCountAsync(token);
    public Task<MailCacheRecoveryReport> RepairAsync(CancellationToken token = default) => cache.RepairAsync(token);
    public string ProfileDirectory => profile.DirectoryPath;
    public ProfileStoragePolicy Policy => new(profile.Manifest.CacheMaximumBytes, profile.Manifest.DownloadPolicy);

    public Task<ProfileStoragePolicy> SetPolicyAsync(ProfileStoragePolicy policy, CancellationToken cancellationToken = default) =>
        cache.WithExclusiveConnectionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            cache.ApplyStoragePolicy(connection, policy, () => profile.SaveStoragePolicy(policy, cancellationToken));
            return Policy;
        }, cancellationToken);

    public Task<ProfileStorageInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        cache.WithExclusiveConnectionAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*),count(body) FROM messages";
            long headers, bodies;
            using (var reader = command.ExecuteReader()) { reader.Read(); headers = reader.GetInt64(0); bodies = reader.GetInt64(1); }
            long backups = 0;
            string folder = Path.Combine(ProfileDirectory, "backups");
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                { RecurseSubdirectories = true, MaxRecursionDepth = 32, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
                { cancellationToken.ThrowIfCancellationRequested(); backups += new FileInfo(file).Length; }
            return new ProfileStorageInfo(new FileInfo(profile.CachePath).Length, profile.Manifest.CacheMaximumBytes, headers, bodies, backups);
        }, cancellationToken);

    public Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default) =>
        cache.WithExclusiveConnectionAsync(connection =>
        {
            using var locks = AcquireConfigurationLocks(cancellationToken);
            return CreateSnapshot(connection, destinationDirectory, cancellationToken);
        }, cancellationToken);

    public Task<ProfileCleanupResult> ClearDownloadedMailAsync(string recoveryDirectory, CancellationToken cancellationToken = default) =>
        cache.WithExclusiveConnectionAsync(connection =>
        {
            using var locks = AcquireConfigurationLocks(cancellationToken);
            if (cache.GetPendingRecoveryCountAsync(cancellationToken).GetAwaiter().GetResult() != 0)
                throw new InvalidOperationException("Repair pending confirmed cache changes before clearing downloaded mail.");
            string backup = CreateSnapshot(connection, recoveryDirectory, cancellationToken);
            // Reset checkpoints before deleting the cache. An interruption can repeat saved work, never skip cleared bodies.
            string offline = Path.Combine(ProfileDirectory, "offline-folders.json");
            if (File.Exists(offline)) new JsonOfflineFolderStore(offline).ResetProgressWithLockHeldAsync(cancellationToken).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            using (var transaction = connection.BeginTransaction())
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "DELETE FROM messages; DELETE FROM mailbox_heads";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA user_version";
                if (Convert.ToInt64(command.ExecuteScalar()) >= 5) { command.CommandText = "DELETE FROM mailbox_snapshots; DELETE FROM mailbox_snapshot_uids"; command.ExecuteNonQuery(); }
                cancellationToken.ThrowIfCancellationRequested(); transaction.Commit();
            }
            // The deletion has committed. A failed compaction must never be reported as a failed deletion.
            cache.RemoveSearchIndex(connection);
            bool compacted = true;
            try { using var vacuum = connection.CreateCommand(); vacuum.CommandText = "VACUUM"; vacuum.ExecuteNonQuery(); }
            catch (SqliteException) { compacted = false; }
            return new ProfileCleanupResult(backup, compacted);
        }, cancellationToken);

    private string CreateSnapshot(SqliteConnection connection, string destination, CancellationToken token)
    {
        var target = NewDestination(destination);
        try
        {
            new JsonAccountStore(profile.AccountsPath).LoadAsync(token).GetAwaiter().GetResult();
            if (File.Exists(Path.Combine(profile.DirectoryPath, "account-collection.json")))
                new JsonAccountCollectionStore(profile.DirectoryPath).LoadStateAsync(token).GetAwaiter().GetResult();
            new JsonSettingsStore(profile.SettingsPath).LoadAsync(token).GetAwaiter().GetResult();
            new JsonDraftStore(profile.DraftsPath).LoadAsync(token).GetAwaiter().GetResult();
            var files = new List<ProfileBackupFile>();
            foreach (string name in AllowedFiles.Where(name => name != "mail-cache.db"))
            {
                string source = Path.Combine(ProfileDirectory, name);
                if (File.Exists(source)) files.Add(CopyAndHash(source, Path.Combine(target.Stage, name), name, token));
            }
            string database = Path.Combine(target.Stage, "mail-cache.db");
            using (var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
            { snapshot.Open(); connection.BackupDatabase(snapshot); }
            token.ThrowIfCancellationRequested();
            SqliteMailCache.ValidateSnapshot(database);
            files.Add(HashFile(database, "mail-cache.db", token));
            var manifest = new ProfileBackupManifest { SchemaVersion = 1, ProfileId = profile.Manifest.ProfileId,
                CreatedAt = DateTimeOffset.UtcNow, Files = files };
            WriteManifest(Path.Combine(target.Stage, "backup.json"), manifest);
            token.ThrowIfCancellationRequested();
            // Check the staged files as well, so a complete backup is never advertised before verification.
            ValidateBackup(target.Stage, token);
            Directory.Move(target.Stage, target.Destination);
            return target.Destination;
        }
        finally { target.Cleanup(); }
    }

    public Task<string> RestoreBackupAsync(string backupDirectory, string newProfileDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            string source = Path.GetFullPath(backupDirectory);
            string destination = Path.GetFullPath(newProfileDirectory);
            if (Within(destination, source)) throw new ArgumentException("Restore into a new folder outside the backup.");
            var manifest = ReadBackupManifest(source);
            var target = NewDestination(destination);
            try
            {
                foreach (var file in manifest.Files)
                {
                    var copied = CopyAndHash(Path.Combine(source, file.Name), Path.Combine(target.Stage, file.Name), file.Name,
                        cancellationToken, file.Bytes);
                    if (!string.Equals(copied.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Backup checksum verification failed. No profile was restored.");
                }
                ValidateProfileFiles(target.Stage, manifest.ProfileId);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(target.Stage, target.Destination); // Atomic publication; never overwrites an existing profile.
                return target.Destination;
            }
            finally { target.Cleanup(); }
        }, cancellationToken);

    private static void ValidateBackup(string directory, CancellationToken token)
    {
        var manifest = ReadBackupManifest(directory);
        foreach (var file in manifest.Files)
        {
            var actual = HashFile(Path.Combine(directory, file.Name), file.Name, token);
            if (actual.Bytes != file.Bytes || !string.Equals(actual.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup verification failed.");
        }
        ValidateProfileFiles(directory, manifest.ProfileId);
    }

    private static ProfileBackupManifest ReadBackupManifest(string directory)
    {
        using var stream = File.OpenRead(Path.Combine(directory, "backup.json"));
        if (stream.Length > 65536) throw new InvalidDataException("Backup manifest exceeds the supported limit.");
        ProfileBackupManifest manifest;
        try { manifest = JsonSerializer.Deserialize(stream, BackupJsonContext.Default.ProfileBackupManifest) ?? throw new InvalidDataException("Missing backup manifest."); }
        catch (JsonException error) { throw new InvalidDataException("The backup manifest is invalid.", error); }
        if (manifest.SchemaVersion != 1 || manifest.ProfileId == Guid.Empty || manifest.Files is null || manifest.Files.Count < 2 || manifest.Files.Count > AllowedFiles.Length ||
            manifest.Files.Any(file => file is null || !AllowedFiles.Contains(file.Name, StringComparer.Ordinal) || file.Bytes < 0 ||
                file.Bytes > MaximumBackupFileBytes(file.Name) ||
                file.Sha256 is null || file.Sha256.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character))) ||
            manifest.Files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count ||
            !manifest.Files.Any(file => file.Name == "profile.json") || !manifest.Files.Any(file => file.Name == "mail-cache.db"))
            throw new InvalidDataException("Unsupported or incomplete backup. No profile was restored.");
        return manifest;
    }

    private static long MaximumBackupFileBytes(string name) => name switch
    {
        "mail-cache.db" => 16L * 1024 * 1024 * 1024,
        "drafts.json" => JsonDraftStore.MaximumFileBytes,
        _ => 4 * 1024 * 1024,
    };

    private static void ValidateProfileFiles(string directory, Guid expectedId)
    {
        // Open validates the actual profile manifest/version but performs no adoption (manifest is required above).
        using var candidate = MailProfile.Open(directory);
        if (candidate.Manifest.ProfileId != expectedId) throw new InvalidDataException("Backup profile identity mismatch.");
        new JsonAccountStore(candidate.AccountsPath).LoadAsync().GetAwaiter().GetResult();
        if (File.Exists(Path.Combine(directory, "account-collection.json")))
            new JsonAccountCollectionStore(directory).LoadStateAsync().GetAwaiter().GetResult();
        new JsonSettingsStore(candidate.SettingsPath).LoadAsync().GetAwaiter().GetResult();
        new JsonDraftStore(candidate.DraftsPath).LoadAsync().GetAwaiter().GetResult();
        var receipts = new CacheRecoveryJournal(candidate.CachePath + ".recovery.json").ReadAsync().GetAwaiter().GetResult();
        var intents = new MutationIntentJournal(candidate.CachePath).ReadAsync().GetAwaiter().GetResult();
        MutationIntentJournal.ValidateReceiptLinks(intents, receipts);
        string recipients = Path.Combine(directory, "recipients.json");
        if (File.Exists(recipients)) new JsonRecipientBook(recipients).ValidateSnapshotAsync().GetAwaiter().GetResult();
        if (File.Exists(Path.Combine(directory, "local-originals.json"))) new JsonLocalMessageOriginals(directory).ValidateSnapshotAsync().GetAwaiter().GetResult();
        string offline = Path.Combine(directory, "offline-folders.json");
        if (File.Exists(offline)) new JsonOfflineFolderStore(offline).ValidateSnapshotAsync().GetAwaiter().GetResult();
        string navigation = Path.Combine(directory, "account-navigation.json");
        if (File.Exists(navigation)) new JsonAccountNavigationStore(navigation).ValidateSnapshotAsync().GetAwaiter().GetResult();
        SqliteMailCache.ValidateSnapshot(candidate.CachePath);
    }

    private IDisposable AcquireConfigurationLocks(CancellationToken token)
    {
        var locks = new List<FileStream>();
        try
        {
            foreach (var name in ConfigurationFiles)
            {
                var elapsed = Stopwatch.StartNew();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    try { locks.Add(new FileStream(Path.Combine(ProfileDirectory, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); break; }
                    catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(3)) { token.WaitHandle.WaitOne(25); }
                }
            }
            return new Locks(locks);
        }
        catch { foreach (var item in locks) item.Dispose(); throw; }
    }

    private static ProfileBackupFile CopyAndHash(string source, string destination, string name, CancellationToken token, long? expectedBytes = null)
    {
        using var input = File.OpenRead(source);
        if (expectedBytes is { } expected && input.Length != expected) throw new InvalidDataException("Backup file size does not match its manifest.");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536]; long count = 0; int read;
        while ((read = input.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested(); count += read;
            if (expectedBytes is { } limit && count > limit) throw new InvalidDataException("Backup file changed during restore.");
            output.Write(buffer, 0, read); hash.AppendData(buffer, 0, read);
        }
        output.Flush(flushToDisk: true);
        if (expectedBytes is { } total && total != count) throw new InvalidDataException("Backup file is incomplete.");
        return new() { Name = name, Bytes = count, Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
    }

    private static ProfileBackupFile HashFile(string path, string name, CancellationToken token)
    {
        using var input = File.OpenRead(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536]; int read; long count = 0;
        while ((read = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); count += read; }
        return new() { Name = name, Bytes = count, Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
    }

    private static void WriteManifest(string path, ProfileBackupManifest manifest)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, manifest, BackupJsonContext.Default.ProfileBackupManifest); stream.Flush(flushToDisk: true);
    }

    private static StagedDirectory NewDestination(string directory)
    {
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new, unused destination folder. Existing folders are never overwritten.");
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Choose a destination folder.");
        Directory.CreateDirectory(parent);
        string stage = Path.GetFullPath(Path.Combine(parent, ".mail-pending-" + Guid.NewGuid().ToString("N")));
        if (!Within(stage, parent) || stage == parent) throw new IOException("Invalid staging directory.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(stage);
        else Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new(destination, stage, parent);
    }
    private static bool Within(string path, string parent) => path.Equals(parent, PathComparison) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, PathComparison);
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record StagedDirectory(string Destination, string Stage, string Parent)
    {
        public void Cleanup()
        {
            if (Within(Stage, Parent) && Stage != Parent && Path.GetFileName(Stage).StartsWith(".mail-pending-", StringComparison.Ordinal) && Directory.Exists(Stage))
                Directory.Delete(Stage, recursive: true);
        }
    }
    private sealed class Locks(List<FileStream> streams) : IDisposable
    { public void Dispose() { foreach (var stream in streams) stream.Dispose(); } }
}

internal sealed record ProfileBackupManifest
{
    [JsonRequired] public int SchemaVersion { get; init; }
    [JsonRequired] public Guid ProfileId { get; init; }
    [JsonRequired] public DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<ProfileBackupFile> Files { get; init; }
}
internal sealed record ProfileBackupFile
{
    public required string Name { get; init; }
    [JsonRequired] public long Bytes { get; init; }
    public required string Sha256 { get; init; }
}
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(ProfileBackupManifest))]
internal partial class BackupJsonContext : JsonSerializerContext;
