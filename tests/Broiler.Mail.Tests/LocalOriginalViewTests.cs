using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
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
    [InlineData("repick")]
    public async Task LateOpenCannotRepaintLostContext(string mode)
    {
        // Typed paths exist only without a picker; a picked file replaces an open that is still reading.
        var store = new HeldStore(); using var view = new LocalOriginalsView(store, files: mode == "repick" ? new FakeMailFilePicker() : null); view.SetAccount(TestDirectory.Profile()); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var opening = view.OpenFileAsync("fixture.eml"); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode == "cancel") view.Cancel(); else if (mode == "account") view.SetAccount(TestDirectory.Profile()); else if (mode == "close") view.SetActive(false); else if (mode == "dispose") view.Dispose(); else if (mode == "path") All(view).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Local .eml file path").Text = "changed.eml";
        else if (mode == "repick") { Assert.Empty(All(view).OfType<StandardEdit>()); await view.OpenPickedAsync(new PathlessFile("changed.eml", [66])); }
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
    private const string Message = "Subject: Local\r\nFrom: sender@example.test\r\nTo: reader@example.test\r\nDate: 04 Mar 2026 05:06:07 +01:00\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"b\"\r\n\r\n--b\r\nContent-Type: text/plain\r\n\r\nDecoded body\r\n--b\r\nContent-Type: application/pdf; name=\"report.pdf\"\r\nContent-Disposition: attachment; filename=\"report.pdf\"\r\n\r\nPDF\r\n--b--\r\n";
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task PickedFileOpensDecodedByDefaultAndToggleShowsRawWithoutIo(double scale)
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("picked.eml"); await File.WriteAllTextAsync(path, Message);
        var files = new FakeMailFilePicker().WillOpen(path);
        using var view = new LocalOriginalsView(new JsonLocalMessageOriginals(directory.Root), files: files); view.SetAccount(TestDirectory.Profile()); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(view, theme); session.RenderFrame();
        Assert.True(await view.OpenWithPickerAsync()); queue.Drain(); session.RenderFrame();
        var request = Assert.Single(files.Requests); Assert.Equal("open", request.Kind); Assert.Same(LocalOriginalsView.OpenRequest, request.Request);
        Assert.Equal(MailFilePurpose.OpenOriginal, request.Request.Purpose); Assert.Equal("Open .eml file", request.Request.Title); Assert.Equal("Open", request.Request.AcceptText);
        Assert.Equal(new[] { MailFileType.Email, MailFileType.AllFiles }, request.Request.Types);
        Assert.True(view.ShowsDecoded); string decoded = view.PreviewText;
        Assert.StartsWith("picked.eml · ", decoded);
        foreach (string line in new[] { "Subject: Local", "From: sender@example.test", "To: reader@example.test", "Date: 2026-03-04 05:06 +01:00", "Attachments: report.pdf (3 bytes)", "Decoded body" }) Assert.Contains(line, decoded);
        Assert.DoesNotContain("Content-Type:", decoded); Assert.StartsWith("Opened picked.eml (", view.Status);
        // The file is gone: switching views reads nothing.
        File.Delete(path);
        var toggle = Button(view, "Show raw source (first 64 KiB)"); Assert.True(toggle.IsEnabled); toggle.Click(); session.RenderFrame();
        Assert.False(view.ShowsDecoded); Assert.Contains("Content-Type: application/pdf", view.PreviewText); Assert.Equal("Show decoded message", toggle.Text);
        view.ShowSource(); Assert.Single(All(view).OfType<ScrollableMessageText>());
        toggle.Click(); Assert.True(view.ShowsDecoded); Assert.Equal(decoded, view.PreviewText); Assert.Equal("Show raw source (first 64 KiB)", toggle.Text);
        view.ShowDecoded(); Assert.Single(All(view).OfType<ScrollableMessageText>()); Assert.Single(files.Requests);
    }
    [Fact]
    public async Task UndecodableFileOpensRawWithExplanation()
    {
        var store = new UndecodableStore(); using var view = new LocalOriginalsView(store, files: new FakeMailFilePicker()); view.SetActive(true);
        await view.OpenPickedAsync(new PathlessFile("odd.eml", [1, 2, 3]));
        Assert.False(view.ShowsDecoded); Assert.Contains("odd.eml — 3 of 3 source bytes", view.PreviewText);
        Assert.Equal("Opened odd.eml. It could not be read as an email message, so its raw source is shown.", view.Status);
        Assert.False(Button(view, LocalOriginalsView.ShowDecodedText).IsEnabled); Assert.Equal(1, store.Decodes); view.ShowDecoded(); Assert.False(view.ShowsDecoded);
    }
    [Fact]
    public async Task PickerCancelKeepsCurrentPreviewAndStartsNoWork()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("first.eml"); await File.WriteAllTextAsync(path, Message);
        var files = new FakeMailFilePicker().WillOpen(path);
        using var view = new LocalOriginalsView(new JsonLocalMessageOriginals(directory.Root), files: files); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view); session.RenderFrame();
        await view.OpenWithPickerAsync(); queue.Drain(); string shown = view.PreviewText; Assert.Contains("Decoded body", shown);
        Assert.False(await view.OpenWithPickerAsync()); queue.Drain();
        Assert.Equal(shown, view.PreviewText); Assert.False(view.IsBusy); Assert.Equal("No file chosen. Nothing was opened.", view.Status);
        Assert.Same(Button(view, LocalOriginalsView.OpenButtonText), session.FocusedElement);
        files.OpenResults.Enqueue(MailFilePick<IMailReadableFile>.Rejected("That file is a folder."));
        Assert.False(await view.OpenWithPickerAsync()); queue.Drain();
        Assert.Equal(shown, view.PreviewText); Assert.Equal("That file is a folder. Nothing was opened.", view.Status); Assert.Equal(3, files.Requests.Count);
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task OpenWorksWithoutAnAccountButImportRequiresOne()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("setup.eml"); await File.WriteAllTextAsync(path, Message);
        var store = new JsonLocalMessageOriginals(directory.Root);
        using var view = new LocalOriginalsView(store, files: new FakeMailFilePicker()); view.SetActive(true);
        Assert.True(Button(view, LocalOriginalsView.OpenButtonText).IsEnabled); Assert.False(Button(view, "List local imports").IsEnabled);
        await view.OpenPickedAsync(new LocalMailFile(path)); Assert.True(view.ShowsDecoded); Assert.Contains("Decoded body", view.PreviewText);
        Assert.False(Button(view, "Import opened original").IsEnabled);
        await view.ImportOpenedAsync(); await view.ListAsync(); Assert.Empty(view.Imports); Assert.False(File.Exists(directory.File("local-originals.json")));
        Assert.Contains("Decoded body", view.PreviewText);
        var account = TestDirectory.Profile(); view.SetAccount(account); Assert.Empty(view.PreviewText);
        await view.OpenPickedAsync(new LocalMailFile(path)); Assert.True(Button(view, "Import opened original").IsEnabled);
        await view.ImportOpenedAsync(); Assert.Single(await store.ListImportsAsync(account.Id));
    }
    [Fact]
    public async Task FileOverTwoMiBCanBeViewedButNotImported()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("large.eml");
        await File.WriteAllBytesAsync(path, LocalOriginalTests.LargeMessage(LocalOriginalLimits.OpenBytes));
        var store = new FileMessageSourceExports(directory.Root);
        using var view = new LocalOriginalsView(store, files: new FakeMailFilePicker()); view.SetAccount(TestDirectory.Profile()); view.SetActive(true);
        await view.OpenPickedAsync(new PathlessFile("large.eml", await File.ReadAllBytesAsync(path)));
        Assert.True(view.ShowsDecoded); Assert.Contains("large.eml · 12.0 MiB", view.PreviewText); Assert.Contains("Attachments: data.bin (6.0 MiB)", view.PreviewText);
        Assert.EndsWith(" Files over 2 MiB can be viewed but not kept in this profile.", view.Status);
        var import = Button(view, "Import opened original"); Assert.False(import.IsEnabled); Assert.Equal(LocalOriginalsView.NotKeptText, import.ToolTipText);
        await view.ImportOpenedAsync(); Assert.Contains("Keeping a copy in the profile is limited to 2 MiB per file", view.Status);
        Assert.False(File.Exists(directory.File("local-originals.json")));
        await File.WriteAllBytesAsync(path, new byte[LocalOriginalLimits.OpenBytes + 1]);
        await view.OpenPickedAsync(new LocalMailFile(path)); Assert.Empty(view.PreviewText); Assert.StartsWith("The file could not be opened. Local .eml files must contain 1 byte to 12 MiB.", view.Status);
    }
    [Fact]
    public async Task NoPathTextBoxesRemainAndPickerButtonsDisableWithoutPicker()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("typed.eml"); await File.WriteAllTextAsync(path, Message);
        var store = new JsonLocalMessageOriginals(directory.Root); var account = TestDirectory.Profile();
        using (var picking = new LocalOriginalsView(store, files: new FakeMailFilePicker()))
        {
            picking.SetAccount(account); picking.SetActive(true);
            Assert.Empty(All(picking).OfType<StandardEdit>()); Assert.True(Button(picking, LocalOriginalsView.OpenButtonText).IsEnabled);
            Assert.DoesNotContain(All(picking).OfType<StandardButton>(), button => button.Text is "Open typed .eml path" or "Export to typed path");
            Assert.False(Button(picking, LocalOriginalsView.ExportButtonText).IsEnabled);
        }
        // A device without a file picker keeps the typed paths (until its picker exists), so it loses no function.
        var unavailable = UnavailableMailFilePicker.NotOnThisDeviceYet;
        using var view = new LocalOriginalsView(store, files: unavailable); view.SetAccount(account); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view); session.RenderFrame();
        var open = Button(view, LocalOriginalsView.OpenButtonText); Assert.False(open.IsEnabled); Assert.Equal(unavailable.Reason, open.ToolTipText);
        var export = Button(view, LocalOriginalsView.ExportButtonText); Assert.False(export.IsEnabled); Assert.Equal(unavailable.Reason, export.ToolTipText);
        Assert.False(await view.OpenWithPickerAsync());
        All(view).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "Local .eml file path").Text = path;
        Button(view, "Open typed .eml path").Click(); await WaitAsync(() => view.PreviewText.Length > 0, queue);
        Assert.True(view.ShowsDecoded); Assert.Contains("Decoded body", view.PreviewText);
        await view.ImportOpenedAsync(); queue.Drain(); await view.ListAsync(); queue.Drain(); var receipt = Assert.Single(view.Imports);
        await view.ReadAsync(receipt); queue.Drain(); Assert.True(view.ShowsDecoded);
        string output = directory.File("typed-copy.eml");
        All(view).OfType<StandardEdit>().Single(edit => edit.AccessibleName == "New local .eml export path").Text = output;
        var typedExport = Button(view, "Export to typed path"); Assert.True(typedExport.IsEnabled); typedExport.Click();
        await WaitAsync(() => File.Exists(output) && !view.IsBusy, queue);
        Assert.Equal(await File.ReadAllBytesAsync(path), await File.ReadAllBytesAsync(output));
    }
    private static async Task WaitAsync(Func<bool> done, TestQueueDispatcher queue)
    {
        for (int i = 0; i < 500 && !done(); i++) { queue.Drain(); await Task.Delay(10); }
        queue.Drain(); Assert.True(done());
    }
    private static StandardButton Button(UiElement root, string text) => All(root).OfType<StandardButton>().Single(button => button.Text == text);
    private sealed class PathlessFile(string name, byte[] bytes) : IMailReadableFile
    {
        public string DisplayName => name;
        public string? LocalPath => null;
        public long? Length => null;
        public Task<Stream> OpenReadAsync(CancellationToken token = default) => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
    private sealed class UndecodableStore : ILocalMessageOriginals, ILocalOriginalManagement, ILocalOriginalStreamSource
    {
        public int Decodes;
        public bool CanRemove => false;
        public async Task<LocalOriginalContent> OpenStreamAsync(string displayName, Stream source, CancellationToken token = default)
        { using var copy = new MemoryStream(); await source.CopyToAsync(copy, token); return new(displayName, copy.ToArray()); }
        public Task<LocalOriginalContent> OpenFileAsync(string path, CancellationToken token = default) => throw new InvalidOperationException("Use the stream.");
        public Task<LocalOriginalReceipt> ImportAsync(AccountId account, LocalOriginalContent content, CancellationToken token = default) => throw new InvalidOperationException("No implicit import.");
        public Task<IReadOnlyList<LocalOriginalReceipt>> ListImportsAsync(AccountId account, CancellationToken token = default) => Task.FromResult<IReadOnlyList<LocalOriginalReceipt>>([]);
        public Task<LocalOriginalContent> ReadImportAsync(LocalOriginalReceipt receipt, CancellationToken token = default) => throw new InvalidOperationException();
        public Task<LocalOriginalPreview> DecodeAsync(LocalOriginalContent content, CancellationToken token = default) { Decodes++; throw new InvalidDataException("Not MIME."); }
        public Task<LocalOriginalExport> ExportAsync(LocalOriginalReceipt receipt, string destination, CancellationToken token = default) => throw new InvalidOperationException();
        public Task<LocalOriginalRemoval> RemoveAsync(LocalOriginalReceipt receipt, CancellationToken token = default) => throw new InvalidOperationException();
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
