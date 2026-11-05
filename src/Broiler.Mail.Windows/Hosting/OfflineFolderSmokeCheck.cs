using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class OfflineFolderSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath); var selections = new JsonOfflineFolderStore(Path.Combine(directory, "offline-folders.json"));
        await selections.SaveAsync(account.Id, ["INBOX", "Archive"]);
        foreach (string folder in new[] { "INBOX", "Archive" })
        {
            var key = new MailMessageKey(account.Id, folder, 7, 1);
            await cache.SaveSummariesAsync(new(account.Id, folder, 7), [new() { Key = key, Subject = "Offline fixture", Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
        }
        var receiver = new Receiver(); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly, selections);
        model.SetAccount(account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        if (receiver.Bodies != 0 || !model.CanDownloadOfflineFolders) throw new InvalidOperationException("Published offline selection started unexpected work.");
        await model.DownloadOfflineFoldersAsync();
        if (receiver.Bodies != 2 || model.DownloadCompleted != 2 || model.ListProblem is not null) throw new InvalidOperationException("Published selected-folder batch failed.");
        using (var view = new InboxView(model).CreateContent())
            if (!All(view).OfType<FormSection>().Any(section => section.Content.AccessibleName == "Offline-folder batch controls") ||
                !All(view).OfType<StandardButton>().Any(button => button.Text == "Download selected offline folders")) throw new InvalidOperationException("Published offline-folder controls missing.");
        var storage = new ProfileStorageService(profile, cache); string backup = await storage.CreateBackupAsync(directory + "-backup");
        string restored = await storage.RestoreBackupAsync(backup, directory + "-restored");
        if ((await new JsonOfflineFolderStore(Path.Combine(restored, "offline-folders.json")).LoadAsync(account.Id)).Count != 2 ||
            (await new SqliteMailCache(Path.Combine(restored, "mail-cache.db")).ReadBodyAsync(new(account.Id, "Archive", 7), 1))?.PlainText != "Saved Archive")
            throw new InvalidOperationException("Published selected-folder backup/restore failed.");
        if ((await new JsonOfflineFolderStore(Path.Combine(restored, "offline-folders.json")).LoadProgressAsync(account.Id, ["INBOX", "Archive"])) is not { Count: 0 })
            throw new InvalidOperationException("Published offline completion checkpoint was not restored.");
        await model.ToggleOfflineFolderAsync();
        if (model.IsSelectedFolderOffline || await cache.ReadBodyAsync(new(account.Id, "INBOX", 7), 1) is null) throw new InvalidOperationException("Published offline unselection deleted mail.");
        model.SetAccount(account with { IsEnabled = false }); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        if (model.CanDownloadOfflineFolders || receiver.Bodies != 2 || !model.SelectFolder("Archive")) throw new InvalidOperationException("Published disabled-account offline policy failed.");
        await model.CacheLoadTask; await model.SelectAsync(new(account.Id, "Archive", 7, 1));
        if (!model.IsBodyFromCache || receiver.Bodies != 2) throw new InvalidOperationException("Published selected-folder offline reading used the network.");
        var large = new MailboxKey(account.Id, "Large", 7);
        var headers = Enumerable.Range(1, 501).Select(i => new MailMessageSummary { Key = new(account.Id, "Large", 7, (uint)i), Subject = "Published paging fixture", Sender = "sender@example.test" }).ToArray();
        await cache.SaveSummariesAsync(large, headers.Take(500).ToArray(), DateTimeOffset.UtcNow); await cache.SaveSummariesAsync(large, headers.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        await selections.SaveAsync(account.Id, ["Large"]); model.SetAccount(account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        await model.DownloadOfflineFoldersAsync();
        if (model.DownloadCompleted != 500 || !model.HasOfflineFolderContinuation || receiver.Bodies != 502 || await cache.ReadBodyAsync(large, 1) is not null)
            throw new InvalidOperationException("Published bounded offline continuation failed.");
        using (var view = new InboxView(model).CreateContent())
            if (!All(view).OfType<StandardButton>().Any(button => button.Text == "Download next offline batch" && button.IsEnabled))
                throw new InvalidOperationException("Published offline continuation control missing.");
        using (var resumed = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly, new JsonOfflineFolderStore(Path.Combine(directory, "offline-folders.json"))))
        {
            resumed.SetAccount(account); await resumed.CacheLoadTask; await resumed.OfflineFolderLoadTask;
            if (!resumed.HasOfflineFolderContinuation || receiver.Bodies != 502) throw new InvalidOperationException("Published durable offline continuation did not resume locally.");
        }
        await model.DownloadOfflineFoldersAsync();
        if (model.DownloadCompleted != 1 || model.HasOfflineFolderContinuation || model.CanDownloadOfflineFolders || receiver.Bodies != 503 || await cache.ReadBodyAsync(large, 1) is null)
            throw new InvalidOperationException("Published older offline batch failed.");
        using (var completed = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, OfflineDownloadPolicy.HeadersOnly, selections))
        {
            completed.SetAccount(account); await completed.CacheLoadTask; await completed.OfflineFolderLoadTask;
            if (completed.CanDownloadOfflineFolders || !completed.HasCompletedOfflineFolderBatch) throw new InvalidOperationException("Published durable offline completion was lost.");
        }
        if (!await model.RestartOfflineFolderBatchesAsync()) throw new InvalidOperationException("Published offline restart control failed.");
        await model.DownloadOfflineFoldersAsync(); if (receiver.Bodies != 503 || !model.HasOfflineFolderContinuation) throw new InvalidOperationException("Published restart refetched saved bodies.");
        await cache.SaveSummariesAsync(new(account.Id, "Large", 8), [new() { Key = new(account.Id, "Large", 8, 1), Subject = "New epoch", Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
        await model.DownloadOfflineFoldersAsync();
        if (receiver.Bodies != 503 || model.ListProblem is null || !model.HasOfflineFolderContinuation) throw new InvalidOperationException("Published offline epoch guard failed.");
        await model.RestartOfflineFolderBatchesAsync(); await model.DownloadOfflineFoldersAsync();
        if (receiver.Bodies != 504 || model.ListProblem is not null || model.CanDownloadOfflineFolders) throw new InvalidOperationException("Published offline epoch restart failed.");
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var row in All(child)) yield return row; }
    private sealed class Receiver : IMailFolderReceiver
    {
        public int Bodies;
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) { Bodies++; return Task.FromResult(new MailMessageBody(key, "Saved " + key.MailboxId)); }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No connection test in offline fixture.");
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No discovery in offline fixture.");
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string folder, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No header receive in offline fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No header receive in offline fixture.");
    }
}
