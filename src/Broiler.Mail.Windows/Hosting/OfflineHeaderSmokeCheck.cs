using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class OfflineHeaderSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath);
        var selections = new JsonOfflineFolderStore(Path.Combine(directory, "offline-folders.json")); await selections.SaveAsync(account.Id, ["INBOX", "Archive"]);
        var receiver = new Receiver(); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly, selections);
        model.SetAccount(account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        if (receiver.Calls != 0 || !model.CanReceiveOfflineHeaders) throw new InvalidOperationException("Published header receive was not explicit.");
        using (var view = new InboxView(model).CreateContent())
            if (!All(view).OfType<StandardButton>().Any(button => button.Text == "Receive selected-folder headers" && button.IsEnabled)) throw new InvalidOperationException("Published selected-folder header control missing.");
        await model.ReceiveOfflineHeadersAsync();
        if (model.ListProblem is not null || model.OfflineHeadersSaved != 51 || !model.HasOfflineHeaderContinuation || receiver.Calls != 2 || model.Messages.Count != 0)
            throw new InvalidOperationException("Published bounded selected-folder header batch failed.");
        await model.ReceiveOfflineHeadersAsync();
        if (model.ListProblem is not null || model.OfflineHeadersSaved != 52 || model.CanReceiveOfflineHeaders || receiver.Calls != 3)
            throw new InvalidOperationException("Published older selected-folder header batch failed.");
        await model.ShowSavedHeadersAsync(); if (model.Messages.Count != 50 || !model.IsShowingCachedMail || receiver.Calls != 3)
            throw new InvalidOperationException("Published local header display performed receive.");
        using (var reopened = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly, selections))
        {
            reopened.SetAccount(account with { IsEnabled = false }); await reopened.CacheLoadTask; await reopened.OfflineFolderLoadTask;
            if (reopened.Messages.Count != 50 || reopened.HasOfflineHeaderWalk || reopened.CanReceiveOfflineHeaders || receiver.Calls != 3)
                throw new InvalidOperationException("Published header restart/disabled-account policy failed.");
        }
        var storage = new ProfileStorageService(profile, cache); string backup = await storage.CreateBackupAsync(directory + "-backup");
        string restored = await storage.RestoreBackupAsync(backup, directory + "-restored"); var restoredCache = new SqliteMailCache(Path.Combine(restored, "mail-cache.db"));
        if ((await restoredCache.ReadSummariesAsync(new(account.Id, "INBOX", 7), 500)).Count != 51 ||
            (await restoredCache.ReadSummariesAsync(new(account.Id, "Archive", 7), 50)).Count != 1 ||
            (await storage.GetInfoAsync()).DownloadedBodyCount != 0) throw new InvalidOperationException("Published header backup/restore or no-body boundary failed.");
        model.BeginStorageMaintenance(); await storage.ClearDownloadedMailAsync(directory + "-cleanup"); model.EndStorageMaintenance(cacheCleared: true);
        await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        if (model.HasOfflineHeaderWalk || model.Messages.Count != 0 || receiver.Calls != 3) throw new InvalidOperationException("Published header cache cleanup reset failed.");
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }
    private sealed class Receiver : IMailFolderReceiver
    {
        public int Calls;
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string folder, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        {
            Calls++; if (maximumCount != 50) throw new InvalidOperationException("Unbounded header request.");
            int count = folder == "INBOX" ? 51 : 1, end = older?.NextIndex ?? count - 1, start = Math.Max(0, end - maximumCount + 1);
            var rows = Enumerable.Range(start, end - start + 1).Reverse().Select(index => new MailMessageSummary { Key = new(account.Id, folder, 7, (uint)index + 1), Subject = "Published header fixture", Sender = "fixture@example.test" }).ToArray();
            return Task.FromResult(new MailInboxPage(rows, start == 0 ? null : new(account.Id, 7, (uint)count + 1, count, start - 1) { MailboxId = folder }) { Mailbox = new(account.Id, folder, 7) });
        }
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => GetMailboxAsync(account, "INBOX", maximumCount, older, cancellationToken);
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No body receive in header fixture.");
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No automatic discovery.");
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No automatic connection test.");
    }
}
