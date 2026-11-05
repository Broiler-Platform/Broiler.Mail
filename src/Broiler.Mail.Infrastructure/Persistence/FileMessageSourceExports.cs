using System.Diagnostics;
using System.Security.Cryptography;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Bounded explicit exports, separate from the cache and never opened automatically.</summary>
public sealed partial class FileMessageSourceExports : IMessageSourceExports, IStreamingMessageSourceExports, IVerifiedMessageSourceExports, IMessageAttachmentExports, IMessageSourceExportCatalog, IAccountSourceExportCatalog, ILocalMessageOriginals, ILocalOriginalManagement
{
    // Bounded process-local coordination prevents sibling writers from exhausting the interprocess lock deadline.
    private static readonly SemaphoreSlim[] ExportGates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private JsonLocalMessageOriginals LocalOriginals => new(_profile, _profileStorage);
    private readonly IProfileStorage? _profileStorage;
    public bool CanRemove => LocalOriginals.CanRemove;
    public Task<LocalOriginalPreview> DecodeAsync(LocalOriginalContent content, CancellationToken token = default) => LocalOriginals.DecodeAsync(content, token);
    public Task<LocalOriginalExport> ExportAsync(LocalOriginalReceipt receipt, string destination, CancellationToken token = default) => LocalOriginals.ExportAsync(receipt, destination, token);
    public Task<LocalOriginalRemoval> RemoveAsync(LocalOriginalReceipt receipt, CancellationToken token = default) => LocalOriginals.RemoveAsync(receipt, token);
    public Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default) => LocalOriginals.OpenFileAsync(path, token);
    public Task<LocalOriginalReceipt> ImportAsync(Broiler.Mail.Core.Accounts.AccountId account, LocalOriginalContent content, CancellationToken token = default) => LocalOriginals.ImportAsync(account, content, token);
    public Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(Broiler.Mail.Core.Accounts.AccountId account, CancellationToken token = default) => LocalOriginals.ListImportsAsync(account, token);
    public Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt receipt, CancellationToken token = default) => LocalOriginals.ReadImportAsync(receipt, token);
    public const int MaximumFiles = 64;
    public const long MaximumDirectoryBytes = 128L * 1024 * 1024;
    private readonly string _profile;
    public string ExportDirectory { get; }
    public FileMessageSourceExports(string profileDirectory, IProfileStorage? profileStorage = null)
    { _profileStorage = profileStorage; _profile = Path.GetFullPath(profileDirectory); ExportDirectory = Path.Combine(_profile, "source-exports"); }
    public Task<MessageSourceExport> SaveAsync(MailMessageSource source, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(source.Bytes);
        if (source.Bytes.Length == 0 || source.Bytes.Length > MailMessageSource.MaximumBytes) throw new ArgumentException("Buffered originals must contain 1 byte to 2 MiB.");
        byte[] bytes = source.Bytes.ToArray();
        return SaveStreamAsync(source.Key, async (destination, maximum, cancellation) =>
        {
            if (bytes.Length > maximum) throw new IOException("Original-source export exceeds the remaining directory quota.");
            await destination.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            return bytes.Length;
        }, token);
    }

    public async Task<MessageSourceExport> SaveStreamAsync(Broiler.Mail.Core.Messages.MailMessageKey key,
        Func<Stream, int, CancellationToken, Task<int>> copy, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(copy);
        if (key.AccountId.Value == Guid.Empty || key.Uid == 0 || key.UidValidity == 0 || string.IsNullOrWhiteSpace(key.MailboxId) || key.MailboxId.Length > 1024 || key.MailboxId.Any(char.IsControl))
            throw new ArgumentException("An exact message identity is required.");
        for (int index = 0; index < key.MailboxId.Length; index++)
            if (char.IsSurrogate(key.MailboxId[index]) && (!char.IsHighSurrogate(key.MailboxId[index]) || ++index >= key.MailboxId.Length || !char.IsLowSurrogate(key.MailboxId[index])))
                throw new ArgumentException("Source mailbox identity must contain valid Unicode.");
        token.ThrowIfCancellationRequested();
        CheckDirectory(_profile); Directory.CreateDirectory(ExportDirectory); CheckDirectory(ExportDirectory);
        await using var lease = await LockAsync(token).ConfigureAwait(false);
        await CheckCatalogAsync(token).ConfigureAwait(false);
        var files = Directory.EnumerateFiles(ExportDirectory).Take(MaximumFiles + 1).ToArray();
        long remaining = MaximumDirectoryBytes - files.Sum(path => new FileInfo(path).Length);
        if (files.Length >= MaximumFiles || remaining <= 0)
            throw new IOException("Original-source exports reached the 64-file/128 MiB limit. Move or remove exports explicitly before retrying; nothing was evicted.");
        string id = Guid.NewGuid().ToString("N"), final = Path.Combine(ExportDirectory, "message-" + id + ".eml"), temporary = Path.Combine(ExportDirectory, id + ".part");
        try
        {
            int length;
            byte[] hash;
            int maximum = (int)Math.Min(remaining, IStreamingMessageSourceExports.MaximumBytes);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                length = await copy(stream, maximum, token).ConfigureAwait(false);
                if (length < 1 || length > maximum || stream.Length != length) throw new InvalidDataException("Original-source length did not match its verified size.");
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                stream.Position = 0;
                hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested(); CheckDirectory(ExportDirectory);
            File.Move(temporary, final); // Never overwrite an existing export.
            var receipt = new MessageSourceExport(key, final, length, Convert.ToHexString(hash).ToLowerInvariant());
            await RecordExportAsync(receipt, token).ConfigureAwait(false); return receipt;
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public async Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport export, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(export); token.ThrowIfCancellationRequested();
        if (export.Key.AccountId.Value == Guid.Empty || export.Key.Uid == 0 || export.Key.UidValidity == 0 || string.IsNullOrWhiteSpace(export.Key.MailboxId) || export.Key.MailboxId.Length > 1024 || export.Key.MailboxId.Any(char.IsControl) || export.Bytes is < 1 or > IStreamingMessageSourceExports.MaximumBytes ||
            export.Sha256 is null || export.Sha256.Length != 64 || !export.Sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid source export receipt.");
        string full = Path.GetFullPath(export.Path), name = Path.GetFileName(full);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(full), ExportDirectory, comparison) || name.Length != 44 || !name.StartsWith("message-", StringComparison.Ordinal) ||
            !name.EndsWith(".eml", StringComparison.Ordinal) || !Guid.TryParseExact(name.AsSpan(8,32), "N", out _))
            throw new ArgumentException("Only this profile's generated source exports can be inspected.");
        CheckDirectory(_profile); CheckDirectory(ExportDirectory);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Source inspection does not follow file links.");
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length != export.Bytes) throw new InvalidDataException("The source export size changed. Existing preview was cleared; download a new original export if needed.");
        byte[] bytes = new byte[export.Bytes]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (stream.ReadByte() != -1 || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), export.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The source export changed or its checksum does not match. Existing preview was cleared.");
        return new(export.Key, bytes);
    }
    private static void CheckDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Source exports require a regular profile directory, not a directory link.");
    }
    private async Task<IAsyncDisposable> LockAsync(CancellationToken token)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var gate = ExportGates[(uint)comparer.GetHashCode(Path.TrimEndingDirectorySeparator(_profile)) % ExportGates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { return new ExportLease(new FileStream(Path.Combine(_profile, "source-exports.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), gate); }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(3)) { await Task.Delay(50, token).ConfigureAwait(false); }
            }
        }
        catch { gate.Release(); throw; }
    }
    private sealed class ExportLease(FileStream file, SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { try { file.Dispose(); } finally { gate.Release(); } }
            return ValueTask.CompletedTask;
        }
    }
}
