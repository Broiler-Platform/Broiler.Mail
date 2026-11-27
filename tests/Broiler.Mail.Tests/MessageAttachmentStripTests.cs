using System.Globalization;
using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// UX-03.3: the reader's inline attachment strip. It lists every attachment with its name and size, opens a file dialog
/// only when a save is chosen, reads the original once per message (a verified saved copy first), and writes exact,
/// verified bytes through the picked location's atomic write, which never replaces a file.
/// </summary>
[Collection("UI theme")]
public sealed class MessageAttachmentStripTests
{
    private static readonly byte[] Report = Random(300_000, 1);
    private static readonly byte[] Notes = Encoding.ASCII.GetBytes("Plain notes\r\nSecond line\r\n");
    private static readonly Part ReportPart = new("report.pdf", "application/pdf", Report);
    private static readonly Part NotesPart = new("notes.txt", "text/plain", Notes);

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed record Part(string Name, string MediaType, byte[] Data, string? Disposition = null);

    /// <summary>A text body followed by each part as a base64 attachment.</summary>
    private static byte[] Original(params Part[] parts)
    {
        var raw = new StringBuilder("From: sender@example.test\r\nTo: test@example.test\r\nSubject: Attachments\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=outer\r\n\r\n--outer\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSee the attached files.\r\n");
        foreach (var part in parts)
            raw.Append("--outer\r\nContent-Type: ").Append(part.MediaType).Append("\r\nContent-Disposition: ")
                .Append(part.Disposition ?? $"attachment; filename=\"{part.Name}\"")
                .Append("\r\nContent-Transfer-Encoding: base64\r\n\r\n")
                .Append(Convert.ToBase64String(part.Data, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
        raw.Append("--outer--\r\n");
        return Encoding.ASCII.GetBytes(raw.ToString());
    }

    [Fact]
    public async Task StripHiddenWithoutAttachmentsAndBannerSlotCollapsed()
    {
        using var app = await Fixture.OpenAsync(_ => new TestAttachmentReader(), new FakeMailFilePicker(), Original());
        Assert.Empty(All(app.Root).OfType<MessageAttachmentStrip>());
        Assert.Equal(UiVisibility.Collapsed, app.Panel("Attachments").Visibility);
        Assert.Equal(UiVisibility.Collapsed, app.Notices.Visibility);
        Assert.DoesNotContain(ReaderMenus.Items(app.Root, "More message actions"), item => item.Id == "attachments");
    }

    [Fact]
    public async Task StripListsNameAndHumanSizeAndSaveAllOnlyForMultiple()
    {
        using var app = await Fixture.OpenAsync(_ => new TestAttachmentReader(), new FakeMailFilePicker(), Original(ReportPart), Original(ReportPart, NotesPart));
        var single = app.Strip;
        Assert.True(Shown(single));
        Assert.Equal(UiVisibility.Visible, app.Notices.Visibility);
        Assert.Contains("report.pdf · 293 KiB", Labels(single));
        Assert.Contains("1 attachment · 293 KiB", Labels(single));
        Assert.Single(SaveButtons(single));
        Assert.DoesNotContain(All(single).OfType<StandardButton>(), button => button.Text == "Save all…");

        await app.Model.SelectAsync(app.KeyOf(2));
        var two = app.Strip;
        Assert.True(single.IsDisposed);
        Assert.Equal(["2 attachments · 293 KiB", "report.pdf · 293 KiB", "notes.txt · 26 bytes"], Labels(two));
        Assert.Equal(2, SaveButtons(two).Length);
        Assert.True(Shown(All(two).OfType<StandardButton>().Single(button => button.Text == "Save all…")));
        // The strip offers no typed path, in the desktop mode as in the unavailable mode below.
        Assert.Empty(All(two).OfType<StandardEdit>());
    }

    [Fact]
    public async Task MoreThanThreeAttachmentsUseDisclosure()
    {
        var parts = Enumerable.Range(1, 5).Select(i => new Part($"file{i}.bin", "application/octet-stream", Random(100 * i, i))).ToArray();
        using var app = await Fixture.OpenAsync(_ => new TestAttachmentReader(), new FakeMailFilePicker(), Original(parts));
        var strip = app.Strip;
        var toggle = All(strip).OfType<StandardButton>().Single(button => button.Text == "Show all 5 attachments");
        Assert.True(Shown(toggle));
        Assert.Equal(3, SaveButtons(strip).Count(Shown));

        toggle.Click();
        app.Render();
        Assert.Equal(5, SaveButtons(strip).Count(Shown));
        Assert.Equal("Show fewer attachments", toggle.Text);

        app.Session.SetFocus(SaveButtons(strip)[4]);
        toggle.Click();
        app.Render();
        Assert.Equal(3, SaveButtons(strip).Count(Shown));
        Assert.Equal("Show all 5 attachments", toggle.Text);
        Assert.Same(toggle, app.Session.FocusedElement);
    }

    [Fact]
    public async Task AccessibleNamesDescribeEachAction()
    {
        using var app = await Fixture.OpenAsync(_ => new TestAttachmentReader(), new FakeMailFilePicker(), Original(ReportPart, NotesPart));
        var strip = app.Strip;
        Assert.Equal("Attachments (2)", strip.AccessibleName);
        Assert.Contains(All(strip).OfType<StandardLabel>(), label => label.AccessibleName == "2 attachments, total 293 KiB");
        var saves = SaveButtons(strip);
        Assert.Equal(["Save report.pdf as…", "Save notes.txt as…"], saves.Select(button => button.AccessibleName).ToArray());
        Assert.Equal("Save report.pdf (293 KiB) to a location you choose", saves[0].ToolTipText);
        Assert.Equal("Save all 2 attachments to a folder…", All(strip).OfType<StandardButton>().Single(button => button.Text == "Save all…").AccessibleName);
        var cancel = All(strip).OfType<StandardButton>().Single(button => button.Text == "Cancel saving");
        Assert.Equal("Cancel saving attachments", cancel.AccessibleName);
        Assert.Equal(UiVisibility.Collapsed, cancel.Visibility);
        Assert.True(strip.IsDescendantOf(app.Panel("Attachments")));
        // The strip is the first of the reader's notices.
        Assert.Same(app.Panel("Attachments"), app.Panel("Message notices").Children[0]);
    }

    [Fact]
    public async Task SaveAsUsesSavedOriginalWithoutNetwork()
    {
        byte[] original = Original(ReportPart, NotesPart);
        var receiver = TestSourceReceiver.Returning(original);
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => new MessageAttachmentReader(receiver, receiver, f.Exports, f.Exports), files, original);
        await app.Exports.SaveAsync(new MailMessageSource(app.KeyOf(1), original));
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        await app.Strip.SaveAsync(app.Strip.Attachments[0]);

        Assert.Equal(0, receiver.SourceCalls + receiver.CopySourceCalls);
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(app.Saves, "report.pdf")));
        Assert.Equal("Saved report.pdf (293 KiB). The file was not opened.", app.Strip.Status);
        var (kind, request) = Assert.Single(files.Requests);
        Assert.Equal("save", kind);
        Assert.Equal(MailFilePurpose.SaveAttachment, request.Purpose);
        Assert.Equal("Save attachment", request.Title);
        Assert.Equal("report.pdf", request.SuggestedFileName);
        Assert.Equal("Save", request.AcceptText);
        Assert.Equal("*.pdf", request.Types[0].Pattern);
        Assert.Same(MailFileType.AllFiles, request.Types[^1]);
    }

    [Fact]
    public async Task SaveAsStreamsOnceAndReusesSourceForSecondSave()
    {
        byte[] original = Original(ReportPart, NotesPart);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, destination, maximum, progress, token) =>
            {
                progress?.Invoke(original.Length / 2, original.Length);
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                await destination.WriteAsync(original, token);
                return original.Length;
            },
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => new MessageAttachmentReader(receiver, receiver, f.Exports, f.Exports), files, original);
        var strip = app.Strip;
        string all = Path.Combine(app.Saves, "all"); Directory.CreateDirectory(all);
        files.WillSave(Path.Combine(app.Saves, "report.pdf")).WillSave(Path.Combine(app.Saves, "notes.txt")).WillChooseFolder(all);

        var first = strip.SaveAsync(strip.Attachments[0]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        app.Dispatcher.Drain();
        Assert.True(strip.IsBusy);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"Downloading original message: {original.Length / 2:N0} of {original.Length:N0} bytes…"), strip.Status);
        release.SetResult();
        await first;
        await strip.SaveAsync(strip.Attachments[1]);
        await strip.SaveAllAsync();

        Assert.Equal(1, receiver.CopySourceCalls);
        Assert.Equal(0, receiver.SourceCalls);
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(app.Saves, "report.pdf")));
        Assert.Equal(Notes, File.ReadAllBytes(Path.Combine(app.Saves, "notes.txt")));
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(all, "report.pdf")));
        Assert.Equal(Notes, File.ReadAllBytes(Path.Combine(all, "notes.txt")));
        Assert.Equal("Saved all 2 attachments to all. The files were not opened.", strip.Status);
    }

    [Fact]
    public async Task SaveAsDoesNotCreateSourceExportOrCatalogEntry()
    {
        byte[] original = Original(ReportPart, NotesPart);
        var receiver = TestSourceReceiver.Returning(original);
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => new MessageAttachmentReader(receiver, receiver, f.Exports, f.Exports), files, original);
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        await app.Strip.SaveAsync(app.Strip.Attachments[0]);

        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(app.Saves, "report.pdf")));
        Assert.Equal(1, receiver.CopySourceCalls);
        Assert.False(Directory.Exists(app.Exports.ExportDirectory));
        Assert.False(Directory.Exists(app.Exports.AttachmentDirectory));
        Assert.False(File.Exists(app.Directory.File("source-export-catalog.json")));
    }

    [Fact]
    public async Task PickerCancelWritesNothingAndRestoresFocus()
    {
        var reader = new TestAttachmentReader();
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, Original(ReportPart, NotesPart));
        var save = SaveButtons(app.Strip)[1];
        app.Session.SetFocus(save);

        await app.Strip.SaveAsync(app.Strip.Attachments[1]);

        Assert.Single(files.Requests);
        Assert.Equal(0, reader.Opens);
        Assert.Equal("", app.Strip.Status);
        Assert.False(app.Strip.IsBusy);
        Assert.Same(save, app.Session.FocusedElement);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
    }

    [Fact]
    public async Task CancelDuringDownloadKeepsNoPartialFile()
    {
        byte[] original = Original(ReportPart, NotesPart);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, _, _, _, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; },
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => new MessageAttachmentReader(receiver, receiver, f.Exports, f.Exports), files, original);
        var strip = app.Strip;
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));
        app.Session.SetFocus(SaveButtons(strip)[0]);

        var saving = strip.SaveAsync(strip.Attachments[0]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancel = All(strip).OfType<StandardButton>().Single(button => button.Text == "Cancel saving");
        Assert.Equal(UiVisibility.Visible, cancel.Visibility);
        Assert.Same(cancel, app.Session.FocusedElement);
        Assert.All(SaveButtons(strip), button => Assert.False(button.IsEnabled));
        cancel.Click();
        await saving;

        Assert.Equal("Saving canceled. No partial file was kept.", strip.Status);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        Assert.All(SaveButtons(strip), button => Assert.True(button.IsEnabled));
        Assert.Equal(UiVisibility.Collapsed, cancel.Visibility);
        Assert.Same(SaveButtons(strip)[0], app.Session.FocusedElement);
    }

    [Fact]
    public async Task EscapeCancelsRunningSaveBeforeClosingFind()
    {
        byte[] original = Original(ReportPart, NotesPart);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, _, _, _, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; },
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => new MessageAttachmentReader(receiver, receiver, f.Exports, f.Exports), files, original);
        var strip = app.Strip;
        Assert.True(app.View.ShowFindInMessage());
        var find = All(app.Root).OfType<MessageTextFindBar>().Single();
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));
        app.Session.SetFocus(SaveButtons(strip)[0]);

        var saving = strip.SaveAsync(strip.Attachments[0]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(app.View.HandleReaderInput(InlineMailFixture.Key(0x1B)));
        await saving;
        app.Render();

        Assert.Equal("Saving canceled. No partial file was kept.", strip.Status);
        Assert.Equal(UiVisibility.Visible, find.Visibility);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        Assert.True(app.View.HandleReaderInput(InlineMailFixture.Key(0x1B)));
        Assert.Equal(UiVisibility.Collapsed, find.Visibility);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("search")]
    public async Task EscapeCancelsRunningSaveWhereverFocusIsBeforeClosingFind(string focus)
    {
        byte[] original = Original(ReportPart, NotesPart);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, _, _, _, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; },
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => new MessageAttachmentReader(receiver, receiver, f.Exports, f.Exports), files, original);
        var dispatcher = app.Dispatcher;
        var shellModel = new MailShellViewModel(
            new(new JsonAccountStore(app.Directory.File("accounts.json")), new TestCredentialStore(), new FixtureReceiver(), dispatcher, app.Account, null),
            new(new JsonSettingsStore(app.Directory.File("settings.json")), dispatcher, new(), null),
            app.Model, new ComposerViewModel(dispatcher: dispatcher));
        using var shell = new MailShellView(shellModel, files: files);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(1100, 720));
        shell.Attach(session); shell.ShowView("inbox");
        session.RenderFrame(); dispatcher.Drain(); session.RenderFrame();
        var keyboard = shell.CreateKeyboardNavigation(session);
        var content = shell.GetContent("inbox");
        var strip = All(content).OfType<MessageAttachmentStrip>().Single();
        Assert.True(shell.Inbox.ShowFindInMessage());
        var find = All(content).OfType<MessageTextFindBar>().Single();
        if (focus == "search") Assert.True(shell.Inbox.ShowSearch());
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        var saving = strip.SaveAsync(strip.Attachments[0]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Focus leaves the reader without changing the message, so the save and the find bar both stay.
        if (focus == "list") session.SetFocus(All(content).OfType<Broiler.UI.ListView.Standard.StandardListView>().Single());
        Assert.False(session.FocusedElement is { } focused && focused.IsDescendantOf(strip));
        Assert.True(keyboard.Handle(InlineMailFixture.Key(0x1B)));
        await saving.WaitAsync(TimeSpan.FromSeconds(10));
        session.RenderFrame();

        Assert.Equal("Saving canceled. No partial file was kept.", strip.Status);
        Assert.Equal(UiVisibility.Visible, find.Visibility);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        Assert.False(shell.Inbox.CancelAttachmentSave());
    }

    [Fact]
    public async Task MessageChangeWhilePickerOpenDiscardsResult()
    {
        var reader = new TestAttachmentReader();
        var files = new GatedPicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, Original(ReportPart), Original(NotesPart));
        var old = app.Strip;

        var saving = old.SaveAsync(old.Attachments[0]);
        Assert.True(old.IsBusy);
        await app.Model.SelectAsync(app.KeyOf(2));
        Assert.True(old.IsDisposed);
        files.Save.SetResult(MailFilePick<IMailWritableFile>.Picked(new LocalMailWritableFile(Path.Combine(app.Saves, "report.pdf"))));
        await saving;

        Assert.Equal(0, reader.Opens);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        Assert.Equal("", app.Strip.Status);
        Assert.Equal("notes.txt", Assert.Single(app.Strip.Attachments).FileName);
    }

    [Fact]
    public async Task MessageChangeDuringWriteNeverCommits()
    {
        byte[] original = Original(ReportPart);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HookedSource? hooked = null;
        var reader = new TestAttachmentReader(new MessageAttachmentReader(TestSourceReceiver.Returning(original), null))
        {
            Wrap = source => hooked = new HookedSource(source, async (inner, attachment, destination, token) =>
            {
                await destination.WriteAsync(inner.Read(attachment)[..1000], token);
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }),
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, original, Original(NotesPart));
        var old = app.Strip;
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        var saving = old.SaveAsync(old.Attachments[0]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await app.Model.SelectAsync(app.KeyOf(2));
        Assert.True(old.IsDisposed);
        await saving;

        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        Assert.True(hooked!.Disposed);
    }

    [Fact]
    public async Task HashMismatchNeverCommits()
    {
        byte[] original = Original(ReportPart);
        var reader = new TestAttachmentReader(new MessageAttachmentReader(TestSourceReceiver.Returning(original), null))
        {
            Wrap = source => new HookedSource(source, async (inner, attachment, destination, token) =>
            {
                byte[] changed = inner.Read(attachment).ToArray();
                changed[^1] ^= 1;
                await destination.WriteAsync(changed, token);
            }),
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, original);
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        await app.Strip.SaveAsync(app.Strip.Attachments[0]);

        Assert.Equal("Saved bytes did not verify; nothing was kept.", app.Strip.Status);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingDestinationUntouchedOnFailure(bool destinationExists)
    {
        byte[] original = Original(ReportPart);
        var reader = new TestAttachmentReader(new MessageAttachmentReader(TestSourceReceiver.Returning(original), null))
        {
            Wrap = source => destinationExists ? source : new HookedSource(source, async (inner, attachment, destination, token) =>
            {
                await destination.WriteAsync(inner.Read(attachment)[..5000], token);
                throw new IOException("The disk is full.");
            }),
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, original);
        string target = Path.Combine(app.Saves, "report.pdf");
        if (destinationExists) File.WriteAllText(target, "keep me");
        files.WillSave(target);

        await app.Strip.SaveAsync(app.Strip.Attachments[0]);

        Assert.StartsWith("Could not write the file. ", app.Strip.Status, StringComparison.Ordinal);
        Assert.EndsWith(" Choose another location.", app.Strip.Status, StringComparison.Ordinal);
        if (destinationExists)
        {
            Assert.Equal("keep me", File.ReadAllText(target));
            Assert.Equal([target], Directory.GetFileSystemEntries(app.Saves));
        }
        else Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
    }

    [Fact]
    public async Task SaveAllMakesUniqueNamesAndNeverOverwrites()
    {
        byte[] original = Original(ReportPart, NotesPart);
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => new MessageAttachmentReader(TestSourceReceiver.Returning(original), null), files, original);
        string folder = Path.Combine(app.Saves, "Downloads"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "report.pdf"), "existing");
        files.WillChooseFolder(folder);

        await app.Strip.SaveAllAsync();

        Assert.Equal("directory", Assert.Single(files.Requests).Kind);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(folder, "report.pdf")));
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(folder, "report (2).pdf")));
        Assert.Equal(Notes, File.ReadAllBytes(Path.Combine(folder, "notes.txt")));
        Assert.Equal(3, Directory.GetFileSystemEntries(folder).Length);
        Assert.Equal("Saved all 2 attachments to Downloads. 1 renamed to avoid replacing existing files. The files were not opened.", app.Strip.Status);
    }

    [Fact]
    public async Task SaveAllCancelReportsPartialCount()
    {
        var third = new Part("third.bin", "application/octet-stream", Random(4000, 9));
        byte[] original = Original(ReportPart, NotesPart, third);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new TestAttachmentReader(new MessageAttachmentReader(TestSourceReceiver.Returning(original), null))
        {
            Wrap = source => new HookedSource(source, async (inner, attachment, destination, token) =>
            {
                if (attachment.FileName != "notes.txt") { await inner.CopyToAsync(attachment, destination, token); return; }
                await destination.WriteAsync(inner.Read(attachment)[..4], token);
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }),
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, original);
        string folder = Path.Combine(app.Saves, "Downloads"); Directory.CreateDirectory(folder);
        files.WillChooseFolder(folder);

        var saving = app.Strip.SaveAllAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        app.Strip.Cancel();
        await saving;

        Assert.Equal("Canceled after saving 1 of 3. Saved files remain; the unfinished file was removed.", app.Strip.Status);
        Assert.Equal([Path.Combine(folder, "report.pdf")], Directory.GetFileSystemEntries(folder));
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(folder, "report.pdf")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OfflineWithoutSavedOriginalExplainsAndOffersOriginal(bool unreachable)
    {
        byte[] original = Original(ReportPart, NotesPart);
        var receiver = new TestSourceReceiver
        {
            CopySource = (_, _, _, _, _) => Task.FromException<int>(new MailConnectionException("The server could not be reached.", MailConnectionFailure.Unreachable)),
        };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(f => unreachable ? new MessageAttachmentReader(receiver, null, f.Exports, f.Exports)
            : new MessageAttachmentReader(null, null, f.Exports, f.Exports), files, original);
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        await app.Strip.SaveAsync(app.Strip.Attachments[0]);
        app.Render();

        Assert.Equal(MessageAttachmentStrip.OfflineText, app.Strip.Status);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        var revealOriginal = All(app.Strip).OfType<StandardButton>().Single(button => button.Text == "Original message (.eml)…");
        Assert.True(Shown(revealOriginal));
        app.Session.SetFocus(revealOriginal);
        revealOriginal.Click();
        app.Render();
        var section = All(app.Root).OfType<MessageSourceExportActions>().Single();
        Assert.True(app.Session.FocusedElement?.IsDescendantOf(section), $"Focus is on {app.Session.FocusedElement}.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnsupportedStructureFallsBackToOriginal(bool changedOriginal)
    {
        byte[] listed = Original(ReportPart, NotesPart);
        // The server's copy now names the report differently, so its parts no longer match the list.
        byte[] served = Original(ReportPart with { Name = "renamed.pdf" }, NotesPart);
        var reader = changedOriginal ? new TestAttachmentReader(new MessageAttachmentReader(TestSourceReceiver.Returning(served), null))
            : new TestAttachmentReader { Failure = new InvalidDataException("The original's MIME structure is not supported.") };
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => reader, files, listed);
        files.WillSave(Path.Combine(app.Saves, "report.pdf"));

        await app.Strip.SaveAsync(app.Strip.Attachments[0]);
        app.Render();

        Assert.Equal(MessageAttachmentStrip.UnsupportedText, app.Strip.Status);
        Assert.Empty(Directory.GetFileSystemEntries(app.Saves));
        Assert.True(Shown(All(app.Strip).OfType<StandardButton>().Single(button => button.Text == "Original message (.eml)…")));
        if (changedOriginal)
        {
            // The original is already read: another save of a part it cannot give opens no dialog.
            await app.Strip.SaveAsync(app.Strip.Attachments[0]);
            Assert.Single(files.Requests);
            Assert.Equal(1, reader.Opens);
            Assert.Equal(MessageAttachmentStrip.UnsupportedText, app.Strip.Status);
        }
    }

    // A forwarded multipart attached beside ordinary files: listed, never saved, and never in the way of the others.
    private static readonly Part ForwardedPart = new("fwd.eml", "multipart/mixed; boundary=inner",
        Encoding.ASCII.GetBytes("--inner\r\nContent-Type: text/plain\r\n\r\nforwarded\r\n--inner--\r\n"));

    [Fact]
    public async Task AnAttachedContainerDoesNotBlockSavingTheOtherAttachments()
    {
        byte[] original = Original(ReportPart, ForwardedPart, NotesPart);
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => new MessageAttachmentReader(TestSourceReceiver.Returning(original), null), files, original);
        var strip = app.Strip;
        Assert.Equal(new[] { false, true, false }, strip.Attachments.Select(attachment => attachment.IsContainer));
        Assert.Equal(2, strip.SavableCount);
        var saveAll = All(strip).OfType<StandardButton>().Single(button => button.Text == "Save all…");
        Assert.Equal("Save all 2 attachments to a folder…", saveAll.AccessibleName);
        Assert.Equal(new[] { true, false, true }, SaveButtons(strip).Select(button => button.IsEnabled));
        string folder = Path.Combine(app.Saves, "Downloads"); Directory.CreateDirectory(folder);
        files.WillSave(Path.Combine(app.Saves, "report.pdf")).WillChooseFolder(folder);

        await strip.SaveAsync(strip.Attachments[0]);
        Assert.Equal("Saved report.pdf (293 KiB). The file was not opened.", strip.Status);
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(app.Saves, "report.pdf")));

        await strip.SaveAllAsync();
        Assert.Equal("Saved all 2 attachments to Downloads. The files were not opened.", strip.Status);
        Assert.Equal(Report, File.ReadAllBytes(Path.Combine(folder, "report.pdf")));
        Assert.Equal(Notes, File.ReadAllBytes(Path.Combine(folder, "notes.txt")));
        Assert.Equal(2, Directory.GetFileSystemEntries(folder).Length);
    }

    [Fact]
    public async Task OneSavablePartBesideAContainerOffersNoSaveAll()
    {
        byte[] original = Original(ReportPart, ForwardedPart);
        using var app = await Fixture.OpenAsync(_ => new MessageAttachmentReader(TestSourceReceiver.Returning(original), null), new FakeMailFilePicker(), original);
        Assert.Equal(1, app.Strip.SavableCount);
        Assert.DoesNotContain(All(app.Strip).OfType<StandardButton>(), button => button.Text == "Save all…" && Shown(button));
        Assert.Contains(ReaderMenus.Items(app.Root, "More message actions"), item => item.Id == "attachments" && item.Text == "Save attachment…");
    }

    public static TheoryData<bool> IncompleteLists() => new() { true, false };

    /// <summary>
    /// More than 32 attachments, or a structure missing its closing delimiter: the original's parts are never extracted
    /// then, so the strip lists what it found, disables its saves with the reason, and offers the original instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(IncompleteLists))]
    public async Task AnIncompleteListDisablesSavesAndSaysWhy(bool tooMany)
    {
        byte[] original = tooMany
            ? Original(Enumerable.Range(1, 33).Select(i => new Part($"file{i}.bin", "application/octet-stream", Random(10, i))).ToArray())
            : Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(Original(ReportPart, NotesPart)).Replace("--outer--\r\n", "", StringComparison.Ordinal));
        var receiver = TestSourceReceiver.Returning(original);
        var files = new FakeMailFilePicker();
        using var app = await Fixture.OpenAsync(_ => new MessageAttachmentReader(receiver, receiver), files, original);
        var strip = app.Strip;
        Assert.NotEmpty(strip.Attachments);
        Assert.False(strip.CanSave);
        Assert.Equal(MessageAttachmentStrip.IncompleteText, strip.Status);
        foreach (var button in SaveButtons(strip))
        {
            Assert.False(button.IsEnabled);
            Assert.Equal(MessageAttachmentStrip.IncompleteText, button.ToolTipText);
        }
        // A list cut off after its first part offers no Save all; any that is offered is disabled too.
        Assert.All(All(strip).OfType<StandardButton>().Where(button => button.Text == "Save all…"), button => Assert.False(button.IsEnabled));
        Assert.Equal(tooMany, All(strip).OfType<StandardButton>().Any(button => button.Text == "Save all…"));
        Assert.True(Shown(All(strip).OfType<StandardButton>().Single(button => button.Text == "Original message (.eml)…")));
        Assert.DoesNotContain(ReaderMenus.Items(app.Root, "More message actions"), item => item.Id == "attachments");

        await strip.SaveAsync(strip.Attachments[0]);
        await strip.SaveAllAsync();
        Assert.Empty(files.Requests);
        Assert.Equal(0, receiver.SourceCalls + receiver.CopySourceCalls);
        Assert.Equal(MessageAttachmentStrip.IncompleteText, strip.Status);
    }

    /// <summary>
    /// Product-owner decision 2: where the picker reports itself unavailable (Android's today) or there is no way to
    /// read the original, the strip still lists every attachment and disables its saves with the reason; the
    /// attachment-exports save in Original message (.eml) stays, so the device loses nothing it had.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoPickerOrReaderDisablesSaveButListsAttachments(bool unavailablePicker)
    {
        byte[] original = Original(ReportPart, NotesPart);
        var files = new FakeMailFilePicker();
        IMailFilePicker picker = unavailablePicker ? UnavailableMailFilePicker.NotOnThisDeviceYet : files;
        using var app = await Fixture.OpenAsync(_ => unavailablePicker ? new MessageAttachmentReader(TestSourceReceiver.Returning(original), null) : null, picker, original);
        var strip = app.Strip;

        Assert.False(strip.CanSave);
        Assert.Contains("report.pdf · 293 KiB", Labels(strip));
        Assert.Contains("notes.txt · 26 bytes", Labels(strip));
        foreach (var button in SaveButtons(strip).Append(All(strip).OfType<StandardButton>().Single(button => button.Text == "Save all…")))
        {
            Assert.True(Shown(button));
            Assert.False(button.IsEnabled);
            Assert.Equal(MessageAttachmentStrip.NotAvailableText, button.ToolTipText);
        }
        Assert.Empty(All(strip).OfType<StandardEdit>());
        Assert.DoesNotContain(ReaderMenus.Items(app.Root, "More message actions"), item => item.Id == "attachments");
        await strip.SaveAsync(strip.Attachments[0]);
        await strip.SaveAllAsync();
        Assert.Empty(files.Requests);
        Assert.Equal("", strip.Status);
        Assert.NotEmpty(All(app.Root).OfType<MessageSourceExportActions>());
    }

    [Theory]
    [InlineData(1100, 720)]
    [InlineData(480, 640)]
    public async Task MoreMenuAttachmentsItemFocusesFirstSave(int width, int height)
    {
        using var app = await Fixture.OpenAsync(_ => new TestAttachmentReader(), new FakeMailFilePicker(), width, height, Original(ReportPart, NotesPart), Original(NotesPart));
        var layout = All(app.Root).OfType<AdaptiveInboxLayout>().Single();
        Assert.True(app.View.OpenSelected());
        app.Dispatcher.DrainUntil(() => !app.Model.IsBusy && app.Model.Body is not null);
        app.Render();
        if (layout.IsCompact) Assert.True(layout.ShowsReaderOnly);
        var more = ReaderMenus.Button(app.Root, "More message actions");
        Assert.True(more.Trigger.IsEnabled && Shown(more.Trigger), $"More message actions: enabled {more.Trigger.IsEnabled}, shown {Shown(more.Trigger)}, compact {layout.IsCompact}.");

        ReaderMenus.Invoke(app.Session, app.Root, "More message actions", "Attachments (2)…");
        app.Render();
        Assert.Same(SaveButtons(app.Strip)[0], app.Session.FocusedElement);

        await app.Model.SelectAsync(app.KeyOf(2));
        app.Render();
        ReaderMenus.Invoke(app.Session, app.Root, "More message actions", "Save attachment…");
        app.Render();
        Assert.Same(Assert.Single(SaveButtons(app.Strip)), app.Session.FocusedElement);
    }

    /// <summary>
    /// End to end over loopback IMAP: the message is received and opened with its body from the server, and one press of
    /// Save as… streams the original once, read-only, and writes the 3 MiB attachment under its RFC 2231 name.
    /// </summary>
    [Fact]
    public async Task Strip_SavesThreeMiBAttachmentWithRfc2231NameInOneClick()
    {
        byte[] payload = Random(3 * 1024 * 1024, 5);
        const string name = "Bericht – März.pdf";
        byte[] original = Original(new Part(name, "application/pdf", payload, "attachment; filename*=UTF-8''Bericht%20%E2%80%93%20M%C3%A4rz.pdf"));
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, Encoding.ASCII.GetString(original)));
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(20));
        using var directory = new TestDirectory(); directory.Create();
        var files = new FakeMailFilePicker().WillSave(directory.File(name));
        var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher()) { AttachmentReader = new MessageAttachmentReader(receiver, receiver) };
        model.SetAccount(account);
        await model.ReceiveAsync();
        await model.SelectAsync(Assert.Single(model.Messages).Key);
        Assert.Equal(name, Assert.Single(model.Body!.Attachments!).FileName);
        int fetchesBefore = server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        var view = new InboxView(model) { Files = files };
        using var root = view.CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(1100, 720));
        session.AddRoot(root);
        session.RenderFrame();
        var strip = All(root).OfType<MessageAttachmentStrip>().Single();

        SaveButtons(strip)[0].Click();
        dispatcher.DrainUntil(() => !strip.IsBusy);

        Assert.Equal(name, Assert.Single(files.Requests).Request.SuggestedFileName);
        Assert.Equal(payload, File.ReadAllBytes(directory.File(name)));
        Assert.Equal($"Saved {name} (3.0 MiB). The file was not opened.", strip.Status);
        Assert.Equal(fetchesBefore + 1, server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal)));
        Assert.DoesNotContain(server.Commands, command => command is "STORE" or "EXPUNGE");
    }

    private static string[] Labels(UiElement root) => All(root).OfType<StandardLabel>().Select(label => label.Text).Where(text => text.Length > 0).ToArray();

    private static StandardButton[] SaveButtons(UiElement root) => All(root).OfType<StandardButton>().Where(button => button.Text == "Save as…").ToArray();

    private static bool Shown(UiElement element)
    {
        for (UiElement? at = element; at is not null; at = at.Parent) if (at.Visibility != UiVisibility.Visible) return false;
        return true;
    }

    private static IEnumerable<UiElement> All(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var nested in All(child)) yield return nested;
    }

    /// <summary>The inbox over a SQLite cache whose messages carry the decoded bodies of the given originals.</summary>
    private sealed class Fixture : IDisposable
    {
        private Fixture() { }

        public TestDirectory Directory { get; } = new();
        public AccountProfile Account { get; } = TestDirectory.Profile();
        public TestQueueDispatcher Dispatcher { get; } = new();
        public FileMessageSourceExports Exports { get; private set; } = null!;
        public InboxViewModel Model { get; private set; } = null!;
        public InboxView View { get; private set; } = null!;
        public UiElement Root { get; private set; } = null!;
        public UiSession Session { get; private set; } = null!;
        public string Saves => Directory.File("saves");
        public MessageAttachmentStrip Strip => All(Root).OfType<MessageAttachmentStrip>().Single();
        public BoundedScrollArea Notices => All(Root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Reader notices");
        public UiElement Panel(string name) => All(Root).Single(element => element is Broiler.UI.Panel.Standard.StandardPanel && element.AccessibleName == name);
        public MailMessageKey KeyOf(uint uid) => new(Account.Id, "INBOX", 7, uid);

        public static Task<Fixture> OpenAsync(Func<Fixture, IMessageAttachmentReader?> reader, IMailFilePicker files, params byte[][] originals) =>
            OpenAsync(reader, files, 1100, 720, originals);

        public static async Task<Fixture> OpenAsync(Func<Fixture, IMessageAttachmentReader?> reader, IMailFilePicker files, int width, int height, params byte[][] originals)
        {
            var fixture = new Fixture();
            fixture.Directory.Create();
            System.IO.Directory.CreateDirectory(fixture.Saves);
            fixture.Exports = new FileMessageSourceExports(fixture.Directory.Root);
            var cache = new SqliteMailCache(fixture.Directory.File("mail.db"));
            var mailbox = new MailboxKey(fixture.Account.Id, "INBOX", 7);
            await cache.SaveSummariesAsync(mailbox, originals.Select((_, i) => new MailMessageSummary
            {
                Key = fixture.KeyOf((uint)i + 1), Subject = $"Message {i + 1}", Sender = "sender@example.test",
                ReceivedAt = new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.Zero).AddMinutes(-i),
            }).ToArray(), DateTimeOffset.UtcNow);
            for (int i = 0; i < originals.Length; i++)
                await cache.SaveBodyAsync(mailbox, MessageTextDecoder.DecodeReceived(fixture.KeyOf((uint)i + 1), originals[i], CancellationToken.None));
            fixture.Model = new InboxViewModel(new FixtureReceiver(), new ImmediateUiDispatcher(), cache, sourceExports: fixture.Exports) { AttachmentReader = reader(fixture) };
            fixture.Model.SetAccount(fixture.Account);
            await fixture.Model.CacheLoadTask;
            await fixture.Model.SelectAsync(fixture.KeyOf(1));
            fixture.View = new InboxView(fixture.Model) { Files = files };
            fixture.Root = fixture.View.CreateContent();
            fixture.Session = new StandardUiSessionBuilder().WithDispatcher(fixture.Dispatcher).Build(new Host(width, height));
            fixture.Session.AddRoot(fixture.Root);
            fixture.Render();
            return fixture;
        }

        public void Render()
        {
            Session.RenderFrame();
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session?.Dispose();
            Root?.Dispose();
            Model?.Dispose();
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Dispose(); return; }
                catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
            }
        }
    }

    /// <summary>Uses the cached bodies; the original can only come from the attachment reader under test.</summary>
    private sealed class FixtureReceiver : IMailReceiver, IMailSourceReceiver
    {
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cached body must be used.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailInboxPage([], null));
        public Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) =>
            Task.FromException<MailMessageSource>(new MailConnectionException("The server could not be reached.", MailConnectionFailure.Unreachable));
    }

    /// <summary>Counts opens, fails when told to, and can wrap the source the inner reader opened.</summary>
    private sealed class TestAttachmentReader(IMessageAttachmentReader? inner = null) : IMessageAttachmentReader
    {
        private int _opens;
        public int Opens => _opens;
        public Exception? Failure { get; init; }
        public Func<IMailAttachmentSource, IMailAttachmentSource>? Wrap { get; init; }

        public async Task<IMailAttachmentSource> OpenAsync(AccountProfile account, MailMessageKey key, Action<int, int>? progress = null, CancellationToken token = default)
        {
            Interlocked.Increment(ref _opens);
            if (Failure is not null) throw Failure;
            var source = await (inner ?? throw new InvalidOperationException("This reader has no original.")).OpenAsync(account, key, progress, token);
            return Wrap?.Invoke(source) ?? source;
        }
    }

    /// <summary>An opened source whose copies are replaced by the test's.</summary>
    private sealed class HookedSource(IMailAttachmentSource inner, Func<IMailAttachmentSource, ReceivedAttachment, Stream, CancellationToken, Task> copy) : IMailAttachmentSource
    {
        public bool Disposed { get; private set; }
        public MailMessageKey Key => inner.Key;
        public bool FromSavedOriginal => inner.FromSavedOriginal;
        public IReadOnlyList<ReceivedAttachment> Attachments => inner.Attachments;
        public Task CopyToAsync(ReceivedAttachment attachment, Stream destination, CancellationToken token = default) => copy(inner, attachment, destination, token);
        public ReadOnlyMemory<byte> Read(ReceivedAttachment attachment) => inner.Read(attachment);
        public void Dispose() { Disposed = true; inner.Dispose(); }
    }

    /// <summary>A save picker that answers only when the test completes <see cref="Save"/>, whatever the token says.</summary>
    private sealed class GatedPicker : IMailFilePicker
    {
        public TaskCompletionSource<MailFilePick<IMailWritableFile>> Save { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MailFilePickerCapabilities Capabilities => MailFilePickerCapabilities.SaveFile | MailFilePickerCapabilities.PickDirectory | MailFilePickerCapabilities.LocalPaths;
        public Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default) => Task.FromResult(MailFilePick<IMailReadableFile>.Canceled);
        public Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default) => Save.Task;
        public Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default) => Task.FromResult(MailFilePick<IMailPickedDirectory>.Canceled);
    }

    private sealed class Host(double width, double height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
