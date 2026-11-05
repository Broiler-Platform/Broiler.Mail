using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class AccountNavigationViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ShortcutsAndStartControlsStayLocalScopedAndBounded(double scale)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var cache = new SqliteMailCache(directory.File("mail.db"));
        foreach (string folder in new[] { "INBOX", "Archive" }) await cache.SaveSummariesAsync(new(account.Id, folder, 7), [new() { Key = new(account.Id, folder, 7, 1), Subject = folder, Sender = "sender@example.test" }], DateTimeOffset.UtcNow);
        var store = new JsonAccountNavigationStore(directory.File("account-navigation.json"));
        using var model = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher(), cache, navigation: store); model.SetAccount(account); await model.CacheLoadTask;
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        var section = All(root).OfType<FormSection>().Single(item => item.Content.AccessibleName == "Account folder navigation controls"); Assert.False(section.IsExpanded); section.Toggle!.Click(); session.RenderFrame();
        StandardButton Button(string text) => All(root).OfType<StandardButton>().Single(button => button.Text == text);
        var pin = Button("Pin current folder"); Assert.True(pin.IsEnabled);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); model.Changed += (_, _) => { if (model.PinnedFolders.Count == 1 && !model.IsSavingNavigation) changed.TrySetResult(); };
        pin.Click(); await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)); session.RenderFrame(); Assert.Equal("Unpin current folder", pin.Text);
        var choices = All(root).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == "Pinned folders for current account"); Assert.Equal(0, choices.SelectedIndex); Assert.True(Button("Open pinned folder").IsEnabled);
        Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask; await model.SetStartFolderAsync(); session.RenderFrame(); Assert.Equal("Archive", (await store.LoadAsync(account.Id)).StartMailboxId);
        Assert.Single(model.Messages); Button("Open pinned folder").Click(); await model.CacheLoadTask; Assert.Equal("INBOX", model.SelectedMailboxId);
        Assert.InRange(All(root).OfType<BoundedScrollArea>().Single(area => All(area).Contains(section)).Bounds.Height, 1, 168); Assert.False(Button("Reset account navigation").IsDefault);
        var other = TestDirectory.Profile(); model.SetAccount(other); await model.CacheLoadTask; session.RenderFrame(); Assert.Empty(model.PinnedFolders); Assert.False(Button("Open pinned folder").IsEnabled); Assert.Equal(-1, choices.SelectedIndex);
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640,480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
