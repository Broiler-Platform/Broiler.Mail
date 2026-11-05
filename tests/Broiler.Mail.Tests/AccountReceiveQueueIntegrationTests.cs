using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class AccountReceiveQueueIntegrationTests
{
    [Fact]
    public async Task SwitchingKeepsJobsIndependentAndDraftPinnedWhileSettingsChangesCancelOnlyTheirAccount()
    {
        using var dir = new TestDirectory(); using var profile = MailProfile.Open(dir.Root); var store = new JsonAccountCollectionStore(dir.Root);
        var first = TestDirectory.Profile(); var second = TestDirectory.Profile() with { DisplayName = "Second", EmailAddress = "second@example.test" };
        await store.SaveAndSelectAsync(first); await store.SaveAndSelectAsync(second); await store.SelectAsync(first.Id);
        var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache); var credentials = new TestCredentialStore(); var receiver = new AccountReceiveQueueTests.Receiver(); var dispatcher = new TestQueueDispatcher();
        using var application = new MailApplication(store, new JsonSettingsStore(profile.SettingsPath), receiver, new NoSender(), credentials, new JsonDraftStore(dir.File("drafts.json")), cache: cache, storage: storage, accountRemoval: new AccountRemovalService(store, storage, credentials));
        await application.InitializeAsync(); var model = application.CreateViewModel(dispatcher); await model.Inbox.CacheLoadTask; dispatcher.Drain();
        try
        {
            Assert.True(model.Composer.StartNew()); model.Composer.Edit("to@example.test", "", "", "Pinned draft", "Unsent text"); await Pump(model.Composer.SaveAsync(), dispatcher); var draftId = model.Composer.DraftId;
            var entered = AccountReceiveQueueTests.NewSource(); var release = AccountReceiveQueueTests.NewSource(); int calls = 0;
            receiver.Get = async (account, _) => { if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult(); await release.Task; return AccountReceiveQueueTests.Page(account); };
            var queue = Assert.IsType<AccountReceiveQueueViewModel>(model.AccountReceives); Assert.Equal(2, queue.QueueAllEnabled()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); dispatcher.Drain();
            Assert.True(model.Accounts!.CanSwitch); await Pump(model.Accounts.SwitchAsync(second.Id), dispatcher); await model.Inbox.CacheLoadTask; dispatcher.Drain();
            Assert.Equal(second.Id, model.Accounts.SelectedId); Assert.Equal(draftId, model.Composer.DraftId); Assert.Equal(first.Id, model.Composer.DraftAccountId); Assert.Equal("Unsent text", model.Composer.PlainText);
            Assert.False(model.Accounts.CanRemove); Assert.False(model.Accounts.CanRequestLocalMailCleanup);
            var settings = model.Settings.Storage!; settings.BackupDestination = dir.File("blocked-backup"); await Pump(settings.BackupAsync(), dispatcher); Assert.False(Directory.Exists(settings.BackupDestination)); Assert.Contains("account receives", settings.Status);
            model.Account.DisplayName = "Changed second configuration"; await Pump(model.Account.SaveAsync(), dispatcher); dispatcher.Drain();
            Assert.Equal(AccountReceiveState.Canceling, queue.Jobs.Single(job => job.AccountId == second.Id).State); Assert.Equal(AccountReceiveState.Receiving, queue.Jobs.Single(job => job.AccountId == first.Id).State);
            queue.CancelAll(); Assert.True(queue.HasWork); await Pump(settings.BackupAsync(), dispatcher); Assert.False(Directory.Exists(settings.BackupDestination));
            release.SetResult(); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); dispatcher.Drain(); Assert.Null(await cache.GetLatestMailboxAsync(first.Id, "INBOX")); Assert.Null(await cache.GetLatestMailboxAsync(second.Id, "INBOX"));
            Assert.Equal(first.Id, model.Composer.DraftAccountId); Assert.Equal(second.Id, (await store.LoadStateAsync()).SelectedAccountId);
            settings.BackupDestination = dir.File("finished-backup"); await Pump(settings.BackupAsync(), dispatcher); Assert.True(Directory.Exists(settings.BackupDestination)); Assert.True(model.Accounts.CanRemove);
        }
        finally { model.AccountReceives?.Dispose(); model.Inbox.Dispose(); model.Composer.Dispose(); model.Settings.Storage?.Dispose(); }
    }
    [Fact]
    public async Task PendingRemovalPreventsStartingNewJobsAndApplicationDisposalCancelsAnUnhostedModel()
    {
        using var dir = new TestDirectory(); using var profile = MailProfile.Open(dir.Root); var account = TestDirectory.Profile(); var store = new JsonAccountCollectionStore(dir.Root); await store.SaveAndSelectAsync(account);
        var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache); var receiver = new AccountReceiveQueueTests.Receiver(); var credentials = new TestCredentialStore(); var dispatcher = new TestQueueDispatcher();
        using var application = new MailApplication(store, new JsonSettingsStore(profile.SettingsPath), receiver, new NoSender(), credentials, cache: cache, storage: storage, accountRemoval: new AccountRemovalService(store, storage, credentials)); await application.InitializeAsync(); var model = application.CreateViewModel(dispatcher); await model.Inbox.CacheLoadTask; dispatcher.Drain();
        try
        {
            using var activity = new Broiler.Mail.Application.Views.AccountReceiveQueueView(model.AccountReceives!, () => model.Accounts!.SelectedId, model.Accounts).CreateContent();
            static IEnumerable<Broiler.UI.UiElement> All(Broiler.UI.UiElement root) { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
            var queueButton = All(activity).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == "Queue enabled accounts"); Assert.True(queueButton.IsEnabled);
            Assert.True(model.Accounts!.RequestRemoval()); Assert.False(queueButton.IsEnabled); Assert.Equal(0, model.AccountReceives!.QueueAllEnabled()); Assert.Empty(receiver.Started); model.Accounts.CancelRemoval(); Assert.True(queueButton.IsEnabled);
            var entered = AccountReceiveQueueTests.NewSource(); var release = AccountReceiveQueueTests.NewSource(); CancellationToken observed = default;
            receiver.Get = async (profile, token) => { observed = token; entered.TrySetResult(); await release.Task; return AccountReceiveQueueTests.Page(profile); };
            Assert.Equal(1, model.AccountReceives.QueueAllEnabled()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); application.Dispose(); Assert.True(observed.IsCancellationRequested); release.SetResult(); await model.AccountReceives.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); dispatcher.Drain(); Assert.Null(await cache.GetLatestMailboxAsync(account.Id, "INBOX"));
        }
        finally { model.Inbox.Dispose(); model.Composer.Dispose(); model.Settings.Storage?.Dispose(); }
    }
    internal static async Task Pump(Task task, TestQueueDispatcher dispatcher)
    { var timeout = task.WaitAsync(TimeSpan.FromSeconds(10)); while (!timeout.IsCompleted) { dispatcher.Drain(); await Task.WhenAny(timeout, Task.Delay(1)); } await timeout; dispatcher.Drain(); }
    private sealed class NoSender : IMailSender { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default) => throw new InvalidOperationException("Queue cannot send mail."); }
}
