using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed record UnifiedSavedInboxRow(AccountProfile Account, MailMessageSummary Message);
public sealed record UnifiedSavedInboxFailure(AccountProfile Account, string Code);
public sealed record UnifiedSavedInboxSnapshot(IReadOnlyList<UnifiedSavedInboxRow> Rows, IReadOnlyList<UnifiedSavedInboxFailure> Failures, int Accounts);

/// <summary>Bounded, explicit local INBOX aggregation. No receiver, sender or credentials are available here.</summary>
public static class UnifiedSavedInbox
{
    public const int MaximumAccounts = 32;
    public const int MaximumPerAccount = 50;
    public static async Task<UnifiedSavedInboxSnapshot> ReadAsync(IMailCache cache, IReadOnlyList<AccountProfile> accounts, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(accounts); token.ThrowIfCancellationRequested();
        var profiles = accounts.ToArray();
        if (profiles.Length > MaximumAccounts || profiles.Any(row => row is null || row.Id.Value == Guid.Empty) || profiles.Select(row => row.Id).Distinct().Count() != profiles.Length)
            throw new ArgumentException("Unified saved mail requires up to 32 distinct configured accounts.");
        var result = await Task.WhenAll(profiles.Select(ReadAccountAsync)).ConfigureAwait(false); token.ThrowIfCancellationRequested();
        var rows = result.SelectMany(row => row.Rows).OrderByDescending(row => row.Message.ReceivedAt)
            .ThenBy(row => row.Account.Id.Value).ThenByDescending(row => row.Message.Key.Uid).ToArray();
        return new(Array.AsReadOnly(rows), Array.AsReadOnly(result.Where(row => row.Failure is not null).Select(row => row.Failure!).ToArray()), profiles.Length);

        async Task<(IReadOnlyList<UnifiedSavedInboxRow> Rows, UnifiedSavedInboxFailure? Failure)> ReadAccountAsync(AccountProfile account)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10)); var requestToken = deadline.Token;
            try
            {
                var rows = await Task.Run(async () =>
                {
                    var head = await cache.GetLatestMailboxAsync(account.Id, "INBOX", requestToken).ConfigureAwait(false);
                    requestToken.ThrowIfCancellationRequested();
                    if (head is null) return Array.Empty<UnifiedSavedInboxRow>();
                    if (head.Key.AccountId != account.Id || head.Key.MailboxId != "INBOX" || head.Key.UidValidity == 0) throw new InvalidDataException("Mismatched saved mailbox.");
                    var messages = await cache.ReadSummariesAsync(head.Key, MaximumPerAccount, cancellationToken: requestToken).ConfigureAwait(false);
                    if (messages.Count > MaximumPerAccount || messages.Any(row => row.Key.AccountId != account.Id || row.Key.MailboxId != "INBOX" || row.Key.UidValidity != head.Key.UidValidity || row.Key.Uid == 0) ||
                        messages.Select(row => row.Key.Uid).Distinct().Count() != messages.Count || !messages.Select(row => row.Key.Uid).SequenceEqual(messages.Select(row => row.Key.Uid).OrderDescending()))
                        throw new InvalidDataException("Mismatched saved header page.");
                    var after = await cache.GetLatestMailboxAsync(account.Id, "INBOX", requestToken).ConfigureAwait(false);
                    if (after?.Key != head.Key) throw new InvalidDataException("Saved mailbox epoch changed.");
                    return messages.Select(row => new UnifiedSavedInboxRow(account, row)).ToArray();
                }, requestToken).WaitAsync(requestToken).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); return (Array.AsReadOnly(rows), null);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return ([], new(account, "Timeout")); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { token.ThrowIfCancellationRequested(); return ([], new(account, error.GetType().Name)); }
        }
    }
    public static async Task<MailMessageBody?> ReadBodyAsync(IMailCache cache, UnifiedSavedInboxRow row, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(row);
        if (row.Account.Id != row.Message.Key.AccountId || row.Message.Key.MailboxId != "INBOX" || row.Message.Key.Uid == 0 || row.Message.Key.UidValidity == 0) throw new ArgumentException("Invalid unified row identity.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10)); var requestToken = deadline.Token;
        var key = row.Message.Key;
        return await Task.Run(async () =>
        {
            var head = await cache.GetLatestMailboxAsync(key.AccountId, "INBOX", requestToken).ConfigureAwait(false);
            if (head?.Key.UidValidity != key.UidValidity || head.Key.AccountId != key.AccountId || head.Key.MailboxId != key.MailboxId) throw new InvalidDataException("Saved mailbox changed. Refresh the unified view.");
            requestToken.ThrowIfCancellationRequested();
            var body = await cache.ReadBodyAsync(head.Key, key.Uid, requestToken).ConfigureAwait(false);
            if (body is not null && body.Key != key) throw new InvalidDataException("Another message body was returned.");
            var after = await cache.GetLatestMailboxAsync(key.AccountId, "INBOX", requestToken).ConfigureAwait(false);
            if (after?.Key != head.Key) throw new InvalidDataException("Saved mailbox changed. Refresh the unified view.");
            return body;
        }, requestToken).WaitAsync(requestToken).ConfigureAwait(false);
    }
}
