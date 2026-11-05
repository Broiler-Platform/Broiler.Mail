using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class AccountNavigationSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath);
        var store = new JsonAccountNavigationStore(Path.Combine(directory, "account-navigation.json"));
        var other = account with { Id = AccountId.New(), EmailAddress = "other@example.test" };
        foreach (var owner in new[] { account, other })
            foreach (string folder in new[] { "INBOX", "Archive" })
                await cache.SaveSummariesAsync(new(owner.Id, folder, 7), [new() { Key = new(owner.Id, folder, 7, 1), Subject = "Navigation fixture", Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
        await store.SaveAsync(new(account.Id, "Archive", ["Archive", "INBOX"]));
        await store.SaveAsync(new(other.Id, "INBOX", ["INBOX"]));
        var receiver = new Receiver(); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache, navigation: store);
        model.SetAccount(account); await model.CacheLoadTask;
        if (model.SelectedMailboxId != "Archive" || model.Messages.Count != 1 || model.PinnedFolders.Count != 2 || model.Body is not null) throw new InvalidOperationException("Published account navigation restore failed.");
        using (var view = new InboxView(model).CreateContent())
            if (!All(view).OfType<FormSection>().Any(section => section.Content.AccessibleName == "Account folder navigation controls")) throw new InvalidOperationException("Published account navigation controls missing.");
        model.SetAccount(other with { IsEnabled = false }); await model.CacheLoadTask;
        if (model.SelectedMailboxId != "INBOX" || model.PinnedFolders.Count != 1 || model.CanReceive) throw new InvalidOperationException("Published account navigation crossed accounts.");
        model.SetAccount(account); await model.CacheLoadTask;
        await model.TogglePinnedFolderAsync(); if (model.PinnedFolders.Contains("Archive")) throw new InvalidOperationException("Published local folder unpin failed.");
        var storage = new ProfileStorageService(profile, cache); var cleared = await storage.ClearDownloadedMailAsync(directory + "-backup");
        string restored = await storage.RestoreBackupAsync(cleared.BackupDirectory, directory + "-restored");
        var restoredPreferences = await new JsonAccountNavigationStore(Path.Combine(restored, "account-navigation.json")).LoadAsync(account.Id);
        if (restoredPreferences.StartMailboxId != "Archive" || restoredPreferences.PinnedMailboxIds.Single() != "INBOX" ||
            (await store.LoadAsync(account.Id)).StartMailboxId != "Archive") throw new InvalidOperationException("Published navigation backup/cleanup changed preferences.");
        await model.ResetAccountNavigationAsync(); await model.ReloadAccountNavigationAsync();
        if (model.SelectedMailboxId != "INBOX" || model.PinnedFolders.Count != 0 || receiver.Calls != 0) throw new InvalidOperationException("Published navigation reset caused network activity or retained preferences.");
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }
    private sealed class Receiver : IMailFolderReceiver
    {
        public int Calls;
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) { Calls++; throw new InvalidOperationException("No automatic body fetch."); }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) { Calls++; throw new InvalidOperationException(); }
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) { Calls++; throw new InvalidOperationException(); }
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string folder, int count, MailInboxCursor? older = null, CancellationToken token = default) { Calls++; throw new InvalidOperationException("No automatic receive."); }
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => GetMailboxAsync(account, "INBOX", count, older, token);
    }
}
