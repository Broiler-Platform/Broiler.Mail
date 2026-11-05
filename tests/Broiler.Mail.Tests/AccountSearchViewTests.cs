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
        var list=All(root).OfType<StandardListView>().Single();Assert.Contains(list.GetSemanticNode().Children,node=>node.Name.Contains("Folder:"));Assert.True(list.Bounds.Height>=40);
        var frame=All(root).OfType<BoundedScrollArea>().Single(area=>area.Scroll.AccessibleName=="Saved search controls");Assert.InRange(frame.Bounds.Height,1,168);
        scope.SelectedIndex=0;Assert.True(model.IsAccountSearchActive);Assert.Contains("all saved folders",model.SearchNotice);
        await model.SelectAsync(model.Messages.Single(row=>row.Key.MailboxId=="Archive" && row.Key.Uid==1).Key);session.RenderFrame();Assert.Equal("needle Archive",model.Body!.PlainText);
        Assert.Contains(All(root).OfType<StandardLabel>(),label=>label.Text.Contains("Folder Archive") && label.Text.Contains(f.Account.EmailAddress));Assert.Equal("INBOX",model.SelectedMailboxId);
        await model.ClearSearchAsync();Assert.False(model.IsAccountSearchActive);Assert.Equal(3,model.Messages.Count);
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task CompleteRetainedBodyModesAreReachableAndExplainCoverage(double scale)
    {
        using var f = await AccountSearchTests.Fixture.Create();
        var box = new Broiler.Mail.Core.Messages.MailboxKey(f.Account.Id, "INBOX", 7);
        await f.Cache.SaveBodyAsync(box, new(new(f.Account.Id, "INBOX", 7, 1), "Bounded preview") { RetainedBodyText = "retained tail phrase" });
        await f.Cache.RebuildSearchIndexAsync(); using var model = await f.Model();
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        All(root).OfType<StandardButton>().Single(button => button.Text == "Search saved mail").Click();
        var field = All(root).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == "Saved search header field");
        field.SelectedIndex = 10; model.SetSearch("tail phrase"); await model.SearchSavedHeadersAsync(); session.RenderFrame();
        Assert.Equal(Broiler.Mail.Core.Services.MailHeaderSearchField.IndexedRetainedBody, model.SearchField);
        Assert.Single(model.Messages); Assert.Contains("complete retained bodies only", model.SearchNotice);
        Assert.Contains(All(root).OfType<StandardLabel>(), label => label.Text.Contains("Older preview-only"));
        var sender = All(root).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(edit => edit.AccessibleName == "Saved sender header contains (optional)");
        sender.Text = "sender@example.test"; Assert.Equal(sender.Text, model.SearchSenderText); await model.SearchSavedHeadersAsync(); session.RenderFrame(); Assert.Single(model.Messages);
        Assert.Contains("Saved sender header contains", model.SearchNotice);
        field.SelectedIndex = 11; await model.SearchSavedHeadersAsync(); session.RenderFrame();
        Assert.Equal(Broiler.Mail.Core.Services.MailHeaderSearchField.IndexedHeadersOrRetainedBody, model.SearchField); Assert.Single(model.Messages);
        Assert.Contains("sender, subject or complete retained bodies", model.SearchNotice);
        var frame = All(root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Saved search controls"); Assert.InRange(frame.Bounds.Height, 1, 168);
    }
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
