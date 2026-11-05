using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class LocalOriginalViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task OpenImportAndReopenAreSeparateExplicitActionsWithReadOnlySource(double scale)
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("fixture.eml");
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("Subject: Local\r\n\r\n<script>plain source</script>"); await File.WriteAllBytesAsync(path, bytes);
        var account = TestDirectory.Profile() with { IsEnabled = false }; var store = new JsonLocalMessageOriginals(directory.Root);
        using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(view, theme); session.RenderFrame();
        Assert.Empty(view.Imports); Assert.Empty(view.PreviewText); Assert.All(All(view).OfType<StandardButton>(), button => Assert.False(button.IsDefault));
        await view.OpenFileAsync(path); queue.Drain(); session.RenderFrame(); Assert.Contains("<script>plain source</script>", view.PreviewText); Assert.Empty(await store.ListImportsAsync(account.Id));
        var import = All(view).OfType<StandardButton>().Single(button => button.Text == "Import opened original"); Assert.True(import.IsEnabled);
        await view.ImportOpenedAsync(); queue.Drain(); Assert.False(import.IsEnabled); Assert.Empty(view.PreviewText); Assert.Empty(view.Imports);
        await view.ListAsync(); queue.Drain(); var receipt = Assert.Single(view.Imports); Assert.Equal(account.Id, receipt.AccountId);
        Assert.Equal(-1, All(view).OfType<StandardComboBox>().Single().SelectedIndex); Assert.Empty(view.PreviewText);
        await view.ReadAsync(receipt); queue.Drain(); session.RenderFrame(); Assert.Contains("plain source", view.PreviewText); Assert.False(import.IsEnabled);
        Assert.Equal(bytes, (await store.ReadImportAsync(receipt)).Bytes); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        view.SetActive(false); Assert.Empty(view.Imports); Assert.Empty(view.PreviewText);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("queued")]
    [InlineData("path")]
    public async Task LateOpenCannotRepaintLostContext(string mode)
    {
        var store = new HeldStore(); using var view = new LocalOriginalsView(store); view.SetAccount(TestDirectory.Profile()); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var opening = view.OpenFileAsync("fixture.eml"); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode == "cancel") view.Cancel(); else if (mode == "account") view.SetAccount(TestDirectory.Profile()); else if (mode == "close") view.SetActive(false); else if (mode == "dispose") view.Dispose(); else if (mode == "path") All(view).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Local .eml file path").Text = "changed.eml";
        if (mode != "queued") Assert.True(store.Token.IsCancellationRequested);
        store.Opened.SetResult(new("fixture.eml", [65])); await opening; if (mode == "queued") view.SetActive(false); queue.Drain();
        Assert.Empty(view.PreviewText); Assert.Empty(view.Imports); Assert.Equal(0, store.Imports);
    }
    [Fact]
    public async Task SelectionChangeRejectsAPendingReadAndRestoresUsableFocus()
    {
        var account = TestDirectory.Profile();
        var first = new LocalOriginalReceipt(Guid.NewGuid(), account.Id, "one.eml", 1, new string('a', 64), DateTimeOffset.UtcNow);
        var second = first with { Id = Guid.NewGuid(), FileName = "two.eml" };
        var store = new HeldStore { Rows = [first, second] };
        using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view); session.RenderFrame();
        await view.ListAsync(); queue.Drain(); var reading = view.ReadAsync(first); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        All(view).OfType<StandardComboBox>().Single().SelectedIndex = 1;
        Assert.True(store.Token.IsCancellationRequested); store.Opened.SetResult(new("one.eml", [65])); await reading; queue.Drain();
        Assert.Empty(view.PreviewText); Assert.False(view.IsBusy); Assert.True(session.FocusedElement?.CanFocus);
    }

    [Fact]
    public async Task NewAccountCanOpenWhileAnOldRequestIgnoresCancellation()
    {
        var store = new HeldStore(); using var view = new LocalOriginalsView(store); view.SetAccount(TestDirectory.Profile()); view.SetActive(true);
        var old = view.OpenFileAsync("old.eml"); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var held = store.Opened;
        view.SetAccount(TestDirectory.Profile()); store.Opened = new(TaskCreationOptions.RunContinuationsAsynchronously); store.Opened.SetResult(new("new.eml", [78]));
        await view.OpenFileAsync("new.eml"); Assert.Contains("new.eml", view.PreviewText); held.SetResult(new("old.eml", [79])); await old;
        Assert.Contains("new.eml", view.PreviewText); Assert.DoesNotContain("old.eml", view.PreviewText);
    }
    [Fact]
    public async Task InvalidFileAndCorruptImportStorageAreReportedWithoutEscapingTheUiOperation()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("empty.eml"); await File.WriteAllBytesAsync(path, []);
        using var view = new LocalOriginalsView(new JsonLocalMessageOriginals(directory.Root)); view.SetAccount(TestDirectory.Profile()); view.SetActive(true);
        await view.OpenFileAsync(path); Assert.Contains("did not finish", view.Status); Assert.Empty(view.PreviewText); Assert.False(view.IsBusy);
        await File.WriteAllTextAsync(directory.File("local-originals.json"), "invalid");
        await view.ListAsync(); Assert.Contains("did not finish", view.Status); Assert.Empty(view.Imports); Assert.False(view.IsBusy);
    }

    [Fact]
    public async Task UnsupportedInactiveAndCanceledViewsNeverImport()
    {
        using var unsupported = new LocalOriginalsView(null); unsupported.SetAccount(TestDirectory.Profile()); unsupported.SetActive(true);
        await unsupported.OpenFileAsync("unused.eml"); Assert.Contains("unavailable", unsupported.Status); Assert.All(All(unsupported).OfType<StandardButton>(), button => Assert.False(button.IsEnabled));
        var store = new HeldStore(); using var view = new LocalOriginalsView(store); view.SetAccount(TestDirectory.Profile()); await view.OpenFileAsync("unused.eml"); Assert.False(store.Entered.Task.IsCompleted);
        view.SetActive(true); await view.ImportOpenedAsync(); Assert.Equal(0, store.Imports);
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class HeldStore : ILocalMessageOriginals
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<LocalOriginalContent> Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token; public int Imports;
        public IReadOnlyList<LocalOriginalReceipt> Rows = [];
        public Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default) { var result = Opened.Task; Token = token; Entered.TrySetResult(); return result; }
        public Task<LocalOriginalReceipt> ImportAsync(AccountId account, LocalOriginalContent content, CancellationToken token = default) { Imports++; throw new InvalidOperationException("No implicit import."); }
        public Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(AccountId account, CancellationToken token = default) => Task.FromResult(Rows);
        public Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt receipt, CancellationToken token = default) { var result = Opened.Task; Token = token; Entered.TrySetResult(); return result; }
    }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(940, 680); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { }
    }
}
