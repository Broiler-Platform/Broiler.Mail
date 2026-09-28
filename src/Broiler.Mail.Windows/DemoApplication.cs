using Broiler.Mail.Application;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Mail;

namespace Broiler.Mail.Windows;

/// <summary>Explicit, in-memory UI preview. Never reads configuration, credentials, or the network.</summary>
internal static class DemoApplication
{
    public static MailApplication Create()
    {
        var store = new DemoStore();
        return new(store, store, new DemoReceiver(), new SmtpMailSender(store), store);
    }

    private sealed class DemoStore : IAccountStore, ISettingsStore, ICredentialStore
    {
        private AccountProfile _profile = new()
        {
            Id = AccountId.New(), DisplayName = "Demo inbox", EmailAddress = "reader@example.test",
            IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = "reader" },
        };
        private ApplicationSettings _settings = new();
        public Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountProfile>>([_profile]);
        public Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default) { _profile = profile; return Task.CompletedTask; }
        public Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Account removal is not available in version 1.");
        Task<ApplicationSettings> ISettingsStore.LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) { _settings = settings; return Task.CompletedTask; }
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Demo mode does not store passwords. Restart without --demo to configure an account.");
        public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Demo mode does not access saved passwords.");
    }

    private sealed class DemoReceiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => throw new MailConnectionException("Demo mode does not connect to a server. Restart without --demo to test an account.");

        public async Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(500, cancellationToken);
            int end = older?.NextIndex ?? 54;
            int start = Math.Max(0, end - maximumCount + 1);
            var messages = Enumerable.Range(start + 1, end - start + 1).Reverse().Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 1, (uint)uid), Sender = "Broiler team <hello@example.test>",
                Subject = uid == 55 ? "Welcome to Broiler.Mail" : uid == 54 ? "HTML-only mail — text preview" : $"Sample message {uid}",
                ReceivedAt = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2)).AddMinutes(uid - 55),
                IsRead = uid % 3 == 0,
            }).ToArray();
            return new(messages, start == 0 ? null : new(account.Id, 1, 56, 55, start - 1));
        }

        public async Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(300, cancellationToken);
            if (message.Uid == 54)
                return new(message, "Hello & welcome!\n\nThis sample demonstrates the labeled text fallback for HTML-only mail.\n\nImages and external resources stay unloaded.") { IsHtmlFallback = true };
            return new(message, "Welcome to Broiler.Mail version 1.\n\nThis is a synthetic message. Demo mode never accesses your account, saved password, or network.\n\nUse Receive mail, Load older, and select a message.\n\nKeyboard: Tab / Shift+Tab moves focus, Ctrl+1/2/3 switches tabs, F5 receives, and Escape cancels.\n\n" +
                string.Join("\n\n", Enumerable.Range(1, 35).Select(index => $"Paragraph {index}: The reading pane wraps and scrolls. Grüße, café, and literal ampersands & remain readable.")));
        }
    }
}
