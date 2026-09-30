// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   13
// Annotated:        13/13
// Exempt:           7
// Human-reviewed:   0/13
// IP risk:          Low
// Security risk:    Low
// Criteria:         5/0
// Resource impact:  2/10 max
// Unverified:       13
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Windows;

/// <summary>Explicit, in-memory UI preview. Never reads configuration, credentials, or the network.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=2673CB
// Broiler-Falsified-If: demo mode opens a network connection, reads the Windows credential store, or reads or writes a configuration file
// Broiler-Human:        PENDING
internal static class DemoApplication
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=C94815
    // Broiler-Falsified-If: the returned application uses a receiver, sender or store other than the in-memory demo implementations
    // Broiler-Human:        PENDING
    public static MailApplication Create()
    {
        var store = new DemoStore();
        return new(store, store, new DemoReceiver(), new DemoSender(), store);
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=C2528C
    // Broiler-Human:        PENDING
    private sealed class DemoSender : IMailSender
    {
        public bool IsAvailable => false;
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=1C0B22
        // Broiler-Falsified-If: a demo send returns Accepted or Unknown, so a draft is marked sent although nothing was submitted
        // Broiler-Human:        PENDING
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SendResult(SubmissionStatus.Rejected, "Demo mode never sends mail."));
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=23AE4F
    // Broiler-Human:        PENDING
    private sealed class DemoStore : IAccountStore, ISettingsStore, ICredentialStore
    {
        private AccountProfile _profile = new()
        {
            Id = AccountId.New(), DisplayName = "Demo inbox", EmailAddress = "reader@example.test",
            IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = "reader" },
        };
        private ApplicationSettings _settings = new();
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=BC503C
        // Broiler-Human:        PENDING
        public Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountProfile>>([_profile]);
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=3A9CDB
        // Broiler-Human:        PENDING
        public Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default) { _profile = profile; return Task.CompletedTask; }
        public Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Account removal is not available in version 1.");
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=45FDAE
        // Broiler-Human:        PENDING
        Task<ApplicationSettings> ISettingsStore.LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=E06FD6
        // Broiler-Human:        PENDING
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) { _settings = settings; return Task.CompletedTask; }
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=15E384
        // Broiler-Falsified-If: a credential lookup in demo mode returns a non-null secret
        // Broiler-Human:        PENDING
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Demo mode does not store passwords. Restart without --demo to configure an account.");
        public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Demo mode does not access saved passwords.");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=73E97D
    // Broiler-Human:        PENDING
    private sealed class DemoReceiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => throw new MailConnectionException("Demo mode does not connect to a server. Restart without --demo to test an account.");

        // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=A77D43
        // Broiler-Falsified-If: following the returned cursor repeats or skips a synthetic message
        // Broiler-Human:        PENDING
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

        // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=0A215B
        // Broiler-Human:        PENDING
        public async Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(300, cancellationToken);
            var composition = new MailCompositionSource
            {
                From = ["hello@example.test"], To = [account.EmailAddress],
                Subject = message.Uid == 55 ? "Welcome to Broiler.Mail" : message.Uid == 54 ? "HTML-only mail — text preview" : $"Sample message {message.Uid}",
                MessageId = $"demo-{message.Uid}@example.test",
            };
            if (message.Uid == 54)
            {
                var embedded = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase)
                {
                    ["demo-logo@example.test"] = new MailEmbeddedImage(
                        "demo-logo@example.test",
                        "image/png",
                        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="))
                };
                return new(message, "Hello & welcome!\n\nThis sample demonstrates the labeled text fallback for HTML-only mail.\n\nSelect Open HTML preview to view formatted HTML.",
                    "<h1>Hello &amp; welcome!</h1><p>This sample demonstrates the <b>HTML reading mode</b> with isolated rendering.</p><p><img src=\"cid:demo-logo@example.test\" alt=\"Demo dot\"></p><p>Links like <a href=\"https://example.test\">example.test</a> open in your default browser.</p>")
                {
                    IsHtmlFallback = true,
                    Composition = composition,
                    EmbeddedImages = embedded,
                };
            }
            return new(message, "Welcome to Broiler.Mail version 1.\n\nThis is a synthetic message. Demo mode never accesses your account, saved password, or network.\n\nUse Receive mail, Load older, and select a message.\n\nKeyboard: Tab / Shift+Tab moves focus, Ctrl+1/2/3 switches tabs, F5 receives, and Escape cancels.\n\n" +
                string.Join("\n\n", Enumerable.Range(1, 35).Select(index => $"Paragraph {index}: The reading pane wraps and scrolls. Grüße, café, and literal ampersands & remain readable."))) { Composition = composition };
        }
    }
}
