using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Protocols.Imap;

namespace Broiler.Mail.Infrastructure.Mail;

public sealed partial class ImapMailReceiver
{
    public Task<MailMailboxSnapshot> GetMailboxSnapshotAsync(AccountProfile account, string mailboxId, CancellationToken token = default)
    {
        mailboxId = ImapMailboxName.Validate(mailboxId);
        return WithConnectionAsync(account, async (client, cancellation) =>
        {
            var before = await client.ExamineMailboxAsync(mailboxId, cancellation, boundedSnapshot: true).ConfigureAwait(false);
            if (before.Count is < 0 or > MailMailboxSnapshot.MaximumMessages)
                throw new MailConnectionException("Full-folder header snapshots currently support at most 4,096 messages. Nothing was replaced; ordinary paged receiving remains available.");
            var mailbox = new MailboxKey(account.Id, mailboxId, before.UidValidity);
            var uids = await client.GetSnapshotUidsAsync(MailMailboxSnapshot.MaximumMessages, cancellation).ConfigureAwait(false);
            if (uids.Length != before.Count || before.UidValidity == 0 || before.UidNext is {} next && (next == 0 || uids.Length > 0 && next <= uids[^1])) throw InboxChanged();
            var messages = new List<MailMessageSummary>(uids.Length);
            for (int start = 1; start <= before.Count; start += 500)
            {
                int end = Math.Min(start + 499, before.Count);
                var batch = await client.FetchSummariesAsync(start, end, cancellation, boundedSnapshot: true).ConfigureAwait(false);
                if (batch.Count != end - start + 1 || batch.Select(row => row.SequenceNumber).Distinct().Count() != batch.Count ||
                    batch.Any(row => row.SequenceNumber < start || row.SequenceNumber > end || row.Uid != uids[row.SequenceNumber - 1] || row.Subject is null && row.From is null))
                    throw InboxChanged();
                messages.AddRange(batch.Select(row => new MailMessageSummary
                {
                    Key = new(account.Id, mailboxId, before.UidValidity, row.Uid),
                    Sender = MessageTextDecoder.Header(row.Sender ?? row.From, "(Unknown sender)"),
                    Subject = MessageTextDecoder.Header(row.Subject, "(No subject)"),
                    ReceivedAt = row.InternalDate ?? row.EnvelopeDate, IsRead = row.IsSeen, IsStarred = row.IsFlagged,
                }));
            }
            var after = await client.ExamineMailboxAsync(mailboxId, cancellation, boundedSnapshot: true).ConfigureAwait(false);
            var finalUids = await client.GetSnapshotUidsAsync(MailMailboxSnapshot.MaximumMessages, cancellation).ConfigureAwait(false);
            if (before != after || !uids.SequenceEqual(finalUids)) throw InboxChanged();
            cancellation.ThrowIfCancellationRequested();
            return new MailMailboxSnapshot(mailbox, Array.AsReadOnly(messages.OrderByDescending(row => row.Key.Uid).ToArray()), DateTimeOffset.UtcNow);
        }, token);
    }
}
