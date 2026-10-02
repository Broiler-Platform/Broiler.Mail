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

using System.Globalization;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.Views;
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
    private static readonly DateTimeOffset Newest = new(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly AccountId DemoAccount = new(new Guid("6d1f3a52-4b8e-4c27-9a3e-1f2b8c7d5e01"));

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=C94815
    // Broiler-Falsified-If: the returned application uses a receiver, sender or store other than the in-memory demo implementations
    // Broiler-Human:        PENDING
    public static MailApplication Create(DemoOptions? options = null)
    {
        options ??= new(DemoScenario.Inbox, Interactive: true);
        var store = new DemoStore(options);
        return new(store, store, new DemoReceiver(options.Scenario, options.Interactive), new DemoSender(), store, CreateDrafts(options.Scenario, store.Profile));
    }

    /// <summary>Dates relative to a fixed moment, zone, and culture, so gallery captures are reproducible.</summary>
    public static MessageDateFormatter CreateDateFormatter() => new(FixedClock.Instance, CultureInfo.GetCultureInfo("en-US"));

    private static MemoryDraftStore CreateDrafts(DemoScenario scenario, AccountProfile profile)
    {
        var drafts = new MemoryDraftStore();
        DraftSnapshot? snapshot = scenario switch
        {
            DemoScenario.LargeDraft => new DraftSnapshot
            {
                Draft = new MailDraft
                {
                    Id = new Guid("0b7c2f4e-8d1a-4e5b-9c3f-2a6d8e1b4c70"), AccountId = profile.Id, FromAddress = profile.EmailAddress,
                    Subject = "Quarterly planning notes — draft for review before the Friday meeting",
                    PlainText = string.Join("\n\n", Enumerable.Range(1, 60).Select(index =>
                        $"Section {index}: A recovered draft keeps its text and recipients. Grüße, naïve café, 東京, and & remain intact.")),
                },
                ToText = "Team <team@example.test>, planning-lead@example.test",
                CcText = "Reviewer One <reviewer.one@example.test>",
                BccText = "archive@example.test",
                State = DraftSubmissionState.Editing,
            },
            DemoScenario.SendUnknown => new DraftSnapshot
            {
                Draft = new MailDraft
                {
                    Id = new Guid("5e9a1c3b-7f2d-4a8e-b6c1-9d4f2e7a3b80"), AccountId = profile.Id, FromAddress = profile.EmailAddress,
                    To = ["team@example.test"], Subject = "Status update", SubmissionDate = Newest,
                    PlainText = "The server connection ended before it confirmed acceptance.",
                },
                ToText = "team@example.test", CcText = "", BccText = "",
                State = DraftSubmissionState.Unknown,
            },
            _ => null,
        };
        if (snapshot is not null) drafts.SaveAsync(0, snapshot).GetAwaiter().GetResult();
        return drafts;
    }

    private sealed class FixedClock : TimeProvider
    {
        public static FixedClock Instance { get; } = new();
        private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Broiler demo", TimeSpan.FromHours(2), "Broiler demo", "Broiler demo");
        public override DateTimeOffset GetUtcNow() => Newest.AddHours(2).ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => Zone;
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
    private sealed class DemoStore(DemoOptions options) : IAccountStore, ISettingsStore, ICredentialStore
    {
        private AccountProfile _profile = new()
        {
            Id = DemoAccount, DisplayName = "Demo inbox", EmailAddress = "reader@example.test",
            IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = "reader" },
        };
        private ApplicationSettings _settings = new() { Theme = options.Theme, WindowWidth = options.Width, WindowHeight = options.Height };
        public AccountProfile Profile => _profile;
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
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            if (options.Scenario == DemoScenario.SaveError) throw new IOException("The demo settings file is read-only.");
            _settings = settings;
            return Task.CompletedTask;
        }
        // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=15E384
        // Broiler-Falsified-If: a credential lookup in demo mode returns a non-null secret
        // Broiler-Human:        PENDING
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Demo mode does not store passwords. Restart without --demo to configure an account.");
        public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Demo mode does not access saved passwords.");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=73E97D
    // Broiler-Human:        PENDING
    private sealed class DemoReceiver(DemoScenario scenario, bool interactive) : IMailReceiver
    {
        private const string LongSender = "Maximilian Alexander von Langenstein-Habsburg <maximilian.alexander.von.langenstein-habsburg.office@subdomain.example.test>";
        private const string LongSubject = "Re: Fwd: Agenda, travel arrangements, accessibility requirements, and the revised budget spreadsheet for the cross-team planning workshop in Zürich";
        private int _receives;

        private int Total => scenario switch { DemoScenario.Empty => 0, DemoScenario.LargeInbox => 500, _ => 55 };
        // The interactive demo shows its busy states; gallery fixtures settle immediately.
        private TimeSpan Latency(int milliseconds) => interactive ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;
        private string Subject(uint uid) => uid == 55 ? (scenario == DemoScenario.LongMessage ? LongSubject : "Welcome to Broiler.Mail")
            : uid == 54 ? "HTML-only mail — text preview" : $"Sample message {uid}";

        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => throw new MailConnectionException("Demo mode does not connect to a server. Restart without --demo to test an account.");

        // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=A77D43
        // Broiler-Falsified-If: following the returned cursor repeats or skips a synthetic message
        // Broiler-Human:        PENDING
        public async Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Latency(500), cancellationToken);
            // The first newest-page fetch succeeds so the failed refresh is shown over retained content.
            if (scenario == DemoScenario.ReceiveError && older is null && Interlocked.Increment(ref _receives) > 1)
                throw new MailConnectionException("The demo server did not respond. Check the connection, then retry.");
            int total = Total;
            if (total == 0) return new([], null);
            int end = older?.NextIndex ?? total - 1;
            int start = Math.Max(0, end - maximumCount + 1);
            var messages = Enumerable.Range(start + 1, end - start + 1).Reverse().Select(uid => new MailMessageSummary
            {
                Key = new(account.Id, "INBOX", 1, (uint)uid),
                Sender = uid == 55 && scenario == DemoScenario.LongMessage ? LongSender : "Broiler team <hello@example.test>",
                Subject = Subject((uint)uid),
                // Spread a large inbox over several days so rows show times, dates, and older years.
                ReceivedAt = Newest.AddMinutes((uid - total) * (total > 55 ? 180 : 1)),
                IsRead = uid % 3 == 0,
            }).ToArray();
            return new(messages, start == 0 ? null : new(account.Id, 1, (uint)total + 1, total, start - 1));
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=0A215B
        // Broiler-Human:        PENDING
        public async Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Latency(300), cancellationToken);
            if (scenario == DemoScenario.BodyError && message.Uid == 55)
                throw new MailConnectionException("The demo server closed the connection while sending this message.");
            var composition = new MailCompositionSource
            {
                From = ["hello@example.test"], To = [account.EmailAddress],
                Subject = Subject(message.Uid),
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
            if (scenario == DemoScenario.LongMessage && message.Uid == 55)
            {
                return new(message, "Long lines, unbroken addresses, and mixed scripts must wrap without clipping.\n\n" +
                    "Unbroken: https://example.test/a/very/long/path/without/any/natural/break/opportunities/whatsoever/index.html\n\n" +
                    "Deutsch: Größenänderung und Donaudampfschifffahrtsgesellschaftskapitän.\n" +
                    "日本語: 長いメッセージの折り返しを確認します。\n" +
                    "العربية: هذا نص تجريبي من اليمين إلى اليسار.\n" +
                    "עברית: טקסט לדוגמה מימין לשמאל.\n" +
                    "Surrogate pairs: 📬 ✉️ 👩🏽‍💻\n\n" +
                    string.Join("\n\n", Enumerable.Range(1, 20).Select(index => $"Paragraph {index}: {LongSubject}.")))
                { Composition = composition };
            }
            return new(message, "Welcome to Broiler.Mail version 1.\n\nThis is a synthetic message. Demo mode never accesses your account, saved password, or network.\n\nUse Receive mail, Load older, and select a message.\n\nKeyboard: Tab / Shift+Tab moves focus, Ctrl+1/2/3 switches tabs, F5 receives, and Escape cancels.\n\n" +
                string.Join("\n\n", Enumerable.Range(1, 35).Select(index => $"Paragraph {index}: The reading pane wraps and scrolls. Grüße, café, and literal ampersands & remain readable."))) { Composition = composition };
        }
    }
}
