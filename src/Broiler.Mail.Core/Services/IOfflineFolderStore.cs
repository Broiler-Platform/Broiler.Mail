using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicit account-scoped folder selections; this store never starts synchronization.</summary>
public interface IOfflineFolderStore
{
    Task<IReadOnlyList<string>> LoadAsync(AccountId account, CancellationToken token = default);
    Task SaveAsync(AccountId account, IReadOnlyList<string> mailboxIds, CancellationToken token = default);
}
/// <summary>Optional durable manual-download checkpoint support. Null means start; empty means completed.</summary>
public interface IOfflineFolderProgressStore
{
    Task<IReadOnlyList<MailMessageKey>?> LoadProgressAsync(AccountId account, IReadOnlyList<string> selection, CancellationToken token = default);
    Task SaveProgressAsync(AccountId account, IReadOnlyList<string> selection, IReadOnlyList<MailMessageKey>? remaining, CancellationToken token = default);
}
public sealed record OfflineFolderSelection(AccountId AccountId, IReadOnlyList<string> MailboxIds)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MailMessageKey>? Remaining { get; init; }
}
public sealed record OfflineFolderState(IReadOnlyList<OfflineFolderSelection> Accounts);
