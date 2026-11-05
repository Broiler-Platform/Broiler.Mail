using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class SavedDraftsViewTests
{
    [Theory]
    [InlineData(640, 480)]
    [InlineData(1100, 720)]
    public async Task LibraryDisclosureKeepsBodyUsableAndRemovalRequiresKeepOrConfirm(int width, int height)
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var drafts = new JsonDraftStore(directory.File("drafts.json"));
        using var model = new ComposerViewModel(drafts); model.SetAccount(account); model.StartNew(); model.Edit("a@example.test", "", "", "First fixture", "First body"); await model.ParkDraftAsync();
        model.StartNew(); model.Edit("b@example.test", "", "", "Open fixture", "Open body"); await model.SaveAsync();
        using var inbox = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher()); using var surface = new ComposerView(model, inbox).CreateContent();
        using var session = new StandardUiSessionBuilder().Build(new Host(width, height)); session.AddRoot(surface); session.RenderFrame();
        var section = All(surface).OfType<FormSection>().Single(item => item.Toggle?.Text.Contains("Saved drafts", StringComparison.Ordinal) == true);
        Assert.False(section.IsExpanded); section.Toggle!.Click(); session.RenderFrame();
        Assert.True(All(surface).OfType<StandardRichEdit>().Single().Bounds.Height >= 120);
        var picker = All(surface).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Saved drafts for selected account"); picker.SelectedIndex = 0;
        var request = All(surface).OfType<StandardButton>().Single(item => item.Text == "Remove selected saved draft…"); request.Click(); Assert.NotNull(model.SavedDraftRemoval);
        var confirm = All(surface).OfType<StandardButton>().Single(item => item.Text == "Confirm permanent local draft removal"); Assert.False(confirm.IsDefault); Assert.True(confirm.IsEnabled);
        All(surface).OfType<StandardButton>().Single(item => item.Text == "Keep saved draft").Click(); Assert.Null(model.SavedDraftRemoval); Assert.Single(model.SavedDrafts);
        Assert.Equal("Open body", model.PlainText); Assert.Single((await drafts.LoadAsync()).SavedDrafts!);
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var row in All(child)) yield return row; }
    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
