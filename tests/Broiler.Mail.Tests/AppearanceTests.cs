using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
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

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Host(UiSystemSettings settings) : IUiHost, IUiSystemSettingsHost
    {
        public UiSystemSettings Settings { get; private set; } = settings;
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
}
