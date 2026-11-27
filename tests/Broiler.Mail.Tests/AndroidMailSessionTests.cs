using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Android;
using Broiler.Mail.Application;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class AndroidMailSessionTests
{
    [Fact]
    public async Task SetupAndFormsStayInsidePhoneViewportAndBackReturnsToInbox()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            Assert.Equal("account", mail.Shell.ActiveViewId);
            Assert.True(mail.TryGoBack());
            Assert.Equal("inbox", mail.Shell.ActiveViewId);
            Assert.False(mail.TryGoBack());
            foreach (var view in new[] { "account", "settings", "compose" })
            {
                mail.Shell.ShowView(view);
                mail.RenderFrame();
                var dialog = Assert.IsType<Broiler.UI.Dialog.Standard.StandardDialog>(mail.Shell.ActiveDialog);
                Assert.False(dialog.IsBrokenOut);
                Assert.InRange(dialog.Placement.Right, 1, 360);
                Assert.InRange(dialog.Placement.Bottom, 1, 640);
                Assert.True(mail.TryGoBack());
            }
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task PauseSavesDraftWithoutDisablingFurtherEditsAndRestartRecoversIt()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.Accounts.SaveAsync(TestDirectory.Profile());
        await application.InitializeAsync();
        using (var mail = new MailSession(application, new Host(), _ => { }))
        {
            Assert.True(mail.Model.Composer.StartNew());
            mail.Model.Composer.Edit("", "", "", "Before pause", "Draft body");
            Assert.True(await mail.SaveDraftAsync());
            mail.Model.Composer.Edit("", "", "", "After resume", "Draft body");
            Assert.True(await mail.SaveDraftAsync());
        }
        var reopened = CreateApplication(directory);
        await reopened.InitializeAsync();
        Assert.Equal("After resume", reopened.LoadedDraft.Draft?.Draft.Subject);
    }

    [Fact]
    public async Task BackClosesAnOpenFindBeforeItLeavesTheReader()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var message = new Broiler.Mail.Core.Messages.MailMessageSummary { Key = new(account.Id, "INBOX", 7, 1), Sender = "author@example.test", Subject = "Plans" };
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new Broiler.Mail.Core.Messages.MailInboxPage([message], null)),
            Body = (key, _) => Task.FromResult(new Broiler.Mail.Core.Messages.MailMessageBody(key, "Some message text.")),
        };
        var application = CreateApplication(directory, receiver);
        await application.Accounts.SaveAsync(account);
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            // The session's dispatcher drains only on this thread, so wait here rather than awaiting.
            void Run(Task work)
            {
                for (int i = 0; i < 2000 && !work.IsCompleted; i++)
                {
                    while (posts.TryDequeue(out var post)) post();
                    mail.RenderFrame();
                    if (!work.IsCompleted) Thread.Sleep(1);
                }
                work.GetAwaiter().GetResult();
                while (posts.TryDequeue(out var post)) post();
                mail.RenderFrame();
            }
            Assert.Equal("inbox", mail.Shell.ActiveViewId);
            Run(mail.Model.Inbox.ReceiveAsync());
            Run(mail.Model.Inbox.SelectAsync(message.Key));
            Assert.True(mail.Shell.Inbox.OpenSelected());
            // Opening reloads the message; wait until its body is shown again.
            for (int i = 0; i < 2000 && (mail.Model.Inbox.IsBusy || mail.Model.Inbox.Body is null); i++) Run(Task.Delay(1));
            Assert.NotNull(mail.Model.Inbox.Body);
            Assert.True(mail.Shell.Inbox.ShowFindInMessage());
            mail.RenderFrame();

            // Back closes Find first, and the reader stays; then it leaves the reader; then there is nothing left.
            Assert.True(mail.TryGoBack());
            Assert.False(mail.Shell.Inbox.CloseFindInMessage());
            Assert.Null(mail.Shell.ActiveDialog);
            Assert.True(mail.TryGoBack());
            Assert.False(mail.TryGoBack());
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task BackClearsThenExitsAnInlineSearchBeforeAnythingElse()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var messages = Enumerable.Range(1, 3).Select(uid => new Broiler.Mail.Core.Messages.MailMessageSummary
            { Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "author@example.test", Subject = uid == 2 ? "needle" : $"Plans {uid}" }).ToArray();
        var receiver = new TestMailReceiver { Inbox = (_, _) => Task.FromResult(new Broiler.Mail.Core.Messages.MailInboxPage(messages, null)) };
        var application = CreateApplication(directory, receiver, new SqliteMailCache(directory.File("mail.db")));
        await application.Accounts.SaveAsync(account);
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            void Run(Task work)
            {
                for (int i = 0; i < 2000 && !work.IsCompleted; i++)
                {
                    while (posts.TryDequeue(out var post)) post();
                    mail.RenderFrame();
                    if (!work.IsCompleted) Thread.Sleep(1);
                }
                work.GetAwaiter().GetResult();
                while (posts.TryDequeue(out var post)) post();
                mail.RenderFrame();
            }
            var inbox = mail.Model.Inbox;
            Run(inbox.CacheLoadTask);
            Run(inbox.ReceiveAsync());
            Assert.Equal(3, inbox.Messages.Count);
            Assert.True(mail.Shell.Inbox.ShowSearch());
            inbox.SetSearchText("needle");
            Run(inbox.SearchSavedHeadersAsync());
            Assert.True(inbox.IsSearchActive);
            Assert.Single(inbox.Messages);

            // Back clears the query, then exits search, with no dialog; only then is there nothing to go back from.
            Assert.True(mail.TryGoBack());
            Assert.Equal("", inbox.SearchText);
            Assert.True(inbox.IsSearchActive);
            Assert.True(mail.TryGoBack());
            for (int i = 0; i < 2000 && (inbox.IsBusy || inbox.IsSearchActive); i++) Run(Task.Delay(1));
            Assert.False(inbox.IsSearchActive);
            Assert.Equal(3, inbox.Messages.Count);
            Assert.Null(mail.Shell.ActiveDialog);
            Assert.False(mail.TryGoBack());
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task AndroidUsesUnavailableFilePicker()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            // Until the document picker of UX-M5, Android chooses no files; the views keep whatever typed path fields they have.
            Assert.Same(Broiler.Mail.Application.Views.UnavailableMailFilePicker.NotOnThisDeviceYet, mail.Shell.Files);
            Assert.Same(mail.Shell.Files, mail.Shell.Composer.Files);
            Assert.Same(mail.Shell.Files, mail.Shell.SavedOriginals.LocalImports.Files);
            Assert.Equal(Broiler.Mail.Core.Services.MailFilePickerCapabilities.None, mail.Shell.Files.Capabilities);
            var request = new Broiler.Mail.Core.Services.MailFileRequest(Broiler.Mail.Core.Services.MailFilePurpose.Attachment, "Attach file");
            foreach (var pick in new[] { (await mail.Shell.Files.PickOpenFileAsync(request)).Message, (await mail.Shell.Files.PickSaveFileAsync(request)).Message,
                (await mail.Shell.Files.PickDirectoryAsync(request)).Message })
                Assert.Contains("not available on this device yet", pick);
            Assert.Null(mail.Shell.ActiveFileDialog);

            // Back behaves as before: it closes setup and returns to the inbox.
            Assert.Equal("account", mail.Shell.ActiveViewId);
            Assert.True(mail.TryGoBack());
            Assert.Equal("inbox", mail.Shell.ActiveViewId);
            Assert.False(mail.TryGoBack());
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task ComposeKeepsTheTypedAttachmentPathInsideThePhoneViewport()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.Accounts.SaveAsync(TestDirectory.Profile());
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            Assert.True(mail.Model.Composer.StartNew());
            mail.Shell.ShowView("compose");
            while (posts.TryDequeue(out var post)) post();
            mail.RenderFrame();
            var dialog = Assert.IsType<Broiler.UI.Dialog.Standard.StandardDialog>(mail.Shell.ActiveDialog);
            Assert.InRange(dialog.Placement.Right, 1, 360);
            Assert.InRange(dialog.Placement.Bottom, 1, 640);

            // Until the document picker of UX-M5 there is no Attach file…: the hint says so and the typed path stays.
            var bar = Assert.IsType<Broiler.Mail.Application.Views.ComposerAttachmentsBar>(mail.Shell.Composer.Attachments);
            Assert.False(bar.CanPick);
            Assert.Equal(UiVisibility.Visible, bar.Visibility);
            var labels = Descendants(bar).OfType<Broiler.UI.Label.Standard.StandardLabel>().ToArray();
            Assert.Contains(labels, label => label.Text == Broiler.Mail.Application.Views.ComposerAttachmentsBar.TypedPathHint);
            Assert.Contains(Descendants(bar).OfType<Broiler.UI.Edit.Standard.StandardEdit>(), edit => edit.AccessibleName == Broiler.Mail.Application.Views.ComposerAttachmentsBar.PathLabel && edit.Bounds.Right <= 360);
            var buttons = Descendants(bar).OfType<Broiler.UI.Button.Standard.StandardButton>().ToArray();
            Assert.DoesNotContain(buttons, button => button.Text == "Attach file…");
            Assert.Contains(buttons, button => button.Text == "Add attachment");
            Assert.InRange(bar.Bounds.Right, 1, 360);
            // The new draft's autosave has finished before the profile folder is removed.
            Assert.True(await mail.SaveDraftAsync());
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task BackDismissesTheSendConfirmationWithoutClosingCompose()
    {
        using var directory = new TestDirectory();
        var application = CreateApplication(directory);
        await application.Accounts.SaveAsync(TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test" } });
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            Assert.True(mail.Model.Composer.StartNew());
            mail.Model.Composer.Edit("to@example.test", "", "", "Report", "The report is attached.");
            mail.Shell.ShowView("compose");
            while (posts.TryDequeue(out var post)) post();
            mail.RenderFrame();
            var dialog = mail.Shell.ActiveDialog;
            var content = mail.Shell.GetContent("compose");
            var buttons = Descendants(content).OfType<Broiler.UI.Button.Standard.StandardButton>();
            buttons.Single(button => button.Text == "Send").Click();
            while (posts.TryDequeue(out var post)) post();
            mail.RenderFrame();

            // The reminder asks inline; nothing is sent and no other dialog opens.
            Assert.True(mail.Model.Composer.ShowsSendConfirmation);
            Assert.Equal(Broiler.Mail.Core.Messages.DraftSubmissionState.Editing, mail.Model.Composer.SubmissionState);
            var confirmation = Assert.IsAssignableFrom<UiElement>(mail.Shell.Composer.SendConfirmation);
            Assert.Equal(UiVisibility.Visible, confirmation.Visibility);
            Assert.InRange(confirmation.Bounds.Right, 1, 360);
            // Without a file picker the reminder's first choice leads to the typed path field.
            var attach = Descendants(confirmation).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == "Add a file first");
            // Label in Name (WCAG 2.5.3): the spoken name contains the words on screen.
            Assert.StartsWith(attach.Text, attach.GetSemanticNode().Name, StringComparison.Ordinal);
            Assert.Same(dialog, mail.Shell.ActiveDialog);

            // Back dismisses the reminder first and keeps Compose open; the next Back closes it.
            Assert.True(mail.TryGoBack());
            mail.RenderFrame();
            Assert.False(mail.Model.Composer.ShowsSendConfirmation);
            Assert.Equal(UiVisibility.Collapsed, confirmation.Visibility);
            Assert.Same(dialog, mail.Shell.ActiveDialog);
            Assert.Equal("compose", mail.Shell.ActiveViewId);

            // Asked again, its first choice dismisses it and focuses the typed path.
            buttons.Single(button => button.Text == "Send").Click();
            while (posts.TryDequeue(out var post)) post();
            mail.RenderFrame();
            attach.Click();
            mail.RenderFrame();
            Assert.False(mail.Model.Composer.ShowsSendConfirmation);
            var path = Descendants(content).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(edit => edit.AccessibleName == Broiler.Mail.Application.Views.ComposerAttachmentsBar.PathLabel);
            Assert.Same(path, mail.Session.FocusedElement);
            Assert.Equal(Broiler.Mail.Core.Messages.DraftSubmissionState.Editing, mail.Model.Composer.SubmissionState);

            Assert.True(mail.TryGoBack());
            Assert.Null(mail.Shell.ActiveDialog);
            Assert.True(await mail.SaveDraftAsync());
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    /// <summary>
    /// docs/ui-ux-roadmap.md (UX-03.2, UX-03.3, UX-03.5 and the UX-M2 exit gate) describes Android's file features from
    /// what CompositionRoot wires. It cannot be compiled here (it needs the Android Keystore), so its optional services
    /// are read from the source: adding one changes what Android has, and those docs must change with it.
    /// </summary>
    [Fact]
    public void AndroidCompositionRootWiresOnlyTheAttachmentReader()
    {
        string source = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "src", "Broiler.Mail.Android", "CompositionRoot.cs"));
        int start = source.IndexOf("new MailApplication(", StringComparison.Ordinal);
        Assert.True(start >= 0, "CompositionRoot no longer builds MailApplication.");
        int end = source.IndexOf(");", start, StringComparison.Ordinal);
        string call = string.Join('\n', source[start..end].Split('\n').Select(line => line.Split("//")[0]));
        var named = System.Text.RegularExpressions.Regex.Matches(call, @"\b([a-z][A-Za-z]*)\s*:(?!:)").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(["attachmentReader"], named);
    }

    /// <summary>
    /// What the Android composition actually has (CompositionRoot: the core services plus the attachment reader, no
    /// storage, saved-original store or cache): only Compose's typed attachment path, no Saved originals, no .eml
    /// open or export and no Settings → Storage, and attachments it can list and forward but not save.
    /// </summary>
    [Fact]
    public async Task AndroidHasOnlyTheComposerTypedPathAndNoProfileFileFeatures()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore();
        // The same services as CompositionRoot.Create, with test credentials in place of the Keystore store.
        var application = new MailApplication(new JsonAccountStore(directory.File("accounts.json")),
            new JsonSettingsStore(directory.File("settings.json")), new TestMailReceiver(), new SmtpMailSender(credentials), credentials,
            new JsonDraftStore(directory.File("drafts.json")),
            attachmentReader: new Broiler.Mail.Infrastructure.Mime.MessageAttachmentReader(null, null));
        await application.Accounts.SaveAsync(TestDirectory.Profile());
        await application.InitializeAsync();
        var posts = new Queue<Action>();
        using var mail = new MailSession(application, new Host(), posts.Enqueue);
        try
        {
            // Received attachments: listed and forwardable through the reader; nothing can save them on this device.
            Assert.NotNull(mail.Model.Inbox.AttachmentReader);
            Assert.NotNull(mail.Model.Composer.AttachmentReader);
            Assert.False(mail.Shell.Files.Capabilities.HasFlag(Broiler.Mail.Core.Services.MailFilePickerCapabilities.SaveFile));

            // No saved-original store: no Original message section, no Saved originals, no .eml open or export.
            Assert.Null(mail.Model.Inbox.SourceExports);
            Assert.False(mail.Model.Inbox.SupportsSourceExport);
            Assert.False(mail.Shell.CanOpenLocalOriginal);
            Assert.Null(mail.Model.Inbox.LocalCache);

            // No profile storage: Settings has no Storage section, so no backup or restore fields, typed or picked.
            Assert.Null(mail.Model.Settings.Storage);
            mail.Shell.ShowView("settings");
            while (posts.TryDequeue(out var post)) post();
            mail.RenderFrame();
            var settings = mail.Shell.GetContent("settings");
            Assert.DoesNotContain(Descendants(settings).OfType<Broiler.UI.Label.Standard.StandardLabel>(),
                label => label.Text?.Contains("backup", StringComparison.OrdinalIgnoreCase) == true || label.Text == "Local mail storage");
            Assert.DoesNotContain(Descendants(settings).OfType<Broiler.UI.Button.Standard.StandardButton>(),
                button => button.Text is "Create verified backup" or "Restore backup" or "Change backup location…");
            Assert.True(mail.TryGoBack());

            // The one typed path Android keeps: Compose's attachment path row.
            Assert.True(mail.Model.Composer.StartNew());
            mail.Shell.ShowView("compose");
            while (posts.TryDequeue(out var post)) post();
            mail.RenderFrame();
            var compose = mail.Shell.GetContent("compose");
            Assert.Contains(Descendants(compose).OfType<Broiler.UI.Edit.Standard.StandardEdit>(),
                edit => edit.AccessibleName == Broiler.Mail.Application.Views.ComposerAttachmentsBar.PathLabel);
            Assert.True(await mail.SaveDraftAsync());
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private static MailApplication CreateApplication(TestDirectory directory, TestMailReceiver? receiver = null, Broiler.Mail.Core.Services.IMailCache? cache = null)
    {
        var credentials = new TestCredentialStore();
        return new(new JsonAccountStore(directory.File("accounts.json")),
            new JsonSettingsStore(directory.File("settings.json")), receiver ?? new TestMailReceiver(),
            new SmtpMailSender(credentials), credentials, new JsonDraftStore(directory.File("drafts.json")), cache: cache);
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(360, 640);
        public double Scale => 3;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
