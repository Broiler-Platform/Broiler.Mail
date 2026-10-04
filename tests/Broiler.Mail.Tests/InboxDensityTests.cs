using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class InboxDensityTests
{
    [Theory]
    [InlineData(1100, 1)]
    [InlineData(1100, 2)]
    [InlineData(640, 1)]
    [InlineData(640, 2)]
    public async Task SavedSpacingReflowsExistingRowsWithoutChangingTextOrReadingContext(int width, double textScale)
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var messages = Enumerable.Range(1, 100).Select(uid => new MailMessageSummary
        {
            Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "A sender <sender@example.test>", Subject = $"Message {uid}",
        }).ToArray();
        var receiver = new TestMailReceiver { Inbox = (_, _) => Task.FromResult(new MailInboxPage(messages, null)) };
        var store = new MemorySettingsStore();
        var dispatcher = new ImmediateUiDispatcher();
        var settings = new SettingsViewModel(store, dispatcher, new(), null);
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            settings, new(receiver, dispatcher), new ComposerViewModel(dispatcher: dispatcher));
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var shell = new MailShellView(model);
            var host = new Host(width, textScale);
            using var session = new StandardUiSessionBuilder().Build(host);
            session.AddRoot(shell.Window);
            using var appearance = new AppearanceController(session, settings, host);
            await model.Inbox.ReceiveAsync();
            await model.Inbox.SelectAsync(messages[30].Key);
            session.RenderFrame();
            var list = Descendants(shell.Window).OfType<StandardListView>().Single();
            list.ScrollIntoView(30);
            session.RenderFrame();
            int first = list.FirstVisibleIndex;
            var reader = Descendants(shell.Window).OfType<ScrollableMessageText>().Single();
            Assert.True(reader.Editor.SetEditorSelection(0, 4));
            var selection = reader.Editor.Selection;
            var body = model.Inbox.Body;
            var font = list.Font;
            double comfortableHeight = list.EffectiveItemHeight;

            var labels = Descendants(shell.Window).OfType<StandardLabel>().Where(label => label.Target is not null).ToList();
            var choice = Assert.IsType<StandardComboBox>(labels.Single(label => label.Text == "Inbox row spacing").Target);
            Assert.True(choice.PreferredSize.Height >= BTextMeasurer.GetLineHeight(choice.Font) + 12);
            Assert.True(choice.ItemHeight >= BTextMeasurer.GetLineHeight(choice.Font) + 8);
            var save = Descendants(shell.Window).OfType<StandardButton>().Single(button => button.Text == "Save settings");
            choice.SelectedIndex = 1;
            Assert.Equal(UiDensity.Comfortable, list.Density); // Editing is not saving.
            store.Fail = true;
            save.Click();
            Assert.StartsWith("Not saved:", settings.Status);
            Assert.Equal(UiDensity.Comfortable, list.Density);

            store.Fail = false;
            save.Click();
            session.RenderFrame();
            Assert.Equal(InboxDensity.Compact, store.Saved.InboxDensity);
            Assert.Equal(UiDensity.Compact, list.Density);
            Assert.True(list.EffectiveItemHeight < comfortableHeight);
            Assert.Equal(font, list.Font);
            Assert.Equal(messages[30].Key, model.Inbox.SelectedMessage?.Key);
            Assert.Same(body, model.Inbox.Body);
            Assert.Equal(selection, reader.Editor.Selection);
            if (!list.Bounds.IsEmpty)
                Assert.InRange(first, list.FirstVisibleIndex, list.FirstVisibleIndex + list.VisibleItemCount - 1);

            // Theme/text-size refresh must retain the separately saved row spacing.
            settings.Theme = AppTheme.Dark;
            await settings.SaveAsync();
            session.RenderFrame();
            Assert.Equal(UiDensity.Compact, list.Density);
            Assert.Equal(font.Size, list.Font.Size);
            choice.SelectedIndex = 0;
            save.Click();
            session.RenderFrame();
            Assert.Equal(comfortableHeight, list.EffectiveItemHeight);
            Assert.Same(body, model.Inbox.Body);
        }
        finally { StandardControlPaint.ApplyTheme(StandardThemeTokens.Light); }
    }

    [Fact]
    public async Task SpacingRoundTripsAndOldSettingsKeepTheComfortableDefault()
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"data":{"theme":"Dark","windowWidth":1100,"windowHeight":720,"inboxSplitterFraction":0.35}}""");
        var store = new JsonSettingsStore(path);
        Assert.Equal(InboxDensity.Comfortable, (await store.LoadAsync()).InboxDensity);
        var settings = new SettingsViewModel(store, new ImmediateUiDispatcher(), await store.LoadAsync(), null)
        { InboxDensity = InboxDensity.Compact };
        await settings.SaveAsync();
        // Background layout writes also retain the choice.
        await settings.RememberLayoutAsync(null, 0.4);
        var loaded = await new JsonSettingsStore(path).LoadAsync();
        Assert.Equal(InboxDensity.Compact, loaded.InboxDensity);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Contains("\"inboxDensity\": \"Compact\"", await File.ReadAllTextAsync(path));
        using var inbox = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher());
        using var content = new InboxView(inbox, settings: new(store, new ImmediateUiDispatcher(), loaded, null)).CreateContent();
        Assert.Equal(UiDensity.Compact, Descendants(content).OfType<StandardListView>().Single().Density);
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("1")]
    public async Task InvalidPersistedDensityIsRejectedWithoutOverwritingTheFile(string density)
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("settings.json");
        string json = "{\"schemaVersion\":1,\"data\":{\"inboxDensity\":" + density + "}}";
        await File.WriteAllTextAsync(path, json);
        var store = new JsonSettingsStore(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new()));
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task UndefinedChoiceDoesNotReachTheStore()
    {
        var store = new MemorySettingsStore();
        var settings = new SettingsViewModel(store, new ImmediateUiDispatcher(), new(), null)
        { InboxDensity = (InboxDensity)99 };
        await settings.SaveAsync();
        Assert.Equal("InboxDensity", settings.ValidationField);
        Assert.Equal(InboxDensity.Comfortable, store.Saved.InboxDensity);
        Assert.Equal(InboxDensity.Comfortable, settings.Settings.InboxDensity);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root) =>
        new[] { root }.Concat(root.Children.SelectMany(Descendants));

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public bool Fail { get; set; }
        public ApplicationSettings Saved { get; private set; } = new();
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Fixture save failure.");
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Host(int width, double textScale) : IUiHost, IUiSystemSettingsHost
    {
        public UiSystemSettings Settings => UiSystemSettings.Default with { TextScale = textScale };
        public event EventHandler<UiSystemSettingsChangedEventArgs>? SettingsChanged { add { } remove { } }
        public BSize ViewportSize => new(width, 480);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
