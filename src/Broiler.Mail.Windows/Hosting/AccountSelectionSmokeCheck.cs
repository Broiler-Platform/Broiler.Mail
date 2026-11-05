using Broiler.Mail.Application;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Synthetic account startup/switching and draft pinning; no protocol or credential access.</summary>
internal static class AccountSelectionSmokeCheck
{
    internal static async Task<string> RunRemovalAsync(IAccountCollectionStore store, IProfileStorage storage, IMailCache cache,
        ICredentialStore credentials, AccountId target)
    {
        var original = (await store.LoadStateAsync()).Accounts.Single(row => row.Id == target);
        await store.SaveAndSelectAsync(original with { SenderIdentities = [new() { Address = original.EmailAddress, Signature = "Native persisted sender override" }] });
        using var application = new MailApplication(store, new JsonSettingsStore(Path.Combine(storage.ProfileDirectory, "settings.json")), new Receiver(), new Sender(), credentials,
            drafts: new JsonDraftStore(Path.Combine(storage.ProfileDirectory, "drafts.json")), cache: cache, storage: storage,
            accountRemoval: new AccountRemovalService(store, storage, credentials));
        await application.InitializeAsync(); var model = application.CreateViewModel();
        try
        {
            var journal = new MutationIntentJournal(Path.Combine(storage.ProfileDirectory, "mail-cache.db"));
            var intent = await journal.BeginAsync(new(target, "INBOX", 7, 1), null);
            try
            {
                bool refused = false, backupCreated = false;
                try { await new AccountRemovalService(store, storage, new Credentials()).RemoveAsync(model.CurrentAccount!, true, _ => backupCreated = true); }
                catch (InvalidOperationException) { refused = true; }
                if (!refused || backupCreated || !(await store.LoadAsync()).Any(row => row.Id == target))
                    throw new InvalidOperationException("Published pending-change account removal guard failed.");
            }
            finally { await journal.CompleteAsync(intent); }
            if (model.CurrentAccount?.Id != target || model.Accounts?.RequestRemoval() != true)
                throw new InvalidOperationException("Published account removal confirmation failed.");
            await model.Accounts.ConfirmRemovalAsync(true);
            if (model.Accounts.Profiles.Any(profile => profile.Id == target) || model.Accounts.LastRemovalBackup is not { } backup)
                throw new InvalidOperationException("Published recovery-backed account removal failed.");
            return backup;
        }
        finally { model.Inbox.Dispose(); await model.Inbox.CacheLoadTask; model.Composer.Dispose(); }
    }

    internal static async Task RunAsync(string directory)
    {
        var first = Profile("Personal", "a@example.test"); var second = Profile("Work", "b@example.test");
        var store = new JsonAccountCollectionStore(directory);
        await store.SaveAsync(first); await store.SaveAndSelectAsync(second);
        var recipients = new JsonRecipientBook(Path.Combine(directory, "recipients.json"));
        await recipients.SaveAsync(new(first.Id, "personal-recipient@example.test", "Personal recipient"));
        await recipients.SaveAsync(new(second.Id, "work-recipient@example.test", "Work recipient"));
        using var application = new MailApplication(store, new JsonSettingsStore(Path.Combine(directory, "settings.json")), new Receiver(), new Sender(), new Credentials(), recipientBook: recipients);
        await application.InitializeAsync(); var model = application.CreateViewModel();
        try
        {
            if (model.CurrentAccount != second || model.Accounts is null) throw new InvalidOperationException("Selected account startup failed.");
            await model.Accounts.SwitchAsync(first.Id);
            if (model.CurrentAccount != first || !model.Composer.StartNew()) throw new InvalidOperationException("Account switch failed.");
            await model.Composer.RecipientBook!.LoadTask;
            if (model.Composer.RecipientBook.Recipients.Single().AccountId != first.Id)
                throw new InvalidOperationException("Published saved recipient account scope failed.");
            model.Composer.Edit("to@example.test", "", "", "Synthetic draft", "Retained text");
            if (!model.Composer.CanSend) throw new InvalidOperationException("Matching draft sender should be usable.");
            await model.Accounts.SwitchAsync(second.Id);
            if (model.CurrentAccount != second || model.Composer.FromAddress != first.EmailAddress || model.Composer.CanSend)
                throw new InvalidOperationException("Account switching changed draft sender authority.");
            await model.Composer.RecipientBook.LoadTask;
            if (model.Composer.RecipientBook.Recipients.Single().AccountId != first.Id)
                throw new InvalidOperationException("Published recipient book switched away from pinned draft account.");
            await model.Accounts.SetEnabledAsync(false);
            if (model.CurrentAccount?.IsEnabled != false || model.Inbox.CanReceive || model.Composer.FromAddress != first.EmailAddress)
                throw new InvalidOperationException("Disabling account changed retained data or receive authority.");
            await model.Accounts.SetEnabledAsync(true);
            await model.Accounts.MoveSelectedAsync(-1);
            var managedState = await store.LoadStateAsync();
            if (managedState.SelectedAccountId != second.Id || managedState.Accounts[0].Id != second.Id || !model.Inbox.CanReceive)
                throw new InvalidOperationException("Account enable/order persistence failed.");
            using var reopened = new MailApplication(store, new JsonSettingsStore(Path.Combine(directory, "settings.json")), new Receiver(), new Sender(), new Credentials());
            await reopened.InitializeAsync();
            if (reopened.LoadedAccount != second) throw new InvalidOperationException("Selected account restart failed.");
            using var shell = new MailShellView(model);
            if (shell.AccountSelectionContent is null) throw new InvalidOperationException("Account selector missing.");
            ShellSmokeCheck.Run(shell);
        }
        finally { model.Inbox.Dispose(); model.Composer.Dispose(); }
    }
    private static AccountProfile Profile(string name, string email) => new()
    {
        Id = AccountId.New(), DisplayName = name, EmailAddress = email,
        IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = email },
        OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = email, Security = TransportSecurity.StartTls },
    };
    private sealed class Receiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network in fixture");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken token = default) => throw new InvalidOperationException("No network in fixture");
    }
    private sealed class Sender : IMailSender
    { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default) => throw new InvalidOperationException("No sending in fixture"); }
    private sealed class Credentials : ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) => throw new InvalidOperationException("No credentials in fixture");
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("No credentials in fixture");
    }
}
