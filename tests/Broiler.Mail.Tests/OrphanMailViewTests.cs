using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OrphanMailViewTests
{
    [Theory]
    [InlineData(1d,false)]
    [InlineData(2d,false)]
    [InlineData(1d,true)]
    [InlineData(2d,true)]
    public async Task OrphanControlsWorkWithoutConfiguredAccountsAndUseKeepFirstConfirmation(double scale,bool noAccounts)
    {
        using var f=await OrphanMailTests.Fixture.Create(noAccounts);var model=f.Model.Accounts!;
        using var root=new AccountSelectionView(model,()=>{}).CreateContent();using var session=new StandardUiSessionBuilder().Build(new Host());session.AddRoot(root);
        var theme=StandardThemeTokens.Light.WithTextScale(scale);StandardControlPaint.SetSessionTheme(session,theme);StandardThemeController.ApplyToSubtree(root,theme);session.RenderFrame();
        var section=All(root).OfType<FormSection>().Single(item=>item.Content.AccessibleName=="Removed-account mail controls");Assert.False(section.IsExpanded);section.Toggle!.Click();session.RenderFrame();
        Assert.InRange(root.DesiredSize.Height,1,192);Assert.Empty(model.OrphanMail);
        StandardButton Button(string text)=>All(root).OfType<StandardButton>().Single(button=>button.Text==text);
        var clear=Button("Clear selected removed-account mail…");Assert.False(clear.IsEnabled);await model.FindOrphanMailAsync();session.RenderFrame();
        var picker=All(root).OfType<StandardComboBox>().Single(item=>item.AccessibleName=="Removed account ID and downloaded-mail counts");Assert.Equal(-1,picker.SelectedIndex);Assert.False(clear.IsEnabled);
        picker.SelectedIndex=model.OrphanMail.ToList().FindIndex(row=>row.AccountId==f.Orphan.Id);Assert.True(clear.IsEnabled);clear.Click();session.RenderFrame();
        var keep=Button("Keep removed-account mail");Assert.Same(keep,session.FocusedElement);Assert.False(Button("Back up and clear removed-account mail").IsDefault);Assert.False(model.CanSwitch);
        keep.Click();Assert.Null(model.PendingOrphanCleanup);clear.Click();Assert.NotNull(model.PendingOrphanCleanup);section.Toggle!.Click();Assert.Null(model.PendingOrphanCleanup);Assert.False(section.IsExpanded);Assert.Equal(2,(await f.Storage.GetAccountDownloadedMailInfoAsync(f.Orphan.Id)).HeaderCount);
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Host:IUiHost
    {public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}}
}
