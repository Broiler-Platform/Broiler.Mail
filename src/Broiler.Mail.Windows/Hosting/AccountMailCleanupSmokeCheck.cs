using Broiler.Mail.Application;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Windows.Hosting;

internal static class AccountMailCleanupSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile first)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache);
        var second = first with { Id = AccountId.New(), EmailAddress = "second@example.test" }; var accounts = new JsonAccountCollectionStore(directory);
        await accounts.SaveAndSelectAsync(first); await accounts.SaveAsync(second);
        var folders = new JsonOfflineFolderStore(Path.Combine(directory, "offline-folders.json"));
        foreach (var account in new[] { first, second })
        {
            var mailbox = new MailboxKey(account.Id, "INBOX", 7); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
            await cache.SaveSummariesAsync(mailbox, [new() { Key = key, Subject = "Cleanup fixture", Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
            await cache.SaveBodyAsync(mailbox, new(key, account.EmailAddress));
            await folders.SaveAsync(account.Id, ["INBOX"]); await folders.SaveProgressAsync(account.Id, ["INBOX"], []);
        }
        await cache.RebuildSearchIndexAsync(); var credentials = new Credentials(); var firstKey = CredentialKey.For(first, MailProtocol.Imap); await credentials.WriteAsync(firstKey, "synthetic retained password");
        using var application = new MailApplication(accounts, new JsonSettingsStore(profile.SettingsPath), new Receiver(), new Sender(), credentials,
            drafts: new JsonDraftStore(profile.DraftsPath), cache: cache, storage: storage);
        await application.InitializeAsync(); var model = application.CreateViewModel();
        try
        {
            await model.Inbox.CacheLoadTask;
            if (!model.Composer.StartNew()) throw new InvalidOperationException("Published cleanup draft fixture could not start.");
            model.Composer.Edit("recipient@example.test", "", "", "Keep unsent draft", "Retained local text"); var draft = model.Composer.DraftId;
            await model.Accounts!.InspectLocalMailAsync();
            if (model.Accounts.LocalMailInfo?.HeaderCount != 1 || !model.Accounts.RequestLocalMailCleanup()) throw new InvalidOperationException("Published account cleanup inspect/confirmation failed.");
            await model.Accounts.ConfirmLocalMailCleanupAsync(); await model.Inbox.CacheLoadTask;
            if (model.Inbox.Messages.Count != 0 || model.CurrentAccount?.Id != first.Id || model.Accounts.Profiles.Count != 2 || model.Composer.DraftId != draft ||
                model.Composer.PlainText != "Retained local text" || await credentials.ReadAsync(firstKey) != "synthetic retained password" || credentials.Deletions != 0)
                throw new InvalidOperationException("Published account cleanup changed retained state.");
            if (await cache.ReadBodyAsync(new(first.Id, "INBOX", 7), 1) is not null || (await cache.ReadBodyAsync(new(second.Id, "INBOX", 7), 1))?.PlainText != second.EmailAddress ||
                await folders.LoadProgressAsync(first.Id, ["INBOX"]) is not null || (await folders.LoadProgressAsync(second.Id, ["INBOX"])) is not { Count: 0 } ||
                !(await cache.GetSearchIndexInfoAsync()).Ready || (await cache.GetSearchIndexInfoAsync()).Documents != 1)
                throw new InvalidOperationException("Published cleanup crossed account/cache/index boundaries.");
            string backup = model.Accounts.LastLocalMailCleanupBackup ?? throw new InvalidOperationException("Published cleanup recovery backup missing.");
            string restored = await storage.RestoreBackupAsync(backup, directory + "-restored");
            if ((await new SqliteMailCache(Path.Combine(restored, "mail-cache.db")).ReadBodyAsync(new(first.Id, "INBOX", 7), 1))?.PlainText != first.EmailAddress ||
                (await new JsonDraftStore(Path.Combine(restored, "drafts.json")).LoadAsync()).Draft?.Draft.PlainText != "Retained local text")
                throw new InvalidOperationException("Published account cleanup recovery lost mail or draft.");
        }
        finally { model.Inbox.Dispose(); await model.Inbox.CacheLoadTask; model.Composer.Dispose(); }
    }
    private sealed class Receiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in cleanup fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No receive in cleanup fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No body fetch in cleanup fixture.");
    }
    private sealed class Sender : IMailSender
    { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default) => throw new InvalidOperationException("No sending in cleanup fixture."); }
    private sealed class Credentials : ICredentialStore
    {
        private readonly Dictionary<CredentialKey, string> _values = []; public int Deletions;
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) { _values[key] = secret; return Task.CompletedTask; }
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) { Deletions++; throw new InvalidOperationException("Cleanup must not delete credentials."); }
    }
}
