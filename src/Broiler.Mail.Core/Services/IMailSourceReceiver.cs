using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit, read-only retrieval of original RFC 5322/MIME octets; never reconstructs a decoded preview.</summary>
public interface IMailSourceReceiver
{
    Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey message, CancellationToken token = default);
}
public sealed record MailMessageSource(MailMessageKey Key, byte[] Bytes)
{
    public const int MaximumBytes = 2 * 1024 * 1024;
}

/// <summary>Copies verified original octets to a staging destination owned by the caller.</summary>
public interface IStreamingMailSourceReceiver
{
    Task<int> CopySourceAsync(AccountProfile account, MailMessageKey message, Stream destination, int maximumBytes,
        Action<int, int>? progress = null, CancellationToken token = default);
}

public interface IStreamingMessageSourceExports
{
    const int MaximumBytes = 12 * 1024 * 1024;
    Task<MessageSourceExport> SaveStreamAsync(MailMessageKey key, Func<Stream, int, CancellationToken, Task<int>> copy, CancellationToken token = default);
}
public interface IMessageSourceExports
{
    string ExportDirectory { get; }
    Task<MessageSourceExport> SaveAsync(MailMessageSource source, CancellationToken token = default);
}
public sealed record MessageSourceExport(MailMessageKey Key, string Path, int Bytes, string Sha256);

/// <summary>Read only an export receipt after validating its location, length and checksum; never fetches from a server.</summary>
public interface IVerifiedMessageSourceExports
{
    Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport export, CancellationToken token = default);
}

/// <summary>Bounded local receipt metadata for one exact message. Selecting a row must reverify its file.</summary>
public interface IMessageSourceExportCatalog
{
    Task<IReadOnlyList<SavedMessageSourceExport>> ListSavedExportsAsync(MailMessageKey key, CancellationToken token = default);
}
public sealed record SavedMessageSourceExport(MessageSourceExport Export, DateTimeOffset SavedAt);

/// <summary>Local catalog metadata across an account's exported originals, independent of inbox cache.</summary>
public interface IAccountSourceExportCatalog
{
    Task<IReadOnlyList<SavedMessageSourceExport>> ListAccountExportsAsync(AccountId account, CancellationToken token = default);
}
