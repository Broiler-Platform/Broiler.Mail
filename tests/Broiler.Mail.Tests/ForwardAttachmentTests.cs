// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.4c: a forward offers the original's files, and includes them all, or none, only when asked.</summary>
[Collection("UI theme")]
public sealed class ForwardAttachmentTests
{
    private const int MiB = 1024 * 1024;
    private const string Include = "Include original attachments";
    private static readonly byte[] First = Random(200_000, 3);
    private static readonly byte[] Second = Random(60_000, 4);

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed record Part(string Name, string MediaType, byte[] Data);

    /// <summary>A text body followed by each part as a base64 attachment.</summary>
    private static byte[] Original(params Part[] parts)
    {
        var raw = new StringBuilder("From: sender@example.test\r\nTo: test@example.test\r\nSubject: Quarterly files\r\nMessage-ID: <files@example.test>\r\n" +
            "MIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=outer\r\n\r\n--outer\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSee the attached files.\r\n");
        foreach (var part in parts)
            raw.Append("--outer\r\nContent-Type: ").Append(part.MediaType).Append("\r\nContent-Disposition: attachment; filename=\"").Append(part.Name)
                .Append("\"\r\nContent-Transfer-Encoding: base64\r\n\r\n")
                .Append(Convert.ToBase64String(part.Data, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
        raw.Append("--outer--\r\n");
        return Encoding.ASCII.GetBytes(raw.ToString());
    }

    private static byte[] TwoFiles() => Original(new("report.pdf", "application/pdf", First), new("data.csv", "text/csv", Second));

    private static MailMessageKey Key(AccountProfile account, uint uid = 1) => new(account.Id, "INBOX", 7, uid);

    private static MailMessageBody Decoded(AccountProfile account, byte[] original, uint uid = 1) =>
        MessageTextDecoder.DecodeReceived(Key(account, uid), original, CancellationToken.None);

    /// <summary>A body that only lists descriptors, for offers whose bytes are never read.</summary>
    private static MailMessageBody Listed(AccountProfile account, params MailAttachmentDescriptor[] parts) =>
        new(Key(account), "See the attached files.")
        {
            Composition = new() { Subject = "Quarterly files", From = ["sender@example.test"], MessageId = "files@example.test" },
            Attachments = parts,
        };

    [Fact]
    public void ForwardOfMessageWithAttachmentsShowsBanner()
    {
        using var fixture = new Fixture(reader: TestSourceReceiver.Returning(TwoFiles()));
        var body = Decoded(fixture.Account, TwoFiles());
        Assert.Equal(2, body.Attachments!.Count);

        // A reply never offers the original's files.
        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Reply));
        fixture.Render();
        Assert.Null(fixture.Composer.ForwardAttachments);
        Assert.Equal(UiVisibility.Collapsed, fixture.Banner.Visibility);
        fixture.Discard();

        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Forward));
        fixture.Render();
        var offer = Assert.IsType<ComposerViewModel.ForwardAttachmentOffer>(fixture.Composer.ForwardAttachments);
        Assert.Equal(fixture.Composer.DraftId, offer.DraftId);
        Assert.Equal(body.Key, offer.Source);
        Assert.Equal(First.Length + Second.Length, offer.TotalBytes);
        Assert.Equal("Original message has 2 attachments (254 KiB).", offer.Text);
        Assert.True(Shown(fixture.Banner));
        Assert.Equal("Original attachments", fixture.Banner.GetSemanticNode().Name);
        Assert.Contains(fixture.Labels(), text => text == offer.Text);
        Assert.True(fixture.Bar.IncludeButton.IsEnabled);
        Assert.True(Shown(fixture.Bar.IncludeButton));
        Assert.Equal("Hide original attachments notice", fixture.Bar.HideForwardButton.GetSemanticNode().Name);
        Assert.Null(fixture.Composer.ForwardAttachmentsUnavailableReason);
        // Nothing is included by default, and the banner comes before Attach file… in reading order.
        Assert.Empty(fixture.Composer.Attachments);
        Assert.True(fixture.Banner.Bounds.Bottom <= fixture.Button("Attach file…").Bounds.Top + 0.5);
        Assert.Equal(0, fixture.Source!.CopySourceCalls);

        // One attachment reads in the singular; a container part is never offered.
        var single = Listed(fixture.Account, new(3, "slides.pdf", "application/pdf", 2_516_582), new(4, "nested.eml", "multipart/mixed", 4_000));
        var only = new ComposerViewModel.ForwardAttachmentOffer(Guid.NewGuid(), single.Key, [single.Attachments![0]]);
        Assert.Equal("Original message has 1 attachment (2.4 MiB).", only.Text);
        fixture.Discard();
        Assert.True(fixture.Composer.StartFromMessage(single, CompositionKind.Forward));
        Assert.Equal(["slides.pdf"], fixture.Composer.ForwardAttachments!.Parts.Select(part => part.FileName));
        fixture.Discard();
        Assert.True(fixture.Composer.StartFromMessage(Listed(fixture.Account, new MailAttachmentDescriptor(3, "nested.eml", "multipart/mixed", 4_000)), CompositionKind.Forward));
        Assert.Null(fixture.Composer.ForwardAttachments);
        fixture.Discard();
        Assert.True(fixture.Composer.StartFromMessage(Listed(fixture.Account), CompositionKind.Forward));
        Assert.Null(fixture.Composer.ForwardAttachments);
    }

    [Fact]
    public async Task IncludeOriginalAttachmentsAppendsAllPartsOnce()
    {
        using var directory = new TestDirectory(); directory.Create();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using var fixture = new Fixture(reader: TestSourceReceiver.Returning(TwoFiles()), store: store);
        Assert.True(fixture.Composer.StartFromMessage(Decoded(fixture.Account, TwoFiles()), CompositionKind.Forward));
        fixture.Render();
        fixture.Session.SetFocus(fixture.Bar.IncludeButton);

        fixture.Bar.IncludeButton.Click();
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.Attachments.Count == 2 && fixture.Bar.RemoveButtons.Count == 2 &&
            fixture.Session.FocusedElement == fixture.Bar.RemoveButtons[0]);
        fixture.Render();

        Assert.Equal(["report.pdf", "data.csv"], fixture.Composer.Attachments.Select(part => part.FileName));
        Assert.Equal(First, fixture.Composer.Attachments[0].Content.ToArray());
        Assert.Equal(Second, fixture.Composer.Attachments[1].Content.ToArray());
        Assert.Equal(1, fixture.Source!.CopySourceCalls);
        Assert.Null(fixture.Composer.ForwardAttachments);
        Assert.Equal(UiVisibility.Collapsed, fixture.Banner.Visibility);
        Assert.Equal("Included 2 original attachments. 0.3 / 8.0 MiB · 2/4 files", fixture.Composer.Status);
        Assert.Equal(FeedbackKind.Information, fixture.Composer.StatusKind);
        Assert.False(fixture.Composer.IsAddingAttachment);

        // Once included, the offer is gone: a second run adds nothing and reads nothing.
        var again = fixture.Composer.IncludeForwardAttachmentsAsync();
        fixture.Dispatcher.DrainUntil(() => again.IsCompleted);
        Assert.Equal(2, fixture.Composer.Attachments.Count);
        Assert.Equal(1, fixture.Source.CopySourceCalls);

        // The included files are ordinary draft attachments: they are saved with the draft.
        Assert.True(fixture.SaveAndDrain());
        var saved = (await store.LoadAsync()).Draft!.Draft.Attachments!;
        Assert.Equal(Second, saved[1].Content.ToArray());
    }

    [Fact]
    public void OverLimitOfferIsDisabledWithReason()
    {
        using var fixture = new Fixture(reader: TestSourceReceiver.Returning(TwoFiles()));
        Assert.True(fixture.Composer.StartFromMessage(Listed(fixture.Account, new MailAttachmentDescriptor(3, "video.mp4", "video/mp4", 9 * MiB)), CompositionKind.Forward));
        fixture.Render();
        const string tooLarge = "They need 9.0 MiB; this draft has 8.0 MiB left.";
        Assert.False(fixture.Composer.CanIncludeForwardAttachments);
        Assert.Equal(tooLarge, fixture.Composer.ForwardAttachmentsUnavailableReason);
        Assert.False(fixture.Bar.IncludeButton.IsEnabled);
        Assert.Equal(tooLarge, fixture.Bar.IncludeButton.ToolTipText);
        Assert.Contains(fixture.Labels(), text => text == tooLarge);
        var refused = fixture.Composer.IncludeForwardAttachmentsAsync();
        Assert.True(refused.IsCompleted);
        fixture.Discard();

        // Three parts on top of two files already attached would make five.
        Assert.True(fixture.Composer.StartFromMessage(Listed(fixture.Account,
            new(3, "a.txt", "text/plain", 10), new(4, "b.txt", "text/plain", 10), new(5, "c.txt", "text/plain", 10)), CompositionKind.Forward));
        fixture.Add(new ComposerAttachmentTests.TestReadableFile("one.txt", [1], 1));
        fixture.Add(new ComposerAttachmentTests.TestReadableFile("two.txt", [2], 1));
        const string tooMany = "Including them would exceed 4 files per draft (2 + 3).";
        Assert.Equal(tooMany, fixture.Composer.ForwardAttachmentsUnavailableReason);
        Assert.False(fixture.Bar.IncludeButton.IsEnabled);
        Assert.Equal(tooMany, fixture.Bar.IncludeButton.ToolTipText);

        // Removing one makes room, and Include is offered again.
        fixture.Composer.RemoveAttachment(0);
        fixture.Render();
        Assert.Null(fixture.Composer.ForwardAttachmentsUnavailableReason);
        Assert.True(fixture.Bar.IncludeButton.IsEnabled);
        Assert.Equal(0, fixture.Source!.CopySourceCalls + fixture.Source.SourceCalls);
    }

    [Fact]
    public void ReaderFailureAndCancelKeepOffer()
    {
        var reader = new ScriptedReader();
        using var fixture = new Fixture(reader);
        Assert.True(fixture.Composer.StartFromMessage(Decoded(fixture.Account, TwoFiles()), CompositionKind.Forward));
        fixture.Render();
        var offer = fixture.Composer.ForwardAttachments;

        // Offline: the offer stays, says why, and focus returns to Include.
        reader.Open = (_, _) => throw new MailConnectionException("No route.", MailConnectionFailure.Unreachable);
        fixture.Session.SetFocus(fixture.Bar.IncludeButton);
        fixture.Bar.IncludeButton.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy && fixture.Session.FocusedElement == fixture.Bar.IncludeButton);
        Assert.Same(offer, fixture.Composer.ForwardAttachments);
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("Connect to download the original attachments, then try again.", fixture.Composer.Status);
        Assert.Equal(FeedbackKind.Warning, fixture.Composer.StatusKind);

        // A broken download says so too.
        reader.Open = (_, _) => throw new IOException("Connection reset.");
        fixture.Bar.IncludeButton.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy && fixture.Session.FocusedElement == fixture.Bar.IncludeButton);
        Assert.Equal("Download did not finish. Connection reset.", fixture.Composer.Status);
        Assert.Same(offer, fixture.Composer.ForwardAttachments);

        // Cancel while downloading: progress is shown, focus moves to Cancel, and the offer survives.
        reader.Open = async (progress, token) =>
        {
            progress?.Invoke(1_024, 360_000);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable.");
        };
        fixture.Bar.IncludeButton.Click();
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.AttachmentReadProgress == "Downloading original attachments: 1,024 of 360,000 bytes.");
        fixture.Render();
        Assert.True(fixture.Composer.IsAddingAttachment);
        Assert.Same(fixture.Button("Cancel"), fixture.Session.FocusedElement);
        Assert.Contains(fixture.Descendants().OfType<InlineFeedback>(), line => line.Message == "Downloading original attachments: 1,024 of 360,000 bytes.");
        fixture.Button("Cancel").Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsBusy && fixture.Session.FocusedElement == fixture.Bar.IncludeButton);
        Assert.Equal("Original attachments not included; downloading was canceled.", fixture.Composer.Status);
        Assert.Same(offer, fixture.Composer.ForwardAttachments);
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal(3, reader.Calls);
        Assert.True(reader.Opened.All(source => source.Disposed));
    }

    [Theory]
    [InlineData("size")]
    [InlineData("type")]
    [InlineData("missing")]
    [InlineData("key")]
    public void MismatchedPartIsRejected(string change)
    {
        // "key": the reader answers for another message than the one forwarded.
        var reader = new ScriptedReader();
        using var fixture = change == "key" ? new Fixture(reader) : new Fixture(reader: TestSourceReceiver.Returning(change switch
        {
            "size" => Original(new("report.pdf", "application/pdf", First), new("data.csv", "text/csv", Second[..^1])),
            "type" => Original(new("report.pdf", "application/pdf", First), new("data.csv", "application/zip", Second)),
            _ => Original(new Part("report.pdf", "application/pdf", First)),
        }));
        reader.Open = async (_, token) => new OpenedSource(await new MessageAttachmentReader(TestSourceReceiver.Returning(TwoFiles()), null)
            .OpenAsync(fixture.Account, Key(fixture.Account, 2), null, token));
        var body = Decoded(fixture.Account, TwoFiles());
        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Forward));
        fixture.Render();
        var offer = fixture.Composer.ForwardAttachments;

        var include = fixture.Composer.IncludeForwardAttachmentsAsync();
        fixture.Dispatcher.DrainUntil(() => include.IsCompleted);

        // All or nothing: the part that still matches is not added either.
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal(ComposerViewModel.ForwardAttachmentsChangedText, fixture.Composer.Status);
        Assert.Same(offer, fixture.Composer.ForwardAttachments);
    }

    [Fact]
    public async Task OfferEndsWithDraftAndDismiss()
    {
        using var directory = new TestDirectory(); directory.Create();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using var fixture = new Fixture(reader: TestSourceReceiver.Returning(TwoFiles()), store: store);
        var body = Decoded(fixture.Account, TwoFiles());

        // × hides the notice for good and moves focus to Attach file….
        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Forward));
        fixture.Render();
        fixture.Session.SetFocus(fixture.Bar.HideForwardButton);
        fixture.Bar.HideForwardButton.Click();
        fixture.Render();
        Assert.Null(fixture.Composer.ForwardAttachments);
        Assert.Equal(UiVisibility.Collapsed, fixture.Banner.Visibility);
        Assert.Same(fixture.Button("Attach file…"), fixture.Session.FocusedElement);
        fixture.Discard();

        // Discarding the draft ends the offer.
        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Forward));
        Assert.NotNull(fixture.Composer.ForwardAttachments);
        fixture.Discard();
        Assert.Null(fixture.Composer.ForwardAttachments);

        // Parking the draft ends it too; it is never persisted, so resuming does not bring it back.
        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Forward));
        Guid parked = fixture.Composer.DraftId!.Value;
        var park = fixture.Composer.ParkDraftAsync();
        fixture.Dispatcher.DrainUntil(() => park.IsCompleted);
        Assert.True(await park);
        Assert.Null(fixture.Composer.ForwardAttachments);
        var resume = fixture.Composer.ResumeDraftAsync(parked);
        fixture.Dispatcher.DrainUntil(() => resume.IsCompleted);
        Assert.True(await resume);
        Assert.Equal(parked, fixture.Composer.DraftId);
        Assert.Null(fixture.Composer.ForwardAttachments);
        Assert.DoesNotContain("report.pdf", await File.ReadAllTextAsync(directory.File("drafts.json")));
        fixture.Discard();

        // Another account selected: the offer stays, but Include waits for the original account.
        Assert.True(fixture.Composer.StartFromMessage(body, CompositionKind.Forward));
        fixture.Composer.SetAccount(TestDirectory.Profile() with { EmailAddress = "other@example.test" });
        fixture.Render();
        Assert.NotNull(fixture.Composer.ForwardAttachments);
        Assert.False(fixture.Composer.CanIncludeForwardAttachments);
        Assert.Equal("Switch back to test@example.test to include them.", fixture.Composer.ForwardAttachmentsUnavailableReason);
        Assert.False(fixture.Bar.IncludeButton.IsEnabled);
        fixture.Composer.SetAccount(fixture.Account);
        fixture.Render();
        Assert.True(fixture.Bar.IncludeButton.IsEnabled);
        Assert.Equal(0, fixture.Source!.CopySourceCalls);
        // Let the last autosave finish before the folder is removed.
        Assert.True(fixture.SaveAndDrain());
    }

    [Fact]
    public void NoReaderShowsBannerWithoutButton()
    {
        using var fixture = new Fixture(reader: (IMessageAttachmentReader?)null);
        Assert.True(fixture.Composer.StartFromMessage(Decoded(fixture.Account, TwoFiles()), CompositionKind.Forward));
        fixture.Render();
        Assert.True(Shown(fixture.Banner));
        Assert.Contains(fixture.Labels(), text => text == "Original message has 2 attachments (254 KiB).");
        Assert.Contains(fixture.Labels(), text => text == "Original attachments can't be included in this mode.");
        Assert.False(Shown(fixture.Bar.IncludeButton));
        Assert.False(fixture.Composer.CanIncludeForwardAttachments);
        Assert.True(Shown(fixture.Bar.HideForwardButton));
        Assert.True(fixture.Composer.IncludeForwardAttachmentsAsync().IsCompleted);
        Assert.Empty(fixture.Composer.Attachments);
    }

    [Fact]
    public void ANoPickerDeviceStillIncludesOriginalAttachments()
    {
        using var fixture = new Fixture(reader: TestSourceReceiver.Returning(TwoFiles()), files: UnavailableMailFilePicker.NotOnThisDeviceYet);
        Assert.True(fixture.Composer.StartFromMessage(Decoded(fixture.Account, TwoFiles()), CompositionKind.Forward));
        fixture.Render();
        Assert.False(fixture.Bar.CanPick);
        Assert.True(Shown(fixture.Banner));
        Assert.True(fixture.Bar.IncludeButton.IsEnabled);

        fixture.Bar.IncludeButton.Click();
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.Attachments.Count == 2 && fixture.Bar.RemoveButtons.Count == 2 &&
            fixture.Session.FocusedElement == fixture.Bar.RemoveButtons[0]);
        Assert.Null(fixture.Composer.ForwardAttachments);
        fixture.Discard();

        // × hands focus to the typed path field, where adds start on this device.
        Assert.True(fixture.Composer.StartFromMessage(Decoded(fixture.Account, TwoFiles()), CompositionKind.Forward));
        fixture.Render();
        fixture.Bar.HideForwardButton.Click();
        fixture.Render();
        Assert.Equal(ComposerAttachmentsBar.PathLabel, fixture.Session.FocusedElement?.AccessibleName);
    }

    [Fact]
    public void APendingOfferDoesNotAskBeforeSending()
    {
        var account = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 465, UserName = "test", Security = TransportSecurity.Tls } };
        using var composer = new ComposerViewModel(sender: new AvailableSender()) { AttachmentReader = new ScriptedReader() };
        composer.SetAccount(account);
        Assert.True(composer.StartFromMessage(Decoded(account, TwoFiles()), CompositionKind.Forward));
        composer.Edit("to@example.test", "", "", composer.Subject, "Please see the attached report.\n" + composer.PlainText);
        Assert.NotNull(composer.AttachmentReminderText);
        Assert.NotNull(composer.ForwardAttachments);
        Assert.False(composer.RequestSendConfirmation());

        // Once the notice is hidden, the ordinary reminder asks again.
        composer.DismissForwardAttachments();
        Assert.True(composer.RequestSendConfirmation());
    }

    [Fact]
    public async Task ForwardedAttachmentsTravelFromLoopbackImapToLoopbackSmtp()
    {
        byte[] report = Random(4 * MiB, 11), sheet = Random(2 * MiB, 12);
        byte[] original = Original(new("Bericht.pdf", "application/pdf", report), new("Zahlen.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheet));
        await using var imap = new LocalImapServer(TransportSecurity.Tls);
        imap.Messages.Add(new(1, Encoding.ASCII.GetString(original)));
        await using var smtp = new LocalSmtpServer(TransportSecurity.Tls);
        var account = TestDirectory.Profile() with
        {
            IncomingServer = new() { Host = "127.0.0.1", Port = imap.Port, UserName = "test", Security = TransportSecurity.Tls },
            OutgoingServer = new() { Host = "127.0.0.1", Port = smtp.Port, UserName = "test", Security = TransportSecurity.Tls },
        };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Smtp), LocalSmtpServer.Password);
        var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == imap.Certificate.GetCertHashString(), TimeSpan.FromSeconds(20));
        var sender = new SmtpMailSender(credentials, (_, cert, _, _) => cert?.GetCertHashString() == smtp.Certificate.GetCertHashString(), TimeSpan.FromSeconds(20));
        var reader = new MessageAttachmentReader(receiver, receiver);
        using var inbox = new InboxViewModel(receiver, new ImmediateUiDispatcher()) { AttachmentReader = reader };
        inbox.SetAccount(account);
        await inbox.ReceiveAsync();
        await inbox.SelectAsync(Assert.Single(inbox.Messages).Key);
        Assert.Equal(2, inbox.Body!.Attachments!.Count);

        using var composer = new ComposerViewModel(sender: sender) { AttachmentReader = reader };
        composer.SetAccount(account);
        Assert.True(composer.StartFromMessage(inbox.Body, CompositionKind.Forward));
        Assert.Equal("Original message has 2 attachments (6.0 MiB).", composer.ForwardAttachments!.Text);
        Assert.Empty(composer.Attachments);
        int fetches = imap.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));

        await composer.IncludeForwardAttachmentsAsync();
        Assert.Equal(["Bericht.pdf", "Zahlen.xlsx"], composer.Attachments.Select(part => part.FileName));
        Assert.Equal("Included 2 original attachments. 6.0 / 8.0 MiB · 2/4 files", composer.Status);
        Assert.Equal(fetches + 1, imap.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal)));
        Assert.DoesNotContain(imap.Commands, command => command is "STORE" or "EXPUNGE");

        composer.Edit("to@example.test", "", "", composer.Subject, composer.PlainText);
        Assert.True(composer.CanSend);
        await composer.SendAsync();
        Assert.Equal(DraftSubmissionState.Accepted, composer.SubmissionState);
        Assert.Equal(1, smtp.DataCount);
        var delivered = MimeParser.Parse(Encoding.UTF8.GetBytes(smtp.RawMessage!));
        var parts = delivered.Parts.Where(part => part.IsAttachment).ToArray();
        Assert.Equal(["Bericht.pdf", "Zahlen.xlsx"], parts.Select(part => part.FileName));
        Assert.Equal(report, parts[0].Data);
        Assert.Equal(sheet, parts[1].Data);
    }

    private static bool Shown(UiElement element)
    {
        for (UiElement? at = element; at is not null; at = at.Parent) if (at.Visibility != UiVisibility.Visible) return false;
        return true;
    }

    private sealed class AvailableSender : IMailSender
    {
        public bool IsAvailable => true;
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SendResult(SubmissionStatus.Accepted, null));
    }

    /// <summary>A reader whose every open runs <see cref="Open"/>, recording the sources it returned.</summary>
    private sealed class ScriptedReader : IMessageAttachmentReader
    {
        public Func<Action<int, int>?, CancellationToken, Task<OpenedSource>> Open { get; set; } =
            (_, _) => throw new InvalidOperationException("No reader in this test.");
        public int Calls { get; private set; }
        public List<OpenedSource> Opened { get; } = [];

        public async Task<IMailAttachmentSource> OpenAsync(AccountProfile account, MailMessageKey key, Action<int, int>? progress = null, CancellationToken token = default)
        {
            Calls++;
            var source = await Open(progress, token);
            Opened.Add(source);
            return source;
        }
    }

    /// <summary>Wraps a real source to record its disposal.</summary>
    private sealed class OpenedSource(IMailAttachmentSource inner) : IMailAttachmentSource
    {
        public bool Disposed { get; private set; }
        public MailMessageKey Key => inner.Key;
        public bool FromSavedOriginal => inner.FromSavedOriginal;
        public IReadOnlyList<ReceivedAttachment> Attachments => inner.Attachments;
        public Task CopyToAsync(ReceivedAttachment attachment, Stream destination, CancellationToken token = default) => inner.CopyToAsync(attachment, destination, token);
        public ReadOnlyMemory<byte> Read(ReceivedAttachment attachment) => inner.Read(attachment);
        public void Dispose() { Disposed = true; inner.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ComposerViewModel _composer;
        private readonly ComposerView _view;
        private readonly FormSurface _surface;

        public Fixture(TestSourceReceiver reader, IDraftStore? store = null, IMailFilePicker? files = null)
            : this(new MessageAttachmentReader(reader, reader), store, files) => Source = reader;

        public Fixture(IMessageAttachmentReader? reader, IDraftStore? store = null, IMailFilePicker? files = null)
        {
            Files = files ?? new FakeMailFilePicker();
            Account = TestDirectory.Profile();
            Dispatcher = new TestQueueDispatcher();
            Store = store ?? new MemoryDraftStore();
            Inbox = new InboxViewModel(new TestMailReceiver(), Dispatcher);
            Inbox.SetAccount(Account);
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host(1100, 720));
            (_composer, _view, _surface) = Build(reader);
        }

        public AccountProfile Account { get; }
        public IMailFilePicker Files { get; }
        public TestSourceReceiver? Source { get; }
        public IDraftStore Store { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public ComposerViewModel Composer => _composer;
        public InboxViewModel Inbox { get; }
        public UiSession Session { get; }
        public ComposerAttachmentsBar Bar => _view.Attachments!;
        public UiElement Banner => Descendants().Single(element => element.AccessibleName == "Original attachments");
        public StandardButton Button(string text) => Descendants().OfType<StandardButton>().Single(button => button.Text == text && Shown(button));
        public string[] Labels() => Descendants().OfType<StandardLabel>().Where(Shown).Select(label => label.Text).Where(text => text.Length > 0).ToArray();
        public IEnumerable<UiElement> Descendants() => Walk(_surface);

        public void Add(IMailReadableFile file)
        {
            var add = Composer.AddAttachmentAsync(file);
            Dispatcher.DrainUntil(() => add.IsCompleted);
            add.GetAwaiter().GetResult();
            Render();
        }

        public void Discard()
        {
            var discard = Composer.DiscardAsync();
            Dispatcher.DrainUntil(() => discard.IsCompleted);
            Assert.True(discard.GetAwaiter().GetResult());
            Render();
        }

        public bool SaveAndDrain()
        {
            var save = Composer.SaveAsync();
            Dispatcher.DrainUntil(() => save.IsCompleted);
            return save.GetAwaiter().GetResult();
        }

        public void Render()
        {
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            _surface.Dispose();
            Inbox.Dispose();
            _composer.Dispose();
        }

        private (ComposerViewModel, ComposerView, FormSurface) Build(IMessageAttachmentReader? reader)
        {
            var composer = new ComposerViewModel(Store, dispatcher: Dispatcher) { AttachmentReader = reader };
            composer.SetAccount(Account);
            var view = new ComposerView(composer, Inbox, files: Files);
            var surface = (FormSurface)view.CreateContent();
            Session.AddRoot(surface);
            Session.RenderFrame();
            return (composer, view, surface);
        }

        private static IEnumerable<UiElement> Walk(UiElement root)
        {
            yield return root;
            foreach (var child in root.Children)
                foreach (var item in Walk(child)) yield return item;
        }
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
