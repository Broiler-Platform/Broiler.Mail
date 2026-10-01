using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI.Forms.Standard;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using MailKit.Net.Imap;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class Version1AcceptanceTests
{
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task FreshProfileRestartReceiveReadAndRecoverWithoutChangingServerFlags(TransportSecurity security)
    {
        using var directory = new TestDirectory();
        await using var server = new LocalImapServer(security);
        server.Messages.AddRange([
            new(1, "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nPlain fixture body."),
            new(2, "MIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>HTML &amp; text</p><img src='https://example.test/tracker'>"),
        ]);
        var credentials = new TestCredentialStore();
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        var settings = new JsonSettingsStore(directory.File("settings.json"));
        var receiver = new ImapMailReceiver(credentials, () => new ImapClient
        { ServerCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString() }, TimeSpan.FromSeconds(5));
        var profile = new AccountProfileViewModel(accounts, credentials, receiver, new ImmediateUiDispatcher(), null, null)
        {
            DisplayName = "Acceptance test", EmailAddress = "test@example.test", Host = "127.0.0.1",
            Port = server.Port.ToString(), UserName = "test", Security = security,
        };
        await profile.SaveAsync();
        Assert.NotNull(profile.Profile);
        await profile.SavePasswordAsync(LocalImapServer.Password);
        await settings.SaveAsync(new() { Theme = AppTheme.Dark, WindowWidth = 960, WindowHeight = 640 });

        // New composition reads the saved identity/settings, as on process restart.
        var restarted = new MailApplication(new JsonAccountStore(directory.File("accounts.json")), new JsonSettingsStore(directory.File("settings.json")), receiver, new SmtpMailSender(credentials), credentials);
        await restarted.InitializeAsync();
        Assert.Equal(profile.Profile, restarted.LoadedAccount);
        Assert.Equal(AppTheme.Dark, restarted.LoadedSettings.Theme);
        Assert.Equal(960, restarted.LoadedSettings.WindowWidth);
        await restarted.Receiver.TestConnectionAsync(restarted.LoadedAccount!);
        using var inbox = new InboxViewModel(restarted.Receiver, new ImmediateUiDispatcher());
        inbox.SetAccount(restarted.LoadedAccount);
        await inbox.ReceiveAsync();
        Assert.Equal(2, inbox.Messages.Count);
        await inbox.SelectAsync(inbox.Messages[0].Key);
        Assert.True(inbox.Body!.IsHtmlFallback);
        Assert.Contains("HTML & text", inbox.Body.PlainText);
        await inbox.SelectAsync(inbox.Messages[1].Key);
        Assert.Contains("Plain fixture body", inbox.Body!.PlainText);
        await inbox.ReceiveAsync();
        Assert.Equal(2, inbox.Messages.DistinctBy(message => message.Key).Count());
        Assert.All(inbox.Messages, message => Assert.False(message.IsRead));

        // A missing password gives a recoverable error, then a new receive succeeds in the same session.
        var key = CredentialKey.For(restarted.LoadedAccount!, MailProtocol.Imap);
        await credentials.DeleteAsync(key);
        await inbox.ReceiveAsync();
        Assert.Contains("No password", inbox.Status);
        Assert.Equal(2, inbox.Messages.Count);
        await credentials.WriteAsync(key, LocalImapServer.Password);
        await inbox.ReceiveAsync();
        Assert.Equal(2, inbox.Messages.Count);
        Assert.True(inbox.CanReceive);
        Assert.DoesNotContain(server.Commands, command => command is "SELECT" or "STORE" or "EXPUNGE");
        Assert.DoesNotContain(LocalImapServer.Password, await File.ReadAllTextAsync(directory.File("accounts.json")));
    }

    [Theory]
    [InlineData(640, 480, false, false)]
    [InlineData(1100, 720, true, false)]
    [InlineData(640, 480, false, true)]
    [InlineData(1100, 720, true, true)]
    public async Task FullShellUsesAvailableWidthAndKeepsKeyboardFocusVisible(int width, int height, bool dark, bool smtp)
    {
        // These static theme tokens are shared with other UI tests, so use a collection for all UI tests.
        StandardControlPaint.ApplyTheme(dark ? StandardThemeTokens.Dark : StandardThemeTokens.Light);
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        if (smtp) account = account with
        {
            OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "outgoing", Security = TransportSecurity.StartTls },
        };
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        await accounts.SaveAsync(account);
        var credentials = new TestCredentialStore();
        var receiver = new TestMailReceiver();
        var app = new MailApplication(accounts, new JsonSettingsStore(directory.File("settings.json")), receiver, new SmtpMailSender(credentials), credentials);
        await app.InitializeAsync();
        using var shell = app.CreateShell();
        using var session = new StandardUiSessionBuilder().Build(new Host(width, height));
        session.AddRoot(shell.Window);
        var keyboard = shell.CreateKeyboardNavigation(session);
        var text = Descendants(shell.Window).OfType<ScrollableMessageText>().Single();
        text.Text = string.Join("\n", Enumerable.Repeat("A readable message with words & symbols.", 40));
        session.RenderFrame();
        var label = Descendants(text).OfType<StandardRichEdit>().Single();
        Assert.True(label.Bounds.Width > 250, $"Reader collapsed to {label.Bounds.Width} DIP.");
        Assert.True(label.Bounds.Height < 3000, "Text was wrapped into a narrow column.");

        shell.Navigation.SelectTab("account");
        session.RenderFrame();
        var edits = Descendants(shell.Navigation.SelectedTab!.Content!).OfType<StandardEdit>().ToArray();
        Assert.True(edits[0].Bounds.Width > width - 40);
        session.SetFocus(shell.Navigation);
        Assert.True(keyboard.Handle(Key(9)));
        Assert.Same(edits[0], session.FocusedElement);
        Assert.True(keyboard.Handle(Key(9, shift: true)));
        Assert.Same(shell.Navigation, session.FocusedElement);
        if (smtp)
        {
            var smtpHost = Descendants(shell.Navigation.SelectedTab.Content!).OfType<StandardLabel>()
                .Single(item => item.Text == "SMTP server (hostname only)").Target;
            for (int attempt = 0; attempt < 20 && session.FocusedElement != smtpHost; attempt++)
                Assert.True(keyboard.Handle(Key(9)));
            Assert.Same(smtpHost, session.FocusedElement);
            var formScroll = Descendants(shell.Navigation.SelectedTab.Content!).OfType<FormSurface>().Single().Content.Scroll;
            Assert.InRange(smtpHost!.Bounds.Top, formScroll.ContentBounds.Top - 1, formScroll.ContentBounds.Bottom);
            Assert.InRange(smtpHost.Bounds.Bottom, formScroll.ContentBounds.Top, formScroll.ContentBounds.Bottom + 1);
            session.SetFocus(shell.Navigation);
        }
        // Backward traversal reaches the persistent action bar, outside the scrolling fields.
        Assert.True(keyboard.Handle(Key(9, shift: true)));
        var button = Assert.IsType<StandardButton>(session.FocusedElement);
        Assert.Equal("Test connection", button.Text);
        var scroll = Descendants(shell.Navigation.SelectedTab.Content!).OfType<FormSurface>().Single().Content.Scroll;
        Assert.InRange(button.Bounds.Top, scroll.Bounds.Bottom, height);
        Assert.InRange(button.Bounds.Bottom, button.Bounds.Top, height);
        Assert.False(scroll.HasHorizontalScrollbar);
        Assert.True(keyboard.Handle(Key(0x33, control: true)));
        Assert.Equal("settings", shell.Navigation.SelectedTab!.Id);
        Assert.Same(shell.Navigation, session.FocusedElement);
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
    }

    [Fact]
    public async Task FirstRunOpensAccountAndPasswordFieldsCannotCopySecrets()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore();
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        var app = new MailApplication(accounts, new JsonSettingsStore(directory.File("settings.json")), new TestMailReceiver(), new SmtpMailSender(credentials), credentials);
        await app.InitializeAsync();
        using var shell = app.CreateShell();
        var host = new Host(1100, 720);
        using var session = new StandardUiSessionBuilder().Build(host);
        session.AddRoot(shell.Window);
        Assert.Equal("account", shell.Navigation.SelectedTab!.Id);
        Assert.All(Descendants(shell.Window).OfType<StandardEdit>().Where(edit => edit.IsPassword), edit => Assert.False(edit.IsEnabled));
        await accounts.SaveAsync(TestDirectory.Profile());
        await app.InitializeAsync();
        using var configured = app.CreateShell();
        session.AddRoot(configured.Window);
        configured.Navigation.SelectTab("account");
        var password = Descendants(configured.Window).OfType<StandardEdit>().Last(edit => edit.IsPassword);
        Assert.True(password.IsEnabled);
        password.Text = "synthetic-secret";
        password.SelectAll();
        session.SetFocus(password);
        session.DispatchInput(Key(0x43, control: true));
        Assert.Null(host.Clipboard);
        host.Clipboard = "synthetic-pasted-secret";
        session.DispatchInput(Key(0x56, control: true));
        Assert.Equal("synthetic-pasted-secret", password.Text);
    }

    private static UiInputEvent Key(int code, bool shift = false, bool control = false)
    {
#pragma warning disable CS0618
        return new StandardLegacyGraphicsInputAdapter("mail-acceptance").FromKey(new BKeyEventArgs(code, control, shift, false), KeyboardKeyTransition.Down);
#pragma warning restore CS0618
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var element in Descendants(child)) yield return element;
    }

    private sealed class Host(int width, int height) : IUiHost, IUiClipboardHost
    {
        public string? Clipboard { get; set; }
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
        public bool TryGetText(out string text) { text = Clipboard ?? ""; return Clipboard is not null; }
        public void SetText(string text) => Clipboard = text;
    }
}

[CollectionDefinition("UI theme", DisableParallelization = true)]
public sealed class UiThemeCollection;
