using System.Security.Cryptography;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime;

namespace Broiler.Mail.Infrastructure.Persistence;

internal sealed record LocalOriginalEntry(LocalOriginalReceipt Receipt, byte[] Bytes);
internal sealed record LocalOriginalState(IReadOnlyList<LocalOriginalEntry> Entries);

/// <summary>Small, irreplaceable local originals, atomically stored and included in profile backups.</summary>
public sealed class JsonLocalMessageOriginals(string profileDirectory, IProfileStorage? profileStorage = null) : ILocalMessageOriginals, ILocalOriginalManagement
{
    public bool CanRemove => profileStorage is not null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileStorage.ProfileDirectory)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileDirectory)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public const int MaximumFiles = 16;
    public const int MaximumTotalBytes = 4 * 1024 * 1024;
    private string StorePath => Path.Combine(Path.GetFullPath(profileDirectory), "local-originals.json");
    private JsonConfigurationFile<LocalOriginalState> Store
    {
        get
        {
            if (File.Exists(StorePath) && (File.GetAttributes(StorePath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Local-original storage does not follow file links.");
            return new(StorePath, () => new([]), Validate, ConfigurationJsonContext.Storage.LocalOriginals, 8 * 1024 * 1024);
        }
    }
    public async Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || !Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".eml", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an absolute path to an .eml file.");
        string full = Path.GetFullPath(path);
        if ((File.GetAttributes(full) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Open a regular .eml file, not a file link or directory.");
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length is < 1 or > MailMessageSource.MaximumBytes) throw new InvalidDataException("Local originals must contain 1 byte to 2 MiB.");
        byte[] bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.ReadByte() != -1) throw new InvalidDataException("The local file changed while being read.");
        token.ThrowIfCancellationRequested();
        return new(MimeAttachmentCatalog.SafeFileName(Path.GetFileName(full)), bytes);
    }
    public async Task<LocalOriginalReceipt> ImportAsync(AccountId account, LocalOriginalContent content, CancellationToken token = default)
    {
        CheckAccount(account); ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(content.Bytes);
        if (content.Bytes.Length is < 1 or > MailMessageSource.MaximumBytes) throw new ArgumentException("Local originals must contain 1 byte to 2 MiB.");
        byte[] bytes = content.Bytes.ToArray();
        var receipt = new LocalOriginalReceipt(Guid.NewGuid(), account, content.FileName, bytes.Length, Hash(bytes), DateTimeOffset.UtcNow);
        var entry = new LocalOriginalEntry(receipt, bytes); Validate(new([entry])); token.ThrowIfCancellationRequested();
        await Store.UpdateAsync(state =>
        {
            if (state.Entries.Count >= MaximumFiles || state.Entries.Sum(row => (long)row.Bytes.Length) + bytes.Length > MaximumTotalBytes)
                throw new IOException("Local originals reached the 16-file/4 MiB profile limit. Nothing was evicted.");
            return new(state.Entries.Append(entry).ToArray());
        }, token).ConfigureAwait(false);
        return receipt;
    }
    public async Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(AccountId account, CancellationToken token = default)
    {
        CheckAccount(account); var state = await Store.ReadAsync(token).ConfigureAwait(false);
        return Array.AsReadOnly(state.Entries.Where(row => row.Receipt.AccountId == account).Select(row => row.Receipt)
            .OrderByDescending(row => row.ImportedAt).ThenBy(row => row.Id).ToArray());
    }
    public async Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt receipt, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(receipt); var state = await Store.ReadAsync(token).ConfigureAwait(false);
        var row = state.Entries.SingleOrDefault(row => row.Receipt == receipt) ?? throw new InvalidDataException("This exact local-original receipt is unavailable. List imports again.");
        return new(row.Receipt.FileName, row.Bytes.ToArray());
    }
    public async Task<LocalOriginalPreview> DecodeAsync(LocalOriginalContent content, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(content.Bytes);
        if (content.Bytes.Length is < 1 or > MailMessageSource.MaximumBytes) throw new ArgumentException("Local originals must contain 1 byte to 2 MiB.");
        token.ThrowIfCancellationRequested(); using var stream = new MemoryStream(content.Bytes.ToArray(), writable: false);
        return await Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.DecodeLocalAsync(stream, token).ConfigureAwait(false);
    }
    public async Task<LocalOriginalExport> ExportAsync(LocalOriginalReceipt receipt, string destination, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination) || !string.Equals(Path.GetExtension(destination), ".eml", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an absolute new .eml path in an existing directory.");
        var content = await ReadImportAsync(receipt, token).ConfigureAwait(false);
        string final = Path.GetFullPath(destination), directory = Path.GetDirectoryName(final)!;
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Export requires a regular existing directory.");
        string temporary = Path.Combine(directory, ".broiler-original-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            { await stream.WriteAsync(content.Bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(flushToDisk: true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, final); // Never overwrite or launch an existing file.
            return new(final, content.Bytes.Length, receipt.Sha256);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public async Task<LocalOriginalRemoval> RemoveAsync(LocalOriginalReceipt receipt, CancellationToken token = default)
    {
        if (!CanRemove) throw new InvalidOperationException("Removing imports requires verified profile backup support for this exact profile.");
        _ = await ReadImportAsync(receipt, token).ConfigureAwait(false);
        string backup = await profileStorage!.CreateBackupAsync(Path.Combine(profileStorage.ProfileDirectory, "backups", "before-local-original-removal-" + Guid.NewGuid().ToString("N")), token).ConfigureAwait(false);
        // Prove that the completed backup contains this exact target before deleting anything.
        _ = await new JsonLocalMessageOriginals(backup).ReadImportAsync(receipt, token).ConfigureAwait(false);
        await Store.UpdateAsync(state =>
        {
            if (!state.Entries.Any(row => row.Receipt == receipt)) throw new InvalidDataException("The selected import changed. List again before removing it; the backup was kept.");
            return new(state.Entries.Where(row => row.Receipt.Id != receipt.Id).ToArray());
        }, token).ConfigureAwait(false);
        return new(receipt, backup);
    }
    internal async Task ValidateSnapshotAsync() => _ = await Store.ReadAsync(default).ConfigureAwait(false);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void CheckAccount(AccountId account) { if (account.Value == Guid.Empty) throw new ArgumentException("A local-original account identity is required."); }
    private static void Validate(LocalOriginalState state)
    {
        if (state.Entries is null || state.Entries.Count > MaximumFiles) throw new ArgumentException("Use at most 16 local originals.");
        long total = 0; var ids = new HashSet<Guid>();
        foreach (var entry in state.Entries)
        {
            if (entry is null || entry.Receipt is not {} row || entry.Bytes is null) throw new ArgumentException("Invalid local original.");
            CheckAccount(row.AccountId);
            if (row.Id == Guid.Empty || !ids.Add(row.Id) || row.FileName is null || row.FileName.Length is < 1 or > 120 || row.FileName != MimeAttachmentCatalog.SafeFileName(row.FileName) ||
                row.Bytes is < 1 or > MailMessageSource.MaximumBytes || row.Bytes != entry.Bytes.Length || row.ImportedAt == default || row.Sha256 != Hash(entry.Bytes))
                throw new ArgumentException("Local-original identity, size or checksum is invalid.");
            total += entry.Bytes.Length;
        }
        if (total > MaximumTotalBytes) throw new ArgumentException("Local originals exceed the 4 MiB profile limit.");
    }
}
