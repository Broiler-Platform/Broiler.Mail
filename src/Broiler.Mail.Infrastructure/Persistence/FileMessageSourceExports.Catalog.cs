using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

internal sealed record SourceExportCatalogEntry(MailMessageKey Key, string FileName, int Bytes, string Sha256, DateTimeOffset SavedAt);
internal sealed record SourceExportCatalogState(IReadOnlyList<SourceExportCatalogEntry> Entries);

public sealed partial class FileMessageSourceExports
{
    private string CatalogPath => Path.Combine(_profile, "source-export-catalog.json");
    private JsonConfigurationFile<SourceExportCatalogState> Catalog => new(CatalogPath, () => new([]), ValidateCatalog,
        ConfigurationJsonContext.Storage.SourceExportCatalog, 512 * 1024);
    private void CheckCatalogFile()
    {
        if (File.Exists(CatalogPath) && (File.GetAttributes(CatalogPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Saved-original catalogs do not follow file links.");
    }
    private async Task CheckCatalogAsync(CancellationToken token)
    { CheckCatalogFile(); _ = await Catalog.ReadAsync(token).ConfigureAwait(false); }
    private Task RecordExportAsync(MessageSourceExport receipt, CancellationToken token)
        => Catalog.UpdateAsync(state =>
        {
            // Missing exports consume no file quota. Reclaim only their metadata when explicitly saving another export.
            var kept = state.Entries.Where(row => !ExportFileIsMissing(row.FileName)).ToArray();
            if (kept.Length >= MaximumFiles) throw new IOException("Saved-original receipt catalog reached its 64-entry limit.");
            return new(kept.Append(new(receipt.Key, Path.GetFileName(receipt.Path), receipt.Bytes, receipt.Sha256, DateTimeOffset.UtcNow)).ToArray());
        }, token);
    private bool ExportFileIsMissing(string fileName)
    {
        try { _ = File.GetAttributes(Path.Combine(ExportDirectory, fileName)); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        // Access/I/O failures are not evidence that a receipt may be discarded.
    }
    public async Task<IReadOnlyList<SavedMessageSourceExport>> ListSavedExportsAsync(MailMessageKey key, CancellationToken token = default)
    {
        ValidateCatalogKey(key); token.ThrowIfCancellationRequested(); CheckDirectory(_profile); CheckCatalogFile();
        // Listing an old profile does not create an export directory or invent receipts for existing files.
        await using var lease = await LockAsync(token).ConfigureAwait(false);
        var state = await Catalog.ReadAsync(token).ConfigureAwait(false);
        return Array.AsReadOnly(state.Entries.Where(row => row.Key == key).OrderByDescending(row => row.SavedAt).ThenBy(row => row.FileName, StringComparer.Ordinal)
            .Select(row => new SavedMessageSourceExport(new(row.Key, Path.Combine(ExportDirectory, row.FileName), row.Bytes, row.Sha256), row.SavedAt)).ToArray());
    }
    public async Task<IReadOnlyList<SavedMessageSourceExport>> ListAccountExportsAsync(AccountId account, CancellationToken token = default)
    {
        if (account.Value == Guid.Empty) throw new ArgumentException("A saved-original account identity is required.");
        token.ThrowIfCancellationRequested(); CheckDirectory(_profile);
        await using var lease = await LockAsync(token).ConfigureAwait(false); CheckCatalogFile();
        var state = await Catalog.ReadAsync(token).ConfigureAwait(false);
        return Array.AsReadOnly(state.Entries.Where(row => row.Key.AccountId == account).OrderByDescending(row => row.SavedAt).ThenBy(row => row.FileName, StringComparer.Ordinal)
            .Select(row => new SavedMessageSourceExport(new(row.Key, Path.Combine(ExportDirectory, row.FileName), row.Bytes, row.Sha256), row.SavedAt)).ToArray());
    }
    private static void ValidateCatalogKey(MailMessageKey key)
    {
        if (key.AccountId.Value == Guid.Empty || key.Uid == 0 || key.UidValidity == 0 || string.IsNullOrWhiteSpace(key.MailboxId) || key.MailboxId.Length > 1024 || key.MailboxId.Any(char.IsControl))
            throw new ArgumentException("An exact saved-original message identity is required.");
        for (int index = 0; index < key.MailboxId.Length; index++)
            if (char.IsSurrogate(key.MailboxId[index]) && (!char.IsHighSurrogate(key.MailboxId[index]) || ++index >= key.MailboxId.Length || !char.IsLowSurrogate(key.MailboxId[index])))
                throw new ArgumentException("Saved-original mailbox identity must contain valid Unicode.");
    }
    private static void ValidateCatalog(SourceExportCatalogState state)
    {
        if (state.Entries is null || state.Entries.Count > MaximumFiles) throw new ArgumentException("Use at most 64 saved-original receipts.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in state.Entries)
        {
            if (row is null) throw new ArgumentException("Invalid saved-original receipt."); ValidateCatalogKey(row.Key);
            if (row.FileName is null || row.FileName.Length != 44 || !row.FileName.StartsWith("message-", StringComparison.Ordinal) || !row.FileName.EndsWith(".eml", StringComparison.Ordinal) ||
                !Guid.TryParseExact(row.FileName.AsSpan(8,32), "N", out _) || !names.Add(row.FileName) || row.Bytes is < 1 or > IStreamingMessageSourceExports.MaximumBytes ||
                row.Sha256 is null || row.Sha256.Length != 64 || !row.Sha256.All(Uri.IsHexDigit) || row.SavedAt == default)
                throw new ArgumentException("Invalid or duplicate bounded saved-original receipt.");
        }
    }
}
