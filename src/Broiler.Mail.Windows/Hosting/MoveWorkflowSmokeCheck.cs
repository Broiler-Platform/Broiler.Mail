using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class MoveWorkflowSmokeCheck
{
    internal static async Task RunAsync(string path, AccountProfile account)
    {
        var cache = new SqliteMailCache(path); var source = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var from = new MailboxKey(account.Id, "INBOX", 7); var destination = new MailMessageKey(account.Id, "Archive", 9, 900);
        await cache.SaveSummariesAsync(from, [new() { Key = source, Sender = "Synthetic sender", Subject = "Move fixture", IsRead = true, IsStarred = true }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(from, new(source, "Moved offline fixture", "<p>Moved offline fixture</p>"));
        var mover = new SyntheticMover();
        using (var model = new InboxViewModel(mover, new ImmediateUiDispatcher(), cache))
        {
            model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); await model.SelectAsync(source);
            await model.ArchiveSelectedAsync();
            if (mover.Calls != 1 || model.Messages.Count != 0 || model.SelectedMessage is not null || model.Body is not null || model.CanLoadOlder)
                throw new InvalidOperationException("Published confirmed move model fixture failed.");
            if (!model.CanUndoMove) throw new InvalidOperationException("Published undo availability fixture failed.");
            await model.UndoLastMoveAsync(); await model.CacheLoadTask;
            if (mover.Calls != 2 || model.CanUndoMove || model.Messages.Count != 1 || model.Messages[0].Key != new MailMessageKey(account.Id, "INBOX", 7, 1000))
                throw new InvalidOperationException("Published verified undo identity fixture failed.");
            await model.SelectAsync(model.Messages[0].Key);
            if (!model.IsBodyFromCache || model.Body?.PlainText != "Moved offline fixture")
                throw new InvalidOperationException("Published undo offline body fixture failed.");
            await model.ArchiveSelectedAsync();
            destination = new(account.Id, "Archive", 9, 901);
        }
        using var reopened = new InboxViewModel(new SyntheticMover(), new ImmediateUiDispatcher(), new SqliteMailCache(path));
        reopened.SetAccount(account); await reopened.CacheLoadTask;
        if (reopened.Messages.Count != 0 || !reopened.SelectFolder("Archive"))
            throw new InvalidOperationException("Published moved cache restart/source fixture failed.");
        await reopened.CacheLoadTask;
        if (reopened.Messages.Count != 1 || reopened.Messages[0].Key != destination || !reopened.Messages[0].IsRead || !reopened.Messages[0].IsStarred)
            throw new InvalidOperationException("Published moved header/flag identity fixture failed.");
        await reopened.SelectAsync(destination);
        if (!reopened.IsBodyFromCache || reopened.Body?.Key != destination || reopened.Body.PlainText != "Moved offline fixture")
            throw new InvalidOperationException("Published rekeyed offline body fixture failed.");
    }

    private sealed class SyntheticMover : IMailFolderReceiver, IMailMover
    {
        private uint _archiveUid = 900;
        public int Calls { get; private set; }
        public Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey source, string destination, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new MailMoveResult(source, destination, MailMoveStatus.Confirmed,
            new(account.Id, destination, destination == "INBOX" ? 7u : 9u, destination == "INBOX" ? 1000u : _archiveUid++), "Synthetic confirmed move")); }
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", null, true, []), new("Archive", null, true, ["\\Archive"])]);
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
    }
}
