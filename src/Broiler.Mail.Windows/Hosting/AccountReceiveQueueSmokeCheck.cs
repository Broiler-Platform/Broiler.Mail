using System.Collections.Concurrent;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;

namespace Broiler.Mail.Windows.Hosting;

internal static class AccountReceiveQueueSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile original)
    {
        using var profile = MailProfile.Open(directory); var store = new JsonAccountCollectionStore(directory);
        var second = original with { Id = AccountId.New(), DisplayName = "Second queued account", EmailAddress = "second@example.test", SenderAliases = "", DefaultSenderAddress = "", SenderIdentities = null };
        await store.SaveAndSelectAsync(original); await store.SaveAndSelectAsync(second); await store.SelectAsync(original.Id);
        var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache); var receiver = new Receiver(original.Id); var dispatcher = new Dispatcher();
        using var application = new MailApplication(store, new JsonSettingsStore(profile.SettingsPath), receiver, new Sender(), new Credentials(), new JsonDraftStore(Path.Combine(directory, "drafts.json")), cache: cache, storage: storage);
        await application.InitializeAsync(); var model = application.CreateViewModel(dispatcher); await model.Inbox.CacheLoadTask; dispatcher.Drain();
        try
        {
            var queue = model.AccountReceives ?? throw new InvalidOperationException("Published account queue is unavailable.");
            using var content = new AccountReceiveQueueView(queue, () => model.Accounts!.SelectedId).CreateContent();
            if (receiver.FirstCalls != 0 || receiver.SecondCalls != 0 || !model.Composer.StartNew()) throw new InvalidOperationException("Published account queue connected automatically or draft setup failed.");
            model.Composer.Edit("to@example.test", "", "", "Pinned queue fixture", "Unsent queue fixture"); await Pump(model.Composer.SaveAsync(), dispatcher); var draftId = model.Composer.DraftId;
            if (queue.QueueAllEnabled() != 2) throw new InvalidOperationException("Published enabled-account queue request failed.");
            await receiver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Wait(() => queue.Jobs.Single(job => job.AccountId == second.Id).State == AccountReceiveState.Completed, dispatcher);
            await Pump(model.Accounts!.SwitchAsync(second.Id), dispatcher); await model.Inbox.CacheLoadTask; dispatcher.Drain();
            if (model.Composer.DraftId != draftId || model.Composer.DraftAccountId != original.Id || model.Accounts.SelectedId != second.Id || !queue.HasWork || queue.QueueAccount(original.Id)) throw new InvalidOperationException("Published account queue changed sender/selection or duplicated outstanding work.");
            var settings = model.Settings.Storage!; settings.BackupDestination = directory + "-blocked-backup"; await Pump(settings.BackupAsync(), dispatcher);
            if (Directory.Exists(settings.BackupDestination) || !settings.Status.Contains("account receives")) throw new InvalidOperationException("Published maintenance started during account receive.");
            queue.CancelAll(); if (!queue.HasWork) throw new InvalidOperationException("Published queue abandoned an unacknowledged cancellation."); receiver.Release.SetResult(); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); dispatcher.Drain();
            if (await cache.GetLatestMailboxAsync(original.Id, "INBOX") is not null || queue.Jobs.Single(job => job.AccountId == original.Id).State != AccountReceiveState.Canceled || receiver.SecondCalls != 1) throw new InvalidOperationException("Published canceled account wrote mail or affected the other account.");
            queue.QueueAccount(original.Id); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); dispatcher.Drain();
            if (queue.Jobs.Single(job => job.AccountId == original.Id).State != AccountReceiveState.Failed || receiver.SecondCalls != 1) throw new InvalidOperationException("Published independent account failure was lost or retried another account.");
            queue.QueueAccount(original.Id); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); dispatcher.Drain();
            foreach (var account in new[] { original, second })
            {
                var saved = await new SqliteMailCache(profile.CachePath).GetLatestMailboxAsync(account.Id, "INBOX");
                if (saved is null || (await cache.ReadSummariesAsync(saved.Key, 10)).Single().Key.AccountId != account.Id) throw new InvalidOperationException("Published queue mixed account cache identities.");
            }
            if (model.Composer.DraftAccountId != original.Id || model.Accounts.SelectedId != second.Id || receiver.FirstCalls != 3 || receiver.SecondCalls != 1) throw new InvalidOperationException("Published explicit account retry changed the reader/draft or performed automatic work.");
            settings.BackupDestination = directory + "-finished-backup"; await Pump(settings.BackupAsync(), dispatcher); if (!Directory.Exists(settings.BackupDestination)) throw new InvalidOperationException("Published queue did not release maintenance after completion.");
        }
        finally { model.AccountReceives?.Dispose(); model.Inbox.Dispose(); model.Composer.Dispose(); model.Settings.Storage?.Dispose(); dispatcher.Drain(); }
    }
    private static async Task Pump(Task task, Dispatcher dispatcher)
    { var deadline = task.WaitAsync(TimeSpan.FromSeconds(10)); while (!deadline.IsCompleted) { dispatcher.Drain(); await Task.WhenAny(deadline, Task.Delay(1)); } await deadline; dispatcher.Drain(); }
    private static async Task Wait(Func<bool> settled, Dispatcher dispatcher)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!settled()) { dispatcher.Drain(); if (DateTime.UtcNow >= end) throw new TimeoutException("Published account queue did not settle."); await Task.Delay(1); } dispatcher.Drain(); }
    private sealed class Dispatcher : IUiDispatcher
    { private readonly ConcurrentQueue<Action> _queue = new(); public bool CheckAccess() => true; public void Post(Action action) => _queue.Enqueue(action); public void Drain() { while (_queue.TryDequeue(out var action)) action(); } }
    private sealed class Receiver(AccountId first) : IMailReceiver
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FirstCalls, SecondCalls;
        public async Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        {
            if (count != 50 || older is not null) throw new InvalidOperationException("Published queue requested more than one bounded page.");
            if (account.Id == first) { int attempt = Interlocked.Increment(ref FirstCalls); if (attempt == 1) { Entered.TrySetResult(); await Release.Task; } if (attempt == 2) throw new MailConnectionException("Synthetic account authentication failure."); } else Interlocked.Increment(ref SecondCalls);
            return new([new() { Key = new(account.Id, "INBOX", 7, 1), Sender = account.EmailAddress, Subject = "Queued fixture header" }], null) { Mailbox = new(account.Id, "INBOX", 7) };
        }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No connection test in queue fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No body download in queue fixture.");
    }
    private sealed class Sender : IMailSender { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default) => throw new InvalidOperationException("Queue fixture cannot send mail."); }
    private sealed class Credentials : ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) => throw new InvalidOperationException("Queue fixture cannot change credentials.");
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("Queue fixture cannot remove credentials.");
    }
}
