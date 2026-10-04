using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-07: saved and system appearance changes apply to the running UI without a restart.</summary>
[Collection("UI theme")]
public sealed class AppearanceTests
{
    private static readonly UiSystemSettings LightSystem = UiSystemSettings.Default with { ColorScheme = UiColorScheme.Light, ContrastPreference = UiContrastPreference.NoPreference };
    private static readonly UiSystemSettings DarkSystem = LightSystem with { ColorScheme = UiColorScheme.Dark };

    [Theory]
    [InlineData(AppTheme.System, false, false)]
    [InlineData(AppTheme.System, true, true)]
    [InlineData(AppTheme.Light, true, false)]
    [InlineData(AppTheme.Dark, false, true)]
    public void ExplicitChoiceWinsOverTheSystemSchemeAndSystemFollowsIt(AppTheme preference, bool systemDark, bool expectDark)
    {
        var tokens = AppearancePolicy.Resolve(preference, systemDark ? DarkSystem : LightSystem);
        Assert.Equal(expectDark, tokens.IsDark);
        Assert.Equal(expectDark ? StandardThemeTokens.Dark.Text : StandardThemeTokens.Light.Text, tokens.Text);
    }

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void SystemHighContrastTakesPrecedenceAndReducedMotionIsKept(AppTheme preference)
    {
        var system = DarkSystem with { ContrastPreference = UiContrastPreference.More, ReducedMotion = true };
        var tokens = AppearancePolicy.Resolve(preference, system);
        Assert.Equal(StandardThemeTokens.Select(system), tokens);
        Assert.True(tokens.ReducedMotion);
        Assert.True(AppearancePolicy.Resolve(preference, LightSystem with { ReducedMotion = true }).ReducedMotion);
    }

    [Fact]
    public async Task SavedPreferenceAndSystemChangesRethemeTheLiveShellWithoutLosingText()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var host = new Host(LightSystem);
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        session.AddRoot(shell.Window);
        try
        {
            using var appearance = new AppearanceController(session, model.Settings, host);
            Assert.False(appearance.Current!.IsDark);
            Assert.True(model.Composer.StartNew());
            dispatcher.Drain();
            shell.Navigation.SelectTab("compose");
            session.RenderFrame();
            var body = Descendants(shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!).OfType<StandardRichEdit>().Single();
            body.SetPlainText("Text that must survive a theme change");
            dispatcher.Drain();
            Assert.True(body.SetEditorSelection(5, 9));
            var selection = body.Selection;
            int applied = 0;
            appearance.Applied += (_, _) => applied++;

            // System mode follows the operating system.
            host.Change(DarkSystem);
            Assert.True(appearance.Current.IsDark);
            Assert.Equal(StandardThemeTokens.Dark.Text, Descendants(shell.Window).OfType<StandardLabel>().First(label => label.Role == StandardLabelRole.Default).Foreground);
            var muted = Descendants(shell.Window).OfType<StandardLabel>().First(label => label.Role == StandardLabelRole.Muted);
            Assert.Equal(StandardLabel.GetRoleColor(StandardLabelRole.Muted, StandardThemeTokens.Dark), muted.Foreground);

            // An unsaved selection changes nothing; saving an explicit Light choice wins over the dark OS.
            model.Settings.Theme = AppTheme.Light;
            Assert.True(appearance.Current.IsDark);
            await model.Settings.SaveAsync();
            dispatcher.DrainUntil(() => !model.Settings.IsBusy);
            Assert.False(appearance.Current.IsDark);
            host.Change(DarkSystem with { ReducedMotion = true });
            Assert.False(appearance.Current.IsDark);
            Assert.True(appearance.Current.ReducedMotion);
            Assert.Equal(3, applied);

            session.RenderFrame();
            Assert.Equal("Text that must survive a theme change", body.GetPlainText());
            Assert.Equal(selection, body.Selection);
            Assert.Equal("Text that must survive a theme change", model.Composer.PlainText);
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Theory]
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void TheSystemTextSizeAppliesWhateverTheThemeChoice(AppTheme preference)
    {
        var tokens = AppearancePolicy.Resolve(preference, LightSystem with { TextScale = 1.5 });
        Assert.Equal(StandardThemeTokens.Light.FontBody.Size * 1.5, tokens.FontBody.Size, 2);
        Assert.Equal(StandardThemeTokens.Light.FontTitle.Size * 1.5, tokens.FontTitle.Size, 2);
        Assert.Equal(StandardThemeTokens.Light.FontBody.Size, AppearancePolicy.Resolve(preference, LightSystem with { TextScale = 0 }).FontBody.Size, 2);
    }

    [Fact]
    public void AChangedSystemTextSizeEnlargesTheLiveShellIncludingTheReaderSubject()
    {
        using var directory = new TestDirectory();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var host = new Host(LightSystem);
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        session.AddRoot(shell.Window);
        try
        {
            using var appearance = new AppearanceController(session, model.Settings, host);
            session.RenderFrame();
            var inbox = shell.Navigation.Tabs.Single(tab => tab.Id == "inbox").Content!;
            var receive = Descendants(inbox).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == "Receive mail");
            var subject = Descendants(inbox).OfType<StandardLabel>().Single(label => label.TextStyle == StandardTextStyle.Title);
            double receiveWidth = receive.DesiredSize.Width;

            host.Change(LightSystem with { TextScale = 1.5 });
            session.RenderFrame();

            Assert.Equal(StandardThemeTokens.Light.FontBody.Size * 1.5, receive.Font.Size, 2);
            Assert.Equal(StandardThemeTokens.Light.FontTitle.Size * 1.5, subject.Font.Size, 2);
            Assert.True(receive.DesiredSize.Width > receiveWidth, "Larger text must re-measure the controls.");
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    /// <summary>
    /// Reduced motion: Mail has no motion for the setting to remove. Even with animation allowed,
    /// selecting, disclosing, switching tabs, focusing an editor, and re-theming start no animation on
    /// the host, and nothing driven by the UI clock changes a still window. The system's request still
    /// reaches the theme the session's controls read, so a standard transition would be instant.
    /// </summary>
    /// <remarks>
    /// The one timed change in Mail is not motion: a success confirmation is removed 6 s after a save,
    /// on a <see cref="TimeProvider"/> timer. This test saves nothing, so that timer cannot change a frame.
    /// </remarks>
    [Fact]
    public async Task NothingInTheShellMovesOnItsOwnAndReducedMotionReachesTheSessionTheme()
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var messages = new[] { 3u, 2u, 1u }.Select(uid => new MailMessageSummary
        {
            Key = new(account.Id, "INBOX", 7, uid), Sender = "author@example.test", Subject = $"Message {uid}",
        }).ToArray();
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage(messages, null)),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Body text")),
        };
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var host = new Host(LightSystem);
        var clock = new UiClock();
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).WithClock(clock).Build(host);
        session.AddRoot(shell.Window);
        try
        {
            using var appearance = new AppearanceController(session, model.Settings, host);
            Assert.False(appearance.Current!.ReducedMotion);
            void AssertStill(string after)
            {
                dispatcher.Drain();
                var first = session.RenderFrame().Commands.ToArray();
                // Off any 1 s blink or 1.4 s sweep period, so a timed visual cannot line up again.
                clock.Elapsed += TimeSpan.FromMilliseconds(730);
                var later = session.RenderFrame().Commands.ToArray();
                Assert.True(first.SequenceEqual(later), $"The window changed on its own after {after}.");
                Assert.Equal(0, host.AnimationStarts);
            }

            await model.Inbox.ReceiveAsync();
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            await model.Inbox.SelectAsync(messages[1].Key);
            dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
            AssertStill("selecting a message");

            Assert.True(model.Composer.StartNew());
            shell.Navigation.SelectTab("compose");
            AssertStill("switching tabs");
            var compose = shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!;
            Descendants(compose).OfType<FormSection>().Single(section => section.Toggle is not null).Toggle!.Click();
            AssertStill("showing Cc and Bcc");
            session.SetFocus(Descendants(compose).OfType<StandardRichEdit>().Single());
            AssertStill("focusing the message body");
            host.Change(DarkSystem);
            AssertStill("changing to the dark theme");

            host.Change(DarkSystem with { ReducedMotion = true });
            var theme = StandardControlPaint.GetTheme(session);
            Assert.True(theme.ReducedMotion);
            Assert.Equal(TimeSpan.Zero, theme.AnimationDurationNormal);
            AssertStill("turning on reduced motion");
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Host(UiSystemSettings settings) : IUiHost, IUiSystemSettingsHost, IUiAnimationHost
    {
        public UiSystemSettings Settings { get; private set; } = settings;
        /// <summary>How often anything asked the host for animation frames.</summary>
        public int AnimationStarts { get; private set; }
        public void StartAnimation() => AnimationStarts++;
        public void StopAnimation() { }
        public event EventHandler<UiSystemSettingsChangedEventArgs>? SettingsChanged;
        public void Change(UiSystemSettings settings)
        {
            Settings = settings;
            SettingsChanged?.Invoke(this, new UiSystemSettingsChangedEventArgs(settings));
        }
        public BSize ViewportSize => new(1100, 720);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    /// <summary>The UI clock, moved only by the test.</summary>
    private sealed class UiClock : IUiClock
    {
        public TimeSpan Elapsed { get; set; }
        public UiTimestamp Now => new(Elapsed);
    }
}
