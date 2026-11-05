using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OfflineHeaderViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task HeaderControlsAreExplicitAndCancellationHasVisibleFocusAndCorrectRetry(double scale)
    {
        using var f=await OfflineHeaderTests.Fixture.Create();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<MailInboxPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Receiver.Override=(_,_,_)=>{entered.TrySetResult();return release.Task;};
        using var model=await f.Model();using var root=new InboxView(model).CreateContent();using var session=new StandardUiSessionBuilder().Build(new Host());session.AddRoot(root);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(root,theme);session.RenderFrame();
        var section=All(root).OfType<FormSection>().Single(item=>item.Content.AccessibleName=="Offline-folder batch controls");Assert.False(section.IsExpanded);section.Toggle!.Click();session.RenderFrame();
        Assert.InRange(All(root).OfType<BoundedScrollArea>().Single(area=>All(area).Contains(section)).Bounds.Height,1,168);
        var receive=All(root).OfType<StandardButton>().Single(button=>button.Text=="Receive selected-folder headers");var restart=All(root).OfType<StandardButton>().Single(button=>button.Text=="Restart selected-folder headers");var show=All(root).OfType<StandardButton>().Single(button=>button.Text=="Show saved headers");var cancel=All(root).OfType<StandardButton>().Single(button=>button.Text=="Cancel");
        Assert.True(receive.IsEnabled);Assert.False(receive.IsDefault);Assert.False(restart.IsEnabled);Assert.True(show.IsEnabled);Assert.Empty(f.Receiver.Calls);
        session.SetFocus(receive);var task=model.ReceiveOfflineHeadersAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));session.RenderFrame();Assert.Same(cancel,session.FocusedElement);Assert.True(cancel.IsEnabled);Assert.InRange(cancel.Bounds.Bottom,1,480);
        cancel.Click();release.SetResult(OfflineHeaderTests.Page(f.Account,"INBOX",101,null));await task;session.RenderFrame();Assert.True(model.ProblemIsOfflineHeaderReceive);
        Assert.Contains(All(root).OfType<StandardButton>(),button=>button.Text=="Retry selected-folder headers" && button.IsEnabled);Assert.True(restart.IsEnabled);Assert.Null(await f.Cache.GetLatestMailboxAsync(f.Account.Id,"INBOX"));
        restart.Click();Assert.False(model.HasOfflineHeaderWalk);Assert.Equal("Receive selected-folder headers",receive.Text);Assert.Single(f.Receiver.Calls);
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
