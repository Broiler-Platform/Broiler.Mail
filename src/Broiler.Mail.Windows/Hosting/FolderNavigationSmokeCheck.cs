using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class FolderNavigationSmokeCheck
{
    internal static async Task RunAsync(AccountProfile account, IMailCache cache)
    {
        using var model = new InboxViewModel(new OfflineReceiver(), new ImmediateUiDispatcher(), cache);
        model.SetAccount(account); await model.CacheLoadTask;
        if (model.FolderRoles.Single(role => role.Role == MailFolderRole.Archive) is not { MailboxId: "Archive", IsOverride: true, IsConfirmed: false })
            throw new InvalidOperationException("Published offline folder-role override fixture failed.");
        if (!model.Folders.Any(folder => folder.MailboxId == "Archive") || !model.SelectFolder("Archive"))
            throw new InvalidOperationException("Published saved folder catalog fixture failed.");
        await model.CacheLoadTask;
        var key = new MailMessageKey(account.Id, "Archive", 7, 1);
        if (model.Messages.Count != 1 || model.Messages[0].Key != key)
            throw new InvalidOperationException("Published folder header scope fixture failed.");
        await model.SelectAsync(key);
        if (!model.IsBodyFromCache || model.Body?.PlainText != "Persisted archive fixture")
            throw new InvalidOperationException("Published folder body scope fixture failed.");
        await model.SetSelectedReadStateAsync(true);
        var storedRead = await cache.ReadSummariesAsync(new(account.Id, "Archive", 7), 1);
        if (!model.SelectedMessage!.IsRead || !storedRead[0].IsRead)
            throw new InvalidOperationException("Published confirmed read-state cache fixture failed.");
        await model.SetSelectedReadStateAsync(false);
        if (model.SelectedMessage!.IsRead || model.Body?.PlainText != "Persisted archive fixture")
            throw new InvalidOperationException("Published unread-state/body preservation fixture failed.");
        await model.SetSelectedStarStateAsync(true);
        var starred = await cache.ReadSummariesAsync(new(account.Id, "Archive", 7), 1);
        if (!model.SelectedMessage!.IsStarred || !starred[0].IsStarred || starred[0].IsRead)
            throw new InvalidOperationException("Published starred-state cache fixture failed.");
        await model.SetSelectedStarStateAsync(false);
        model.SelectFolder("INBOX"); await model.CacheLoadTask;
        await model.SelectAsync(new(account.Id, "INBOX", 7, 1));
        if (model.Body?.PlainText != "Persisted offline fixture")
            throw new InvalidOperationException("Published overlapping folder UID fixture failed.");
    }

    private sealed class OfflineReceiver : IMailFolderReceiver, IMailReadStateWriter, IMailStarStateWriter
    {
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in offline fixture.");
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in offline fixture.");
        public Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new MailFlagChangeResult(key, isStarred, MailFlagChangeStatus.Confirmed, "Synthetic writer confirmation")); }
        public Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new ReadStateChangeResult(key, isRead, MailFlagChangeStatus.Confirmed, "Synthetic writer confirmation")); }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network in offline fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network in offline fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network in offline fixture.");
    }
}
