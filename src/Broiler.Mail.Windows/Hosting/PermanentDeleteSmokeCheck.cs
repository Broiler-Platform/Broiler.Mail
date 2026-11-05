using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class PermanentDeleteSmokeCheck
{
    internal static async Task RunAsync(string path, AccountProfile account)
    {
        string trash = account.FolderOverrides.Trash ?? "Trash";
        var cache = new SqliteMailCache(path); var mailbox = new MailboxKey(account.Id, trash, 9);
        var key = new MailMessageKey(account.Id, trash, 9, 1); var other = new MailMessageKey(account.Id, trash, 9, 2);
        await cache.SaveSummariesAsync(mailbox, [new() { Key = key, Sender = "Synthetic sender", Subject = "Confirmed deletion fixture" },
            new() { Key = other, Sender = "Other sender", Subject = "Kept fixture", ReceivedAt = DateTimeOffset.UtcNow.AddDays(-1) }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(key, "Deleted fixture body")); await cache.SaveBodyAsync(mailbox, new(other, "Kept fixture body"));
        var deleter = new SyntheticDeleter(trash);
        using (var model = new InboxViewModel(deleter, new ImmediateUiDispatcher(), cache))
        {
            model.SetAccount(account); await model.CacheLoadTask; await model.RefreshFoldersAsync(); model.SelectFolder(trash); await model.CacheLoadTask;
            await model.SelectAsync(key);
            if (!model.RequestPermanentDelete() || deleter.Calls != 0) throw new InvalidOperationException("Published deletion confirmation fixture failed.");
            model.CancelPermanentDeleteConfirmation(); await model.ConfirmPermanentDeleteAsync();
            if (deleter.Calls != 0) throw new InvalidOperationException("Published canceled confirmation sent deletion.");
            model.RequestPermanentDelete(); await model.ConfirmPermanentDeleteAsync();
            if (deleter.Calls != 1 || model.Body is not null || model.Messages.Any(message => message.Key == key))
                throw new InvalidOperationException("Published confirmed deletion fixture failed.");
        }
        var reopened = new SqliteMailCache(path);
        var search = await reopened.SearchHeadersAsync(new(account.Id, "KEPT") { MailboxId = trash, IsRead = false });
        if (search.HasMore || search.Messages.Count != 1 || search.Messages[0].Key != other || !search.Messages[0].IsBodyCached)
            throw new InvalidOperationException("Published offline header search fixture failed.");
        if ((await reopened.SearchHeadersAsync(new(AccountId.New(), "fixture"))).Messages.Count != 0)
            throw new InvalidOperationException("Published offline header search crossed accounts.");
        using (var searchModel = new InboxViewModel(new SyntheticDeleter(trash), new ImmediateUiDispatcher(), reopened))
        {
            searchModel.SetAccount(account with { IsEnabled = false }); await searchModel.CacheLoadTask;
            if (!searchModel.SelectFolder(trash)) throw new InvalidOperationException("Published saved-search folder fixture failed.");
            await searchModel.CacheLoadTask;
            searchModel.SetSearch("kept"); searchModel.SetSearchReceivedWithinDays(7); await searchModel.SearchSavedHeadersAsync();
            if (!searchModel.IsSearchActive || searchModel.CanReceive || searchModel.CanLoadOlder || searchModel.Messages.Single().Key != other)
                throw new InvalidOperationException("Published saved-search mode fixture failed.");
            await searchModel.SelectAsync(other);
            if (searchModel.Body?.PlainText != "Kept fixture body" || !searchModel.IsBodyFromCache)
                throw new InvalidOperationException("Published saved-search body fixture failed.");
            await searchModel.ClearSearchAsync();
            if (searchModel.IsSearchActive) throw new InvalidOperationException("Published saved-search exit fixture failed.");
        }
        if (await reopened.ReadBodyAsync(mailbox, 1) is not null || (await reopened.ReadBodyAsync(mailbox, 2))?.PlainText != "Kept fixture body" ||
            (await reopened.ReadSummariesAsync(mailbox, 10)).Single().Key != other)
            throw new InvalidOperationException("Published deletion cache restart/scope fixture failed.");
    }

    private sealed class SyntheticDeleter(string trash) : IMailFolderReceiver, IMailPermanentDeleter
    {
        public int Calls { get; private set; }
        public Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new MailDeleteResult(key, MailDeleteStatus.Confirmed, "Synthetic confirmed deletion")); }
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) => Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", null, true, []), new(trash, null, true, ["\\Trash"])]);
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture.");
    }
}
