using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Local file identities are independent of IMAP UIDs. No operation contacts a server.</summary>
public interface ILocalMessageOriginals
{
    Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default);
    Task<LocalOriginalReceipt> ImportAsync(AccountId account, LocalOriginalContent content, CancellationToken token = default);
    Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(AccountId account, CancellationToken token = default);
    Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt receipt, CancellationToken token = default);
}
public sealed record LocalOriginalContent(string FileName, byte[] Bytes);
public sealed record LocalOriginalReceipt(Guid Id, AccountId AccountId, string FileName, int Bytes, string Sha256, DateTimeOffset ImportedAt);

/// <summary>Optional local MIME reading and explicit file management, independent of server message identities.</summary>
public interface ILocalOriginalManagement
{
    bool CanRemove { get; }
    Task<LocalOriginalPreview> DecodeAsync(LocalOriginalContent content, CancellationToken token = default);
    Task<LocalOriginalExport> ExportAsync(LocalOriginalReceipt receipt, string destination, CancellationToken token = default);
    Task<LocalOriginalRemoval> RemoveAsync(LocalOriginalReceipt receipt, CancellationToken token = default);
}
public sealed record LocalOriginalPreview(string Subject, string Sender, string PlainText, bool IsHtmlFallback, bool IsTruncated, bool HasIncompleteParts)
{
    public const int MaximumHtmlCharacters = 128_000;
    public string? HtmlText { get; init; }
    public bool HtmlExceedsLimit { get; init; }
}
public sealed record LocalOriginalExport(string Path, int Bytes, string Sha256);
public sealed record LocalOriginalRemoval(LocalOriginalReceipt Removed, string BackupDirectory);
