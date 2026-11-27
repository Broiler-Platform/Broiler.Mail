using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class LocalOriginalManagementViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task DecodingExportAndKeepFirstRemovalAreExplicitAndRestoreFromVerifiedBackup(double scale)
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.File("profile")); var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath, storage); var account = TestDirectory.Profile() with { IsEnabled = false };
        byte[] original = Encoding.UTF8.GetBytes("Subject: Decoded original\r\nFrom: sender@example.test\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>Hello &amp; safe</p><script>never shown</script>");
        string input = directory.File("input.eml"); await File.WriteAllBytesAsync(input, original);
        using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true); var queue = new TestQueueDispatcher();
        using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(view, theme); session.RenderFrame();
        await view.OpenFileAsync(input); queue.Drain(); await view.DecodeAsync(); queue.Drain(); session.RenderFrame();
        Assert.Contains("Subject: Decoded original", view.PreviewText); Assert.Contains("Hello & safe", view.PreviewText); Assert.DoesNotContain("never shown", view.PreviewText);
        Assert.True(Button(view, "Import opened original").IsEnabled); view.ShowSource(); view.ShowSource(); Assert.Contains("<script>", view.PreviewText);
        Assert.Single(All(view).OfType<ScrollableMessageText>());
        await view.ImportOpenedAsync(); queue.Drain(); await view.ListAsync(); queue.Drain(); var receipt = Assert.Single(view.Imports);
        string output = directory.File("exported.eml"); await view.ExportAsync(receipt, output); queue.Drain(); Assert.Equal(original, await File.ReadAllBytesAsync(output)); Assert.Contains(receipt.Sha256, view.PreviewText);
        view.RequestRemoval(receipt); Assert.True(view.HasPendingRemoval); Assert.Equal("Keep imported original", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        Assert.False(Button(view, "Remove import after recovery backup").IsDefault); Assert.Single(await store.ListImportsAsync(account.Id));
        view.Keep(); Assert.False(view.HasPendingRemoval); Assert.True(session.FocusedElement?.CanFocus); Assert.Single(await store.ListImportsAsync(account.Id));
        view.RequestRemoval(receipt); await view.ConfirmRemovalAsync(); queue.Drain(); session.RenderFrame(); Assert.Empty(await store.ListImportsAsync(account.Id)); Assert.Empty(view.Imports);
        Assert.Contains("Verified recovery backup", view.PreviewText); Assert.Equal(original, await File.ReadAllBytesAsync(input)); Assert.Equal(original, await File.ReadAllBytesAsync(output));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalHtmlRequiresAnExplicitChoiceAndKeepsSelectableText(bool fail)
    {
        using var directory = new TestDirectory();
        using var profile = MailProfile.Open(directory.File("profile"));
        var store = new JsonLocalMessageOriginals(profile.DirectoryPath);
        var host = new LocalHtmlHost(fail);
        using var view = new LocalOriginalsView(store, host);
        view.SetAccount(TestDirectory.Profile());
        view.SetActive(true);
        string input = directory.File("local.eml");
        await File.WriteAllTextAsync(input, "Content-Type: text/html; charset=utf-8\r\n\r\n<p>Local body</p>");
        await view.OpenFileAsync(input);
        // Opening shows the decoded message; local HTML still needs its own explicit choice.
        Assert.True(view.ShowsDecoded);
        Assert.Equal(0, host.Created);
        await view.DecodeAsync();
        Assert.Equal(0, host.Created);
        var text = view.PreviewText;
        Assert.Contains("Local body", text);
        Button(view, "Show local HTML").Click();
        Assert.Equal(1, host.Created);
        Assert.Equal(text, view.PreviewText);
        if (fail) Assert.Contains("could not be rendered", view.Status);
        else
        {
            Assert.True(Button(view, "Hide local HTML").IsEnabled);
            var surface = host.Surface!;
            view.ShowSource();
            Assert.True(surface.IsDisposed);
            Assert.False(Button(view, "Show local HTML").IsEnabled);
            await view.DecodeAsync();
            Button(view, "Show local HTML").Click();
            view.SetActive(false);
            Assert.True(host.Surface!.IsDisposed);
        }
    }

    private sealed class LocalHtmlHost(bool fail) : ILocalOriginalHtmlHost
    {
        public int Created { get; private set; }
        public UiElement? Surface { get; private set; }

        public UiElement CreateLocalOriginalContent(string html)
        {
            Assert.Contains("<p>Local body</p>", html);
            Created++;
            if (fail) throw new ArgumentException("Fixture renderer failure");
            return Surface = new Broiler.UI.Label.Standard.StandardLabel { Text = "Local body" };
        }
    }

    [Theory]
    [InlineData("account")]
    [InlineData("close")]
    public async Task ContextChangeInvalidatesPendingRemoval(string mode)
    {
        using var directory = new TestDirectory(); using var profile = MailProfile.Open(directory.Root); var store = new JsonLocalMessageOriginals(profile.DirectoryPath, new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath)));
        var account = TestDirectory.Profile(); var receipt = await store.ImportAsync(account.Id, new("original.eml", [65]));
        using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true); await view.ListAsync(); view.RequestRemoval(receipt);
        if (mode == "account") view.SetAccount(TestDirectory.Profile()); else view.SetActive(false);
        await view.ConfirmRemovalAsync(); Assert.False(view.HasPendingRemoval); Assert.Single(await store.ListImportsAsync(account.Id));
    }
    [Fact]
    public async Task ReadOnlyHostCannotOfferRemovalWithoutRecoverySupport()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = TestDirectory.Profile(); var receipt = await store.ImportAsync(account.Id, new("original.eml", [65]));
        using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true); await view.ListAsync(); view.RequestRemoval(receipt);
        Assert.False(view.HasPendingRemoval); Assert.False(Button(view, "Remove selected import…").IsEnabled); await view.ConfirmRemovalAsync(); Assert.Single(await store.ListImportsAsync(account.Id));
    }
    [Theory]
    [InlineData("decode")]
    [InlineData("export")]
    [InlineData("remove")]
    public async Task LateManagementResultsCannotRepaintAnotherAccount(string operation)
    {
        var account = TestDirectory.Profile(); var receipt = new LocalOriginalReceipt(Guid.NewGuid(), account.Id, "original.eml", 1, new string('a', 64), DateTimeOffset.UtcNow);
        var store = new HeldManagement(receipt); using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        // Opening decodes at once; hold only the operation under test.
        await view.ListAsync(); queue.Drain(); await view.OpenFileAsync("fixture.eml"); queue.Drain(); store.Hold = true;
        Task task;
        if (operation == "decode") task = view.DecodeAsync();
        else if (operation == "export") task = view.ExportAsync(receipt, "unused.eml");
        else { view.RequestRemoval(receipt); task = view.ConfirmRemovalAsync(); }
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); view.SetAccount(TestDirectory.Profile()); Assert.True(store.Token.IsCancellationRequested);
        store.Release.SetResult(); await task; queue.Drain(); Assert.Empty(view.PreviewText); Assert.Empty(view.Imports); Assert.False(view.HasPendingRemoval);
    }
    [Fact]
    public async Task ExportThroughPickerWritesExactBytesAndNeverOverwrites()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = TestDirectory.Profile();
        byte[] original = Encoding.ASCII.GetBytes("Subject: Export\r\n\r\nExact bytes\r\n").Concat(new byte[] { 0, 128, 255 }).ToArray();
        var receipt = await store.ImportAsync(account.Id, new("kept original.eml", original));
        string output = directory.File("exported.eml"), existing = directory.File("existing.eml"); await File.WriteAllBytesAsync(existing, [42]);
        var files = new FakeMailFilePicker().WillSave(output).WillSave(existing);
        using var view = new LocalOriginalsView(store, files: files); view.SetAccount(account); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view); session.RenderFrame();
        Assert.Empty(All(view).OfType<Broiler.UI.Edit.Standard.StandardEdit>());
        await view.ListAsync(); queue.Drain();
        await view.ExportWithPickerAsync(receipt); queue.Drain();
        var request = Assert.Single(files.Requests); Assert.Equal("save", request.Kind); Assert.Equal(MailFilePurpose.ExportOriginal, request.Request.Purpose);
        Assert.Equal("Export as .eml", request.Request.Title); Assert.Equal("kept original.eml", request.Request.SuggestedFileName); Assert.Equal(new[] { MailFileType.Email }, request.Request.Types);
        Assert.Equal(original, await File.ReadAllBytesAsync(output)); Assert.Contains(receipt.Sha256, view.PreviewText); Assert.Contains("Nothing was overwritten", view.Status);
        // A target that appeared after the pick is never replaced.
        await view.ExportWithPickerAsync(receipt); queue.Drain();
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(existing)); Assert.Contains("did not finish", view.Status);
        // A target without a path on this device is written through the picker handle, after a checksum check.
        var pathless = new MemoryTarget(); files.SaveResults.Enqueue(MailFilePick<IMailWritableFile>.Picked(pathless));
        await view.ExportWithPickerAsync(receipt); queue.Drain(); Assert.Equal(original, pathless.Written); Assert.Contains(receipt.Sha256, view.PreviewText);
        // Canceling saves nothing and keeps focus on the export button.
        await view.ExportWithPickerAsync(receipt); queue.Drain(); Assert.Equal("Export canceled. Nothing was saved.", view.Status);
        Assert.Same(Button(view, LocalOriginalsView.ExportButtonText), session.FocusedElement);
        Assert.Equal(new[] { "existing.eml", "exported.eml" }, Directory.GetFiles(directory.Root, "*.eml").Select(Path.GetFileName).Order());
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }
    [Fact]
    public async Task APendingRemovalSaysWhyAPickedFileCannotOpen()
    {
        var account = TestDirectory.Profile(); var receipt = new LocalOriginalReceipt(Guid.NewGuid(), account.Id, "original.eml", 1, new string('a', 64), DateTimeOffset.UtcNow);
        var store = new HeldManagement(receipt); using var view = new LocalOriginalsView(store); view.SetAccount(account); view.SetActive(true);
        await view.ListAsync(); Assert.Null(view.OpenBlockedReason); Assert.False(view.ExplainOpenBlocked());
        view.RequestRemoval(receipt); Assert.True(view.HasPendingRemoval);
        Assert.NotNull(view.OpenBlockedReason); Assert.True(view.ExplainOpenBlocked()); Assert.Contains("Nothing was opened", view.Status); Assert.True(view.HasPendingRemoval);
        view.Keep(); Assert.Null(view.OpenBlockedReason);
        view.SetActive(false); Assert.Null(view.OpenBlockedReason);
    }
    private sealed class MemoryTarget : IMailWritableFile
    {
        public byte[]? Written { get; private set; }
        public string DisplayName => "provider document";
        public string? LocalPath => null;
        public async Task<MailWrittenFile> WriteNewAsync(Func<Stream, CancellationToken, Task> write, CancellationToken token = default)
        { using var stream = new MemoryStream(); await write(stream, token); Written = stream.ToArray(); return new(DisplayName, null, Written.Length); }
    }
    private sealed class HeldManagement(LocalOriginalReceipt receipt) : ILocalMessageOriginals, ILocalOriginalManagement
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously); public CancellationToken Token;
        public bool CanRemove => true;
        public Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default) => Task.FromResult(new LocalOriginalContent("original.eml", [65]));
        public Task<LocalOriginalReceipt> ImportAsync(AccountId account, LocalOriginalContent content, CancellationToken token = default) => throw new InvalidOperationException("No implicit import.");
        public Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(AccountId account, CancellationToken token = default) => Task.FromResult<IReadOnlyList<LocalOriginalReceipt>>([receipt]);
        public Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt row, CancellationToken token = default) => Task.FromResult(new LocalOriginalContent("original.eml", [65]));
        public bool Hold;
        private async Task Wait(CancellationToken token) { if (!Hold) return; Token = token; Entered.TrySetResult(); await Release.Task; }
        public async Task<LocalOriginalPreview> DecodeAsync(LocalOriginalContent content, CancellationToken token = default) { await Wait(token); return new("Late subject", "Late sender", "Late text", false, false, false); }
        public async Task<LocalOriginalExport> ExportAsync(LocalOriginalReceipt row, string path, CancellationToken token = default) { await Wait(token); return new(path, 1, receipt.Sha256); }
        public async Task<LocalOriginalRemoval> RemoveAsync(LocalOriginalReceipt row, CancellationToken token = default) { await Wait(token); return new(row, "late-backup"); }
    }

    private static StandardButton Button(UiElement root, string text) => All(root).OfType<StandardButton>().Single(button => button.Text == text);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class Host : IUiHost
    { public BSize ViewportSize => new(940, 680); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { } }
}
