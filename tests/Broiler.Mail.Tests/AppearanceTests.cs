using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
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

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void SystemHighContrastUsesTheSystemsOwnPaletteWhereTheHostHasOne(AppTheme preference)
    {
        var system = DarkSystem with { ContrastPreference = UiContrastPreference.More, TextScale = 1.5, ReducedMotion = true };
        // A palette shaped like Hosting's: the selection's text differs from the window text.
        var palette = StandardThemeTokens.HighContrastDark with { Name = "HighContrastSystem", SelectionText = BColor.FromArgb(0xFF, 0x26, 0x3B, 0x50) };
        UiSystemSettings? asked = null;

        var tokens = AppearancePolicy.Resolve(preference, system, settings => { asked = settings; return palette; });

        // It wins over the saved choice, and is built for the whole of the system's settings (text scale and motion).
        Assert.Same(palette, tokens);
        Assert.Equal(system, asked);
        // A host that cannot read its colors keeps the theme's preset, as before.
        Assert.Equal(StandardThemeTokens.Select(system), AppearancePolicy.Resolve(preference, system, _ => null));
        // Without high contrast the system palette is not even asked for.
        Assert.Equal(AppearancePolicy.Resolve(preference, LightSystem),
            AppearancePolicy.Resolve(preference, LightSystem, _ => throw new InvalidOperationException("Asked without high contrast.")));
    }

    [Fact]
    public void TheLiveShellTakesTheSystemContrastPaletteAndFollowsItsColors()
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
        shell.Attach(session);
        try
        {
            var first = StandardThemeTokens.HighContrastDark with { Name = "HighContrastSystem", SelectionText = BColor.FromArgb(0xFF, 0x26, 0x3B, 0x50) };
            var system = first;
            using var appearance = new AppearanceController(session, model.Settings, host, _ => system);
            Assert.Equal(StandardThemeTokens.Light, appearance.Current);

            host.Change(LightSystem with { ContrastPreference = UiContrastPreference.More });
            Assert.Same(first, appearance.Current);
            Assert.Same(first, StandardControlPaint.GetTheme(session));
            var list = Descendants(shell.Window).OfType<Broiler.UI.ListView.Standard.StandardListView>().Single();
            Assert.Equal(first.SelectionText, list.SelectedForeground);

            // Another contrast theme changes only the colors; the host asks for the palette again.
            var second = first with { Surface = BColor.FromArgb(0xFF, 0x2D, 0x32, 0x36), SelectionText = BColor.FromArgb(0xFF, 0x21, 0x2D, 0x3B) };
            system = second;
            appearance.Apply();
            Assert.Same(second, appearance.Current);
            Assert.Equal(second.SelectionText, list.SelectedForeground);
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    /// <summary>
    /// A button strokes its focus ring inside the fill it draws in its state: the accent on a default button, the
    /// state fill while the pointer rests on a secondary one, the pressed fills while Space holds either down. Where
    /// the palette's ring does not stand out from that fill (3:1), Broiler.UI draws it in the label color drawn on it
    /// (ADR 0032), so keyboard focus on Save account (and Send, Save settings) and on Test connection stays visible in
    /// every state; on the window color a button keeps the palette's ring.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    [InlineData("SystemDusk")]
    [InlineData("SystemDesert")]
    [InlineData("RingStandsOut")]
    public void AKeyboardFocusedButtonShowsItsRingOnTheFillOfEachState(string name)
    {
        var palette = Palette(name);
        using var directory = new TestDirectory();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        var host = new Host(LightSystem with { ContrastPreference = UiContrastPreference.More });
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        using var shell = new MailShellView(model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);
        try
        {
            using var appearance = new AppearanceController(session, model.Settings, host, _ => palette);
            Assert.Same(palette, appearance.Current);
            shell.ShowView("account");
            var account = shell.GetContent("account");
            var save = Descendants(account).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == "Save account");
            var test = Descendants(account).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == "Test connection");
            Assert.True(save.IsDefault && save.IsEnabled && !test.IsDefault && test.IsEnabled);
            session.RenderFrame();

            foreach (var (button, fills, labels) in new[]
            {
                (save, new[] { palette.Accent, palette.AccentHover, palette.AccentPressed }, new[] { palette.OnAccent, palette.OnAccent, palette.OnAccent }),
                (test, new[] { palette.Surface, palette.StateFill, palette.SurfaceDisabled }, new[] { palette.Text, palette.StateText, palette.Text }),
            })
            {
                // Pressed last: the test never lets go of Space, which would save the account or start a test.
                foreach (var state in new[] { ButtonState.Rest, ButtonState.Hovered, ButtonState.Pressed })
                {
                    var drawn = Drawn(session, button, state);
                    string where = $"{name}, {button.Text}, {state}";
                    Assert.True(drawn.Fill == fills[(int)state], $"{where}: fill {drawn.Fill}, expected {fills[(int)state]}");
                    Assert.True(drawn.Label == labels[(int)state], $"{where}: label {drawn.Label}, expected {labels[(int)state]}");
                    // The palette's ring where it stands out from the fill, the label's color where it does not.
                    var expected = StandardContrast.Ratio(palette.FocusRing, drawn.Fill) >= StandardContrast.AaLargeOrUi ? palette.FocusRing : drawn.Label;
                    Assert.True(drawn.Ring == expected, $"{where}: ring {drawn.Ring}, expected {expected}");
                    Assert.True(StandardContrast.Ratio(drawn.Ring, drawn.Fill) >= StandardContrast.AaLargeOrUi, $"{where}: ring {drawn.Ring} on {drawn.Fill}");
                    if (state == ButtonState.Rest)
                        // A default button at rest takes its label's color unless the palette's ring stands out on the
                        // accent; a button on the window color keeps the palette's ring.
                        Assert.Equal(button == test || name == "RingStandsOut" ? palette.FocusRing : palette.OnAccent, drawn.Ring);
                }
            }
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    private enum ButtonState { Rest, Hovered, Pressed }

    /// <summary>
    /// Puts <paramref name="button"/> into <paramref name="state"/> with its keyboard focus shown, as a user does: a key
    /// press shows the focus (hovered: while the pointer rests on the button), or Space is held down. Returns the fill,
    /// label and focus ring it then draws, read from a rendered frame.
    /// </summary>
    private static (BColor Fill, BColor Label, BColor Ring) Drawn(UiSession session, Broiler.UI.Button.Standard.StandardButton button, ButtonState state)
    {
        if (state == ButtonState.Hovered)
            session.DispatchInput(UiInputEvent.FromMouseMove(new MouseMoveEvent(Header(),
                InputPoint.ClientDeviceIndependentPixels(button.Bounds.Left + (button.Bounds.Width / 2), button.Bounds.Top + (button.Bounds.Height / 2)),
                MouseButtons.None, InputEventSource.Synthetic)));
        session.SetFocus(button);
        // Shift does nothing on a button but counts as keyboard use; Space holds the button down until it is let go.
        int key = state == ButtonState.Pressed ? 0x20 : 0x10;
        session.DispatchInput(UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(Header(), KeyboardKey.FromName("VirtualKey:" + key),
            KeyboardKeyTransition.Down, KeyboardModifierState.None, key, 0, 0, false, false, Source: InputEventSource.Synthetic)));
        Assert.True(session.IsFocusVisible);
        Assert.Equal(state == ButtonState.Pressed, button.IsPressed);

        var commands = session.RenderFrame().Commands.ToArray();
        var fill = commands.OfType<BRenderCommand.FillRoundedRect>().Last(command => command.Rect == button.Bounds).Color;
        var label = commands.OfType<BRenderCommand.DrawText>().Last(command => command.Text.Text == button.Text).Text.Color;
        var ring = commands.OfType<BRenderCommand.StrokeRoundedRect>().Single(command => command.Rect == StandardControlPaint.Inset(button.Bounds, 2)).Color;
        return (fill, label, ring);
    }

    private static InputEventHeader Header() => new(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);

    /// <summary>The palettes the button rings are checked in; the system ones are shaped like Hosting's.</summary>
    private static StandardThemeTokens Palette(string name) => name switch
    {
        "Light" => StandardThemeTokens.Light,
        "Dark" => StandardThemeTokens.Dark,
        "HighContrastLight" => StandardThemeTokens.HighContrastLight,
        "HighContrastDark" => StandardThemeTokens.HighContrastDark,
        // Windows' Dusk and Desert contrast themes as WindowsTheme.CreateHighContrastTheme maps them: the accent,
        // the states and the focus ring are all Highlight.
        "SystemDusk" => SystemPalette(StandardThemeTokens.HighContrastDark, BColor.FromArgb(0xFF, 0x2D, 0x32, 0x36), BColor.White,
            BColor.FromArgb(0xFF, 0xA1, 0xBF, 0xDE), BColor.FromArgb(0xFF, 0x21, 0x2D, 0x3B)),
        "SystemDesert" => SystemPalette(StandardThemeTokens.HighContrastLight, BColor.FromArgb(0xFF, 0xFF, 0xFA, 0xEF), BColor.FromArgb(0xFF, 0x3D, 0x3D, 0x3D),
            BColor.FromArgb(0xFF, 0x90, 0x39, 0x09), BColor.FromArgb(0xFF, 0xFF, 0xF5, 0xE3)),
        "RingStandsOut" => StandardThemeTokens.Light with { FocusRing = BColor.Black, AccentHover = StandardThemeTokens.Light.Accent, AccentPressed = StandardThemeTokens.Light.Accent },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static StandardThemeTokens SystemPalette(StandardThemeTokens preset, BColor window, BColor text, BColor highlight, BColor highlightText) => preset with
    {
        Name = "HighContrastSystem", Surface = window, SurfaceAlt = window, SurfaceDisabled = window, Text = text, TextMuted = text,
        Border = text, BorderStrong = text, Accent = highlight, AccentHover = highlight, AccentPressed = highlight, AccentSoft = highlight,
        OnAccent = highlightText, SelectionText = highlightText, SelectionTextMuted = highlightText,
        StateFill = highlight, StateText = highlightText, FocusRing = highlight,
    };

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
        shell.Attach(session);
        try
        {
            using var appearance = new AppearanceController(session, model.Settings, host);
            Assert.False(appearance.Current!.IsDark);
            Assert.True(model.Composer.StartNew());
            dispatcher.Drain();
            shell.ShowView("compose");
            session.RenderFrame();
            var body = Descendants(shell.GetContent("compose")).OfType<StandardRichEdit>().Single();
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
        shell.Attach(session);
        try
        {
            using var appearance = new AppearanceController(session, model.Settings, host);
            session.RenderFrame();
            var inbox = shell.GetContent("inbox");
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
        shell.Attach(session);
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
            shell.ShowView("compose");
            AssertStill("switching tabs");
            var compose = shell.GetContent("compose");
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
