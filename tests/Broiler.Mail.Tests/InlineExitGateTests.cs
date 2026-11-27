using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// UX-M1 exit gate: reading, zoom, remote-image consent, find, triage and search all happen inline in the shell's
/// inbox. After every step no dialog and no modal element is open and the inbox stays the active view; the removed
/// dialog ids no longer open anything. Only Mailbox options stays a dialog in M1 and is not driven here.
/// </summary>
[Collection("UI theme")]
public sealed class InlineExitGateTests
{
    [Theory]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1100, 720, 2.0)]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    public async Task ReadingFindTriageAndSearchNeverOpenADialog(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var app = await InlineMailFixture.OpenAsync(width, height);
            var (shell, session, model) = (app.Shell, app.Session, app.Inbox);
            string at = $"{width}x{height}, text {textScale:P0}";
            void Inline(string step)
            {
                Assert.True(shell.ActiveDialog is null, $"{at}, {step}: the dialog {shell.ActiveViewId} is open.");
                Assert.True(session.ModalElement is null, $"{at}, {step}: {session.ModalElement} is modal.");
                Assert.True(shell.ActiveViewId == "inbox", $"{at}, {step}: the active view is {shell.ActiveViewId}.");
                Assert.True(session.FocusedElement is null || session.FocusedElement.CanFocus, $"{at}, {step}: focus is on {session.FocusedElement}, which cannot take it.");
            }
            foreach (string removed in new[] { "message-actions", "find-message", "search-mail" })
                Assert.False(shell.ShowView(removed), $"{at}: {removed} still opens.");
            Inline("removed ids");

            // Reading: open a message, move to its end and top, switch between HTML and plain text.
            await app.OpenMessageAsync(0);
            Assert.NotNull(model.Body);
            Inline("open message");
            app.Menu("More message actions", "End of message"); Inline("End of message");
            Assert.Equal(1, app.Surface.ReadingPosition);
            app.Menu("More message actions", "Top of message"); Inline("Top of message");
            Assert.Equal(0, app.Surface.ReadingPosition);
            app.Click("Plain text"); Inline("Plain text");
            app.Click("HTML"); Inline("HTML");

            // Zoom.
            app.Click("Zoom in"); Inline("Zoom in");
            Assert.Equal(1.5, app.Surface.Zoom);
            app.Click("Zoom out"); app.Click("Zoom out"); Inline("Zoom out");
            Assert.Equal(0.5, app.Surface.Zoom);
            app.Click(Descendants(app.Content).OfType<StandardButton>().Single(button => button.AccessibleName?.StartsWith("Reset HTML zoom", StringComparison.Ordinal) == true));
            Inline("Reset zoom");
            Assert.Equal(1, app.Surface.Zoom);

            // Remote images: consent, cancel, retry and block again from the banner below the line.
            Assert.True(app.Shown(app.Banner), $"{at}: the remote images banner is hidden.");
            app.Click("Load images"); Inline("Load images");
            Assert.Equal(1, app.Surface.Loads);
            app.Click("Cancel loading"); Inline("Cancel loading");
            app.Click("Retry"); Inline("Retry");
            app.Surface.Complete(3); app.Render(); Inline("images loaded");
            app.Click("Block again"); Inline("Block again");
            Assert.Equal(1, app.Surface.Resets);
            Assert.Equal("Load images", app.Button("Load images").Text);

            // Find: Ctrl+F docks the bar, Enter, F3 and Shift+F3 move, Escape closes it again.
            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('F', KeyboardModifierState.Control)));
            app.Render(); Inline("Ctrl+F");
            Assert.True(app.Shown(app.Find), $"{at}: the find bar is hidden.");
            Assert.Same(app.FindQuery, session.FocusedElement);
            app.FindQuery.Text = "paragraph";
            app.Press(InlineMailFixture.Key(0x0D)); Inline("Enter in find");
            Assert.Contains("Match 1 of 20", app.Find.Status);
            app.Press(InlineMailFixture.Key(0x72)); Inline("F3");
            Assert.Contains("Match 2 of 20", app.Find.Status);
            app.Press(InlineMailFixture.Key(0x72, KeyboardModifierState.Shift)); Inline("Shift+F3");
            Assert.Contains("Match 1 of 20", app.Find.Status);
            app.Press(InlineMailFixture.Key(0x1B)); app.Render(); Inline("Escape in find");
            Assert.False(app.Shown(app.Find), $"{at}: Escape left the find bar open.");
            Assert.False(app.Layout.IsCompact && !app.Layout.ShowsReaderOnly, $"{at}: Escape in find left the compact reader.");

            // Parsed headers, then triage from the strip, the star and the menus.
            app.Menu("More message actions", "Show parsed headers"); Inline("Show parsed headers");
            app.Menu("More message actions", "Mark read"); app.Drain(); Inline("Mark read");
            Assert.Single(app.Receiver.ReadChanges);
            app.Click("Star message"); app.Drain(); Inline("Star");
            Assert.Single(app.Receiver.StarChanges);
            app.Click("Archive"); app.Drain(); Inline("Archive");
            app.Click("Trash"); app.Drain(); Inline("Trash");
            app.Menu("Move ▾", "Archive — Archive"); app.Drain(); Inline("Move to a quick target");
            Assert.Equal(["Archive", "Trash", "Archive"], app.Receiver.Moves.Select(move => move.Destination).ToArray());
            app.Menu("Move ▾", "Choose folder…"); app.Render(); Inline("Choose folder…");
            var picker = Descendants(app.Content).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == "Move destination");
            Assert.Same(picker, session.FocusedElement);
            app.Click("Close move picker"); Inline("Close move picker");

            // Delete permanently… in the verified Trash asks inline, and Keep message keeps it.
            Assert.True(model.SelectFolder("Trash"));
            await model.CacheLoadTask; app.Drain();
            await model.ReceiveAsync(); app.Drain();
            await app.OpenMessageAsync(0);
            Assert.Equal(UiVisibility.Collapsed, app.Button("Trash").Visibility);
            app.Click("Delete permanently…"); Inline("Delete permanently…");
            Assert.Same(app.Button("Keep message"), session.FocusedElement);
            app.Click("Keep message"); Inline("Keep message");
            Assert.Null(model.PendingPermanentDelete);
            Assert.Empty(app.Receiver.Deletions);
            Assert.True(model.SelectFolder("INBOX"));
            await model.CacheLoadTask; app.Drain();
            await app.OpenMessageAsync(0);

            // Search: Ctrl+K and Ctrl+E focus the header's query, Enter searches, pages and Exit search stay inline.
            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('E', KeyboardModifierState.Control)));
            app.Render(); Inline("Ctrl+E");
            Assert.Same(app.Query, session.FocusedElement);
            app.Press(InlineMailFixture.Key(0x1B)); app.Render(); Inline("Escape closes the empty search");
            Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('K', KeyboardModifierState.Control)));
            app.Render(); Inline("Ctrl+K");
            Assert.Same(app.Query, session.FocusedElement);
            app.Query.Text = "needle";
            app.Press(InlineMailFixture.Key(0x0D)); app.Drain(); Inline("Enter searches");
            Assert.True(model.IsSearchActive);
            Assert.Equal(500, model.Messages.Count);
            app.Click(app.Named("Next saved results")); app.Drain(); Inline("Next saved results");
            Assert.Equal(2, model.SearchPageNumber);
            app.Click(app.Named("Previous saved results")); app.Drain(); Inline("Previous saved results");
            Assert.Equal(1, model.SearchPageNumber);
            app.Click("Exit search"); app.Drain(); Inline("Exit search");
            Assert.False(model.IsSearchActive);

            foreach (string removed in new[] { "message-actions", "find-message", "search-mail" })
                Assert.False(shell.ShowView(removed), $"{at}: {removed} opens after the walk.");
            Inline("end");
            Assert.Empty(app.Receiver.Deletions);
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    /// <summary>
    /// UX-M2: listing, browsing and focusing a message's attachments stays inline like the rest of reading. Only an
    /// explicit Save as… or Save all… opens a file dialog, the documented exemption to the no-modal rule; while it is
    /// open it is the only modal element, no other dialog opens, and Escape closes only it.
    /// </summary>
    [Theory]
    [InlineData(1100, 720, 1.0)]
    [InlineData(640, 480, 2.0)]
    public async Task AttachmentStripOpensNoDialogUntilSaveIsChosen(int width, int height, double textScale)
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var app = await InlineMailFixture.OpenAsync(width, height, attachments: 5);
            var (shell, session) = (app.Shell, app.Session);
            string at = $"{width}x{height}, text {textScale:P0}";
            void Inline(string step)
            {
                Assert.True(shell.ActiveDialog is null, $"{at}, {step}: the dialog {shell.ActiveViewId} is open.");
                Assert.True(shell.ActiveFileDialog is null, $"{at}, {step}: a file dialog is open.");
                Assert.True(session.ModalElement is null, $"{at}, {step}: {session.ModalElement} is modal.");
                Assert.True(shell.ActiveViewId == "inbox", $"{at}, {step}: the active view is {shell.ActiveViewId}.");
            }
            await app.OpenMessageAsync(0);
            Inline("open message with attachments");
            var strip = app.Attachments;
            Assert.True(app.Shown(strip), $"{at}: the attachment strip is hidden.");
            app.Menu("More message actions", "Attachments (5)…"); Inline("Attachments (5)…");
            var saves = Descendants(strip).OfType<StandardButton>().Where(button => button.Text == "Save as…").ToArray();
            Assert.Same(saves[0], session.FocusedElement);
            app.Click("Show all 5 attachments"); Inline("Show all");
            app.Press(InlineMailFixture.Key(0x09)); Inline("Tab in the strip");
            app.Click("Show fewer attachments"); Inline("Show fewer");

            void ExpectOnlyTheFileDialog(string step)
            {
                var dialog = shell.ActiveFileDialog;
                Assert.True(dialog is not null, $"{at}, {step}: no file dialog opened.");
                Assert.Same(dialog, session.ModalElement);
                Assert.True(shell.ActiveDialog is null, $"{at}, {step}: the dialog {shell.ActiveViewId} opened too.");
                Assert.Equal("inbox", shell.ActiveViewId);
                app.Press(InlineMailFixture.Key(0x1B));
                app.Render();
                ((TestQueueDispatcher)session.Dispatcher).DrainUntil(() => !strip.IsBusy);
                app.Render();
                Inline(step + " closed with Escape");
                Assert.Equal("", strip.Status);
            }
            session.SetFocus(saves[1]);
            saves[1].Click();
            app.Render();
            ExpectOnlyTheFileDialog("Save as…");
            Assert.Same(saves[1], session.FocusedElement);
            var saveAll = app.Button("Save all…");
            session.SetFocus(saveAll);
            saveAll.Click();
            app.Render();
            ExpectOnlyTheFileDialog("Save all…");
            Assert.False(strip.IsDisposed);
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }
}

/// <summary>
/// The shell's inbox over a SQLite cache and an account that can triage, with HTML that has remote images: 501
/// saved INBOX headers that all match "needle", a verified Archive and Trash, and a Trash that holds two messages.
/// </summary>
internal sealed class InlineMailFixture : IDisposable
{
    private readonly TestDirectory _directory;
    private readonly ImageHost _html;

    private InlineMailFixture(MailShellView shell, MailShellViewModel model, UiSession session, TestDirectory directory, GateReceiver receiver, ImageHost html)
    {
        (Shell, Model, Session, _directory, Receiver, _html) = (shell, model, session, directory, receiver, html);
        Keyboard = shell.CreateKeyboardNavigation(session);
    }

    public MailShellView Shell { get; }
    public MailShellViewModel Model { get; }
    public InboxViewModel Inbox => Model.Inbox;
    public UiSession Session { get; }
    public GateReceiver Receiver { get; }
    public MailKeyboardNavigation Keyboard { get; }
    public UiElement Content => Shell.GetContent("inbox");
    public ImageSurface Surface => Assert.IsType<ImageSurface>(_html.Surface);
    public AdaptiveInboxLayout Layout => Descendants(Content).OfType<AdaptiveInboxLayout>().Single();
    public RemoteImageActions Banner => Descendants(Content).OfType<RemoteImageActions>().Single();
    public MessageTextFindBar Find => Descendants(Content).OfType<MessageTextFindBar>().Single();
    public BoundedScrollArea Header => Descendants(Content).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
    /// <summary>The reader's banner slot below the line, which holds the remote-images banner and the find bar.</summary>
    public BoundedScrollArea Notices => Descendants(Content).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Reader notices");
    public ScrollableMessageText Text => Descendants(Content).OfType<ScrollableMessageText>().Single();
    public StandardEdit FindQuery => Descendants(Find).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Find in message text");
    public StandardEdit Query => Descendants(Content).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Saved mail search text");
    public StandardToolbar Strip => Descendants(Content).OfType<StandardToolbar>().Single(row => row.AccessibleName == "Saved search results");
    public StandardButton Button(string text) => Descendants(Content).OfType<StandardButton>().Single(button => button.Text == text);
    public StandardButton Named(string name) => Descendants(Content).OfType<StandardButton>().Single(button => button.AccessibleName == name);

    /// <summary>Whether the element and every container above it are shown, inside the inbox.</summary>
    public bool Shown(UiElement element)
    {
        for (UiElement? at = element; at is not null; at = at.Parent) if (at.Visibility != UiVisibility.Visible) return false;
        return element.IsDescendantOf(Content);
    }

    /// <summary>Asserts a pointer user can reach the element (see <see cref="Reachability.Assert"/>).</summary>
    public void AssertReachable(UiElement element, string? where = null)
    {
        Assert.True(Shown(element), $"{where ?? element.AccessibleName ?? element.GetType().Name} is hidden.");
        Reachability.Assert(Session, Render, element, where);
    }

    public void Render()
    {
        Session.RenderFrame();
        ((TestQueueDispatcher)Session.Dispatcher).Drain();
        Session.RenderFrame();
    }

    public void Drain()
    {
        ((TestQueueDispatcher)Session.Dispatcher).DrainUntil(() => !Inbox.IsBusy);
        Render();
    }

    public void Click(string text) => Click(Button(text));

    public void Click(StandardButton button)
    {
        Assert.True(button.IsEnabled, $"{button.Text} is disabled.");
        // A pointer user can press only a button they can see, scrolled into view where it scrolls.
        AssertReachable(button, button.Text);
        // As a pointer or keyboard user does, the button has focus when it is pressed.
        if (button.CanFocus) Session.SetFocus(button);
        button.Click();
        Drain();
    }

    public void Menu(string trigger, string item)
    {
        ReaderMenus.Invoke(Session, Content, trigger, item);
        Render();
    }

    /// <summary>As the host does: a key the shell does not take reaches the focused control.</summary>
    public void Press(UiInputEvent input)
    {
        if (!Keyboard.Handle(input)) Session.DispatchInput(input);
    }

    /// <summary>Selects the folder's message at <paramref name="index"/> and opens it in the reader.</summary>
    public async Task OpenMessageAsync(int index)
    {
        await Inbox.SelectAsync(Inbox.Messages[index].Key);
        Drain();
        Shell.Inbox.OpenSelected();
        Drain();
        Assert.NotNull(Inbox.Body);
    }

    public static UiInputEvent Key(char key, KeyboardModifierState modifiers) => ReaderMenus.Key(key, "VirtualKey:" + (int)key, modifiers);

    public static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        ReaderMenus.Key(code, code switch { 0x1B => "Escape", 0x09 => "Tab", _ => "VirtualKey:" + code }, modifiers);

    /// <summary>The reader's attachment strip, shown for a body with attachments.</summary>
    public MessageAttachmentStrip Attachments => Descendants(Content).OfType<MessageAttachmentStrip>().Single();

    /// <summary>
    /// Opens the fixture. With <paramref name="attachments"/> every body lists that many attachments, and the inbox has an
    /// attachment reader that is never expected to run; <paramref name="files"/> replaces the shell's own file dialogs.
    /// </summary>
    public static async Task<InlineMailFixture> OpenAsync(int width, int height, int attachments = 0, IMailFilePicker? files = null)
    {
        var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var storage = new SqliteMailCache(directory.File("mail.db"));
        var inbox = new MailboxKey(account.Id, "INBOX", 7);
        var saved = GateReceiver.Rows(inbox, 501);
        await storage.SaveSummariesAsync(inbox, saved.Take(500).ToArray(), DateTimeOffset.UtcNow);
        await storage.SaveSummariesAsync(inbox, saved.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        var receiver = new GateReceiver();
        if (attachments > 0)
            receiver.Attachments = Enumerable.Range(1, attachments)
                .Select(i => new MailAttachmentDescriptor(i + 1, $"attachment report number {i} with a long name.pdf", "application/pdf", 1000 * i)).ToArray();
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher, storage) { AttachmentReader = attachments > 0 ? new UnusedAttachmentReader() : null },
            new ComposerViewModel(dispatcher: dispatcher));
        var html = new ImageHost();
        var shell = new MailShellView(model, html, files: files);
        var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
        shell.Attach(session);
        shell.ShowView("inbox");
        await model.Inbox.CacheLoadTask;
        dispatcher.Drain();
        // Discover the folders, so Archive and Trash are confirmed targets when a message opens.
        await model.Inbox.RefreshFoldersAsync();
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        var fixture = new InlineMailFixture(shell, model, session, directory, receiver, html);
        fixture.Render();
        Assert.NotEmpty(model.Inbox.Messages);
        return fixture;
    }

    public void Dispose()
    {
        Session.Dispose();
        Shell.Dispose();
        // Switching between HTML and plain text remembers the reader mode in a background write; let it finish.
        for (int attempt = 0; ; attempt++)
        {
            try { _directory.Dispose(); return; }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
        }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>
    /// An IMAP-like account: INBOX, a verified Archive and Trash, and every change recorded and answered NotApplied,
    /// so the messages stay where they are and the walk can go on with them.
    /// </summary>
    internal sealed class GateReceiver : IMailFolderReceiver, IMailMover, IMailReadStateWriter, IMailStarStateWriter, IMailPermanentDeleter
    {
        private static readonly DateTimeOffset ReceivedAtBase = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);
        public List<(MailMessageKey Key, string Destination)> Moves { get; } = [];
        public List<(MailMessageKey Key, bool IsRead)> ReadChanges { get; } = [];
        public List<(MailMessageKey Key, bool IsStarred)> StarChanges { get; } = [];
        public List<MailMessageKey> Deletions { get; } = [];

        public static MailMessageSummary[] Rows(MailboxKey mailbox, int count) =>
            Enumerable.Range(1, count).Select(uid => new MailMessageSummary
            {
                Key = new(mailbox.AccountId, mailbox.MailboxId, mailbox.UidValidity, (uint)uid),
                Sender = $"sender{uid}@example.test", Subject = $"needle report {uid}", ReceivedAt = ReceivedAtBase.AddMinutes(-uid),
            }).ToArray();

        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailFolder>>([new("INBOX", "/", true, []), new("Archive", "/", true, ["\\Archive"]), new("Trash", "/", true, ["\\Trash"])]);
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            GetMailboxAsync(account, "INBOX", maximumCount, older, cancellationToken);

        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        {
            var mailbox = new MailboxKey(account.Id, mailboxId, mailboxId switch { "Trash" => 9u, "Archive" => 8u, _ => 7u });
            MailMessageSummary[] rows = mailboxId == "Trash" ? Rows(mailbox, 2) : [];
            return Task.FromResult(new MailInboxPage(rows, null) { Mailbox = mailbox });
        }

        /// <summary>While true, every body fails to load, as it does offline, so the reader shows the problem and Retry.</summary>
        public bool FailBodies { get; set; }

        /// <summary>The attachments every body lists; none by default.</summary>
        public IReadOnlyList<MailAttachmentDescriptor>? Attachments { get; set; }

        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default) =>
            FailBodies ? Task.FromException<MailMessageBody>(new MailConnectionException("The connection closed.")) : Task.FromResult(new MailMessageBody(message, string.Join("\n\n", Enumerable.Repeat("A paragraph of the message body.", 20)),
                "<p>A paragraph of the message body.</p><img src='https://images.example.test/a'>")
            {
                Composition = new MailCompositionSource { From = ["sender@example.test"], To = [account.EmailAddress], Subject = "Subject", MessageId = "message@example.test" },
                Attachments = Attachments,
            });

        public Task<MailMoveResult> MoveAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId, CancellationToken cancellationToken = default)
        {
            Moves.Add((source, destinationMailboxId));
            return Task.FromResult(new MailMoveResult(source, destinationMailboxId, MailMoveStatus.NotApplied, null, "Fixture move not applied"));
        }

        public Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead, CancellationToken cancellationToken = default)
        {
            ReadChanges.Add((key, isRead));
            return Task.FromResult(new ReadStateChangeResult(key, isRead, MailFlagChangeStatus.NotApplied, "Fixture read state not applied"));
        }

        public Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred, CancellationToken cancellationToken = default)
        {
            StarChanges.Add((key, isStarred));
            return Task.FromResult(new MailFlagChangeResult(key, isStarred, MailFlagChangeStatus.NotApplied, "Fixture star state not applied"));
        }

        public Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default)
        {
            Deletions.Add(key);
            return Task.FromResult(new MailDeleteResult(key, MailDeleteStatus.NotApplied, "Fixture deletion not applied"));
        }
    }

    /// <summary>An attachment reader the walks never reach: they only list attachments or cancel a picker.</summary>
    private sealed class UnusedAttachmentReader : IMessageAttachmentReader
    {
        public Task<IMailAttachmentSource> OpenAsync(AccountProfile account, MailMessageKey key, Action<int, int>? progress = null, CancellationToken token = default) =>
            throw new InvalidOperationException("The walk never reads an original.");
    }

    /// <summary>Gives each message an HTML surface with three blocked remote images that can be loaded and blocked again.</summary>
    private sealed class ImageHost : IInlineHtmlPreviewHost
    {
        public UiElement? Surface;
        public UiElement CreateInlineContent(MailMessageBody message)
        {
            var surface = new ImageSurface();
            surface.AddChild(new StandardButton { Text = "Fixture HTML link" });
            return Surface = surface;
        }
    }

    internal sealed class ImageSurface : UiElement, IInlineHtmlReader, IReaderViewport, IInlineRemoteImages, IInlineRemoteImageReset
    {
        private EventHandler? _changed;
        public int Loads, Resets;
        protected override BSize MeasureCore(BSize availableSize) => Children.Single().Measure(availableSize);
        public event EventHandler? ViewChanged;
        public event EventHandler? RenderingFailed { add { } remove { } }
        public double ReadingPosition { get; private set; }
        public void RestoreReadingPosition(double value) => ReadingPosition = Math.Clamp(value, 0, 1);
        public double Zoom { get; private set; } = 1;
        public bool CanZoomIn => Zoom < 3;
        public bool CanZoomOut => Zoom > 0.5;
        public void ZoomBy(int direction) { Zoom = Math.Clamp(Zoom + direction * 0.5, 0.5, 3); ViewChanged?.Invoke(this, EventArgs.Empty); }
        public void ResetZoom() { Zoom = 1; ViewChanged?.Invoke(this, EventArgs.Empty); }
        public event EventHandler? RemoteImagesChanged { add => _changed += value; remove => _changed -= value; }
        public RemoteImageState RemoteImages { get; private set; } = new(3, 0, false, true, "Remote images are blocked.");
        public Task LoadRemoteImagesAsync() { Loads++; Set(RemoteImages with { IsLoading = true, CanLoad = false, Status = "Loading remote images for this message…" }); return Task.CompletedTask; }
        public void CancelRemoteImages() { if (RemoteImages.IsLoading) Set(RemoteImages with { IsLoading = false, CanLoad = true, Status = "Remote image loading canceled." }); }
        public void Complete(int loaded) => Set(new(3, loaded, false, loaded < 3, loaded == 3 ? "Loaded 3 remote images for this message." : $"Loaded {loaded} of 3 remote images."));
        public void BlockRemoteImages() { Resets++; CancelRemoteImages(); Set(new(3, 0, false, true, "Remote images are blocked again. Previously sent requests cannot be undone.")); }
        private void Set(RemoteImageState state) { RemoteImages = state; _changed?.Invoke(this, EventArgs.Empty); }
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

internal static class Reachability
{
    /// <summary>
    /// Asserts a pointer user can reach the element: scrolled into view, as Tab or a scroll bar brings it, it is laid
    /// out inside every container above it and the window, and nothing above it clips it, except that a scroll view
    /// shorter or narrower than the element shows as much of it as the scroll view's own size allows.
    /// </summary>
    public static void Assert(UiSession session, Action render, UiElement element, string? where = null)
    {
        string name = where ?? element.AccessibleName ?? element.GetType().Name;
        element.BringIntoView();
        render();
        BRect shown = element.Bounds;
        Xunit.Assert.True(shown.Width > 0 && shown.Height > 0, $"{name} is not laid out: {shown}.");
        for (UiElement? at = element.Parent; at is not null; at = at.Parent)
        {
            string container = $"{at.AccessibleName ?? at.GetType().Name} at {at.Bounds}";
            if (at is IUiScrollable)
            {
                BRect inView = shown.Intersect(at.Bounds);
                Xunit.Assert.True(inView.Height >= Math.Min(shown.Height, at.Bounds.Height) - 0.5 && inView.Width >= Math.Min(shown.Width, at.Bounds.Width) - 0.5,
                    $"{name} at {element.Bounds} is scrolled out of {container}: {inView} of it shows.");
                shown = inView;
            }
            else Xunit.Assert.True(Inside(shown, at.Bounds), $"{name} at {shown} is cut off by {container}.");
        }
        var window = session.Host.ViewportSize;
        Xunit.Assert.True(Inside(shown, new BRect(0, 0, window.Width, window.Height)), $"{name} at {shown} is outside the window.");
        BRect visible = element.GetVisibleBounds();
        Xunit.Assert.True(Math.Abs(visible.Height - shown.Height) <= 0.5 && Math.Abs(visible.Width - shown.Width) <= 0.5, $"{name} at {element.Bounds} shows only {visible}, not {shown}.");
    }

    private static bool Inside(BRect inner, BRect outer) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 && inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;
}
