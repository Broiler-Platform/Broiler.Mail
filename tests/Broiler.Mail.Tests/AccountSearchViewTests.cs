using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class AccountSearchViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ScopeIsExplicitAndRowsAndReaderExposeFolderWithoutChangingMessages(double scale)
    {
        using var f=await AccountSearchTests.Fixture.Create();using var model=await f.Model();using var root=new InboxView(model).CreateContent();using var session=new StandardUiSessionBuilder().Build(new Host());session.AddRoot(root);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(root,theme);session.RenderFrame();
        var scope=All(root).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Saved search folder scope");Assert.Equal(0,scope.SelectedIndex);All(root).OfType<StandardButton>().Single(button=>button.Text=="Search saved mail").Click();session.RenderFrame();
        scope.SelectedIndex=1;Assert.False(model.IsSearchActive);model.SetSearch("needle");await model.SearchSavedHeadersAsync();session.RenderFrame();Assert.True(model.IsAccountSearchActive);Assert.Equal(18,model.Messages.Count);Assert.All(model.Messages,row=>Assert.Equal("needle",row.Subject));
        var list=All(root).OfType<StandardListView>().Single();Assert.Contains(list.GetSemanticNode().Children,node=>node.Name.Contains("Folder:"));Assert.True(list.Bounds.Height>=40,$"The list is {list.Bounds.Height} high.");
        var frame=All(root).OfType<BoundedScrollArea>().Single(area=>area.Scroll.AccessibleName=="Saved search controls");Assert.InRange(frame.Bounds.Height,1,168);
        // The header and its results strip sit at the top of the list pane, above the list, not over the whole inbox.
        var strip=All(root).OfType<Broiler.UI.Toolbar.Standard.StandardToolbar>().Single(panel=>panel.AccessibleName=="Saved search results");
        Assert.True(frame.IsDescendantOf(list.Parent!));Assert.True(strip.IsDescendantOf(list.Parent!));Assert.Equal(UiVisibility.Visible,strip.Visibility);
        Assert.True(strip.Bounds.Bottom<=list.Bounds.Top+0.5);Assert.Contains("in all folders",model.SearchSummary);
        scope.SelectedIndex=0;Assert.True(model.IsAccountSearchActive);Assert.Contains("all saved folders",model.SearchNotice);
        await model.SelectAsync(model.Messages.Single(row=>row.Key.MailboxId=="Archive" && row.Key.Uid==1).Key);session.RenderFrame();Assert.Equal("needle Archive",model.Body!.PlainText);
        Assert.Contains(All(root).OfType<StandardLabel>(),label=>label.Text.Contains("Folder Archive") && label.Text.Contains(f.Account.EmailAddress));Assert.Equal("INBOX",model.SelectedMailboxId);
        await model.ClearSearchAsync();Assert.False(model.IsAccountSearchActive);Assert.Equal(3,model.Messages.Count);
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task SearchScopesReachRetainedBodiesAndExplainCoverage(double scale)
    {
        using var f = await AccountSearchTests.Fixture.Create();
        var box = new Broiler.Mail.Core.Messages.MailboxKey(f.Account.Id, "INBOX", 7);
        await f.Cache.SaveBodyAsync(box, new(new(f.Account.Id, "INBOX", 7, 1), "Bounded preview") { RetainedBodyText = "retained tail phrase" });
        await f.Cache.RebuildSearchIndexAsync(); using var model = await f.Model();
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        All(root).OfType<StandardButton>().Single(button => button.Text == "Search saved mail").Click();
        var field = All(root).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == "Search in");
        Assert.Equal(0, field.SelectedIndex); Assert.Equal(Broiler.Mail.Core.Services.MailSearchScope.Everywhere, model.SearchScope);
        field.SelectedIndex = 2; model.SetSearch("tail phrase"); await model.SearchSavedHeadersAsync(); session.RenderFrame();
        Assert.Equal(Broiler.Mail.Core.Services.MailSearchScope.MessageBody, model.SearchScope);
        Assert.Equal(Broiler.Mail.Core.Services.MailSearchEngine.Indexed, model.SearchEngineUsed);
        Assert.Single(model.Messages); Assert.Contains("message text only", model.SearchNotice); Assert.Contains("beyond the preview", model.SearchNotice);
        Assert.Contains(All(root).OfType<StandardLabel>(), label => label.Text.Contains("otherwise the 32,000-character preview"));
        // The fixture's other messages have headers only, which the strip notes in a few words.
        var coverage = All(root).OfType<StandardLabel>().Single(label => label.AccessibleName == "Search coverage");
        Assert.Equal(UiVisibility.Visible, coverage.Visibility); Assert.Equal("Some headers only", coverage.Text); Assert.Contains("they were skipped", coverage.ToolTipText);
        var sender = All(root).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(edit => edit.AccessibleName == "Saved sender header contains (optional)");
        sender.Text = "sender@example.test"; Assert.Equal(sender.Text, model.SearchSenderText); await model.SearchSavedHeadersAsync(); session.RenderFrame(); Assert.Single(model.Messages);
        Assert.Contains("Saved sender header contains", model.SearchNotice);
        field.SelectedIndex = 0; await model.SearchSavedHeadersAsync(); session.RenderFrame();
        Assert.Equal(Broiler.Mail.Core.Services.MailSearchScope.Everywhere, model.SearchScope); Assert.Single(model.Messages);
        Assert.Contains("sender, subject or message text", model.SearchNotice);
        field.SelectedIndex = 1; await model.SearchSavedHeadersAsync(); session.RenderFrame();
        Assert.Empty(model.Messages); Assert.Equal(UiVisibility.Collapsed, coverage.Visibility);
        var frame = All(root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Saved search controls"); Assert.InRange(frame.Bounds.Height, 1, 168);
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task RefreshResultsAppearsInTheResultsStripOnlyAfterTheFoldersChange(double scale)
    {
        using var f = await AccountSearchTests.Fixture.Create();
        await f.Cache.SaveSummariesAsync(new(f.Account.Id, "Z", 7), Enumerable.Range(4, 500).Select(uid => AccountSearchTests.Row(f.Account.Id, "Z", 7, (uint)uid)).ToArray(), DateTimeOffset.UtcNow);
        using var model = await f.Model();
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        All(root).OfType<StandardButton>().Single(button => button.Text == "Search saved mail").Click();
        model.SetSearch("needle"); model.SetSearchAllSavedFolders(true); await model.SearchSavedHeadersAsync(); session.RenderFrame();
        var strip = All(root).OfType<Broiler.UI.Toolbar.Standard.StandardToolbar>().Single(panel => panel.AccessibleName == "Saved search results");
        var refresh = All(root).OfType<StandardButton>().Single(button => button.Text == "Refresh results");
        Assert.True(refresh.IsDescendantOf(strip)); Assert.Equal(UiVisibility.Collapsed, refresh.Visibility);
        await model.LoadNextSearchPageAsync(); session.RenderFrame(); Assert.Equal(UiVisibility.Collapsed, refresh.Visibility);
        await f.Cache.SaveSummariesAsync(new(f.Account.Id, "New", 7), [], DateTimeOffset.UtcNow);
        await model.LoadPreviousSearchPageAsync(); await model.LoadNextSearchPageAsync(); session.RenderFrame();
        Assert.Contains("Start a new search", model.ListProblem!.Text);
        Assert.Equal(UiVisibility.Visible, refresh.Visibility); Assert.True(refresh.IsEnabled);
        Assert.True(refresh.Bounds.Width > 0 && refresh.Bounds.Bottom <= strip.Bounds.Bottom + 0.5, $"Refresh results is at {refresh.Bounds}, the strip at {strip.Bounds}.");
        session.SetFocus(refresh); refresh.Click();
        for (int i = 0; i < 500 && (model.IsBusy || model.ListProblem is not null); i++) await Task.Delay(10);
        session.RenderFrame();
        Assert.Null(model.ListProblem); Assert.Equal(1, model.SearchPageNumber); Assert.Equal(500, model.Messages.Count);
        Assert.Equal(UiVisibility.Collapsed, refresh.Visibility);
        var list = All(root).OfType<StandardListView>().Single();
        Assert.Same(list, session.FocusedElement);
    }
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
