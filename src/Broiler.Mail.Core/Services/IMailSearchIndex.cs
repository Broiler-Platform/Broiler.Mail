namespace Broiler.Mail.Core.Services;

public interface IMailSearchIndex
{
    Task<MailSearchIndexInfo> GetSearchIndexInfoAsync(CancellationToken cancellationToken = default);
    Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default);
    Task DisableSearchIndexAsync(CancellationToken cancellationToken = default);
}
public sealed record MailSearchIndexInfo(bool Ready, long Documents, long Bytes, string Status)
{
    public long RetainedBodyDocuments { get; init; }
}
public sealed class MailSearchIndexUnavailableException(string message) : InvalidOperationException(message);
