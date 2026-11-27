// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.2: the desktop picker shows the standard file dialog over the open form, and checks what it returns.</summary>
[Collection("UI theme")]
public sealed class MailFileDialogsTests
{
    [Fact]
    public async Task PickOpenFileShowsTitledStandardFileDialogOverActiveOwner()
    {
        using var fixture = ShellFixture.Open();
        fixture.Shell.ShowView("compose");
        var compose = fixture.Shell.ActiveDialog!;

        var pick = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file") { AcceptText = "Attach", InitialDirectory = fixture.Directory.Root });

        var dialog = Assert.IsType<StandardFileDialog>(fixture.Session.ModalElement);
        Assert.Equal("Attach file", dialog.Title);
        Assert.Equal("Attach", dialog.OkButton.Text);
        Assert.Equal(UiFileDialogMode.Open, dialog.Mode);
        Assert.Same(compose, dialog.Owner);
        Assert.Same(dialog, fixture.Shell.ActiveFileDialog);
        Assert.Equal(fixture.Directory.Root, dialog.CurrentDirectory);
        Assert.Same(dialog.FileNameEdit, fixture.Session.FocusedElement);
        Assert.False(pick.IsCompleted);

        Assert.True(dialog.Cancel());
        Assert.Null(fixture.Shell.ActiveFileDialog);
        fixture.Dispatcher.Drain();
        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Same(compose, fixture.Shell.ActiveDialog);
        Assert.Same(compose, fixture.Session.ModalElement);
    }

    [Fact]
    public void FileDialogControlsHaveAccessibleNames()
    {
        using var fixture = ShellFixture.Open();
        _ = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.OpenOriginal, "Open .eml file") { Types = [MailFileType.Email, MailFileType.AllFiles] });
        var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);

        Assert.Equal("File name", dialog.FileNameEdit.AccessibleName);
        Assert.Equal("Places", dialog.PlacesList.AccessibleName);
        Assert.Equal("Files", dialog.FilesList.AccessibleName);
        Assert.Equal("Folders", dialog.DirectoriesList.AccessibleName);
        Assert.Equal("File type", dialog.FileTypeComboBox.AccessibleName);
        Assert.Equal("Sort files by", dialog.SortComboBox.AccessibleName);
        Assert.Equal("Cancel", dialog.CancelButton.Text);
        Assert.Equal("*.eml", dialog.FileNameFilter);
        Assert.Equal(2, dialog.FileTypeFilters.Count);
        dialog.Cancel();
    }

    [Fact]
    public async Task AcceptExistingFileReturnsPickedLocalFile()
    {
        using var fixture = ShellFixture.Open();
        string path = fixture.Directory.File("report.txt");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        var pick = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file") { InitialDirectory = fixture.Directory.Root });
        var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);
        dialog.FileNameEdit.Text = "report.txt";
        Assert.True(dialog.AcceptSelection());
        fixture.Dispatcher.Drain();

        var result = await pick;
        Assert.Equal(MailFilePickStatus.Picked, result.Status);
        Assert.Equal(path, result.Item!.LocalPath);
        Assert.Equal("report.txt", result.Item.DisplayName);
        Assert.Equal(5, result.Item.Length);
        await using var stream = await result.Item.OpenReadAsync();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal([1, 2, 3, 4, 5], copy.ToArray());
    }

    [Fact]
    public async Task OpenMissingOrDirectoryIsRejected()
    {
        using var fixture = ShellFixture.Open();
        Directory.CreateDirectory(fixture.Directory.File("folder"));
        foreach (var name in new[] { "missing.txt", "folder" })
        {
            var pick = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file") { InitialDirectory = fixture.Directory.Root });
            Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog).Accept(fixture.Directory.File(name));
            fixture.Dispatcher.Drain();
            var result = await pick;
            Assert.Equal(MailFilePickStatus.Rejected, result.Status);
            Assert.Null(result.Item);
            Assert.Contains($"'{name}'", result.Message);
        }
    }

    [Fact]
    public async Task CancelReturnsCanceledAndRestoresTriggerFocus()
    {
        using var fixture = ShellFixture.Open();
        fixture.Shell.ShowView("compose");
        var trigger = fixture.Shell.Composer.Body!;
        fixture.Session.SetFocus(trigger);

        var pick = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"));
        var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);
        Assert.NotSame(trigger, fixture.Session.FocusedElement);
        dialog.CancelButton.Click();
        fixture.Dispatcher.Drain();

        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Null(fixture.Shell.ActiveFileDialog);
        Assert.Same(trigger, fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task CancelledTokenClosesDialog()
    {
        using var fixture = ShellFixture.Open();
        using var cancellation = new CancellationTokenSource();
        var pick = fixture.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.SaveAttachment, "Save attachment"), cancellation.Token);
        var dialog = fixture.Shell.ActiveFileDialog;
        Assert.NotNull(dialog);

        cancellation.Cancel();
        fixture.Dispatcher.Drain();

        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Null(fixture.Shell.ActiveFileDialog);
        Assert.Null(fixture.Session.ModalElement);
        Assert.Equal(MailFilePickStatus.Canceled, (await fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"), cancellation.Token)).Status);
        Assert.Null(fixture.Shell.ActiveFileDialog);
    }

    [Fact]
    public async Task ShellDisposeCompletesPendingPickAsCanceled()
    {
        var fixture = ShellFixture.Open();
        fixture.Shell.ShowView("compose");
        var pick = fixture.Shell.Files.PickDirectoryAsync(new(MailFilePurpose.BackupLocation, "Choose backup location"));
        Assert.NotNull(fixture.Shell.ActiveFileDialog);

        fixture.Dispose();

        Assert.True(pick.IsCompleted);
        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Equal(MailFilePickStatus.Unavailable, (await fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"))).Status);
    }

    [Fact]
    public async Task SecondPickWhileOpenIsUnavailable()
    {
        using var fixture = ShellFixture.Open();
        var first = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"));
        var dialog = fixture.Shell.ActiveFileDialog;

        var second = await fixture.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.SaveAttachment, "Save attachment"));
        var third = await fixture.Shell.Files.PickDirectoryAsync(new(MailFilePurpose.BackupLocation, "Choose backup location"));

        Assert.Equal(MailFilePickStatus.Unavailable, second.Status);
        Assert.Equal("Another file dialog is already open.", second.Message);
        Assert.Equal(MailFilePickStatus.Unavailable, third.Status);
        Assert.Same(dialog, fixture.Shell.ActiveFileDialog);
        Assert.Same(dialog, fixture.Session.ModalElement);
        Assert.False(first.IsCompleted);
        dialog!.Cancel();
        fixture.Dispatcher.Drain();
        Assert.Equal(MailFilePickStatus.Canceled, (await first).Status);
    }

    [Fact]
    public async Task SaveSuggestsNameAndAppliesEmlExtension()
    {
        using var fixture = ShellFixture.Open();
        var pick = fixture.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.ExportOriginal, "Export as .eml")
        {
            Types = [MailFileType.Email], SuggestedFileName = "GrÃ¼ÃŸe", AcceptText = "Export", InitialDirectory = fixture.Directory.Root,
        });
        var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);

        Assert.Equal(UiFileDialogMode.Save, dialog.Mode);
        Assert.Equal("Export as .eml", dialog.Title);
        Assert.Equal("Export", dialog.OkButton.Text);
        Assert.Equal("GrÃ¼ÃŸe", dialog.FileName);
        Assert.Equal("GrÃ¼ÃŸe", dialog.FileNameEdit.Text);
        Assert.EndsWith("GrÃ¼ÃŸe.eml", dialog.SelectedPath, StringComparison.Ordinal);

        Assert.True(dialog.AcceptSelection());
        fixture.Dispatcher.Drain();
        var result = await pick;
        Assert.Equal(MailFilePickStatus.Picked, result.Status);
        Assert.Equal(fixture.Directory.File("GrÃ¼ÃŸe.eml"), result.Item!.LocalPath);
        Assert.False(File.Exists(result.Item.LocalPath));
    }

    [Fact]
    public async Task SaveOntoExistingFileIsRejectedAndNothingIsWritten()
    {
        using var fixture = ShellFixture.Open();
        string existing = fixture.Directory.File("kept.eml");
        File.WriteAllBytes(existing, [9, 9]);
        Directory.CreateDirectory(fixture.Directory.File("folder.eml"));

        foreach (var (name, expected) in new[] { ("kept.eml", "never replaces files"), ("folder.eml", "is a folder") })
        {
            var pick = fixture.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.ExportOriginal, "Export as .eml") { InitialDirectory = fixture.Directory.Root });
            var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);
            dialog.FileNameEdit.Text = name;
            Assert.True(dialog.AcceptSelection());
            fixture.Dispatcher.Drain();
            var result = await pick;
            Assert.Equal(MailFilePickStatus.Rejected, result.Status);
            Assert.Contains(expected, result.Message);
        }
        Assert.Equal([9, 9], File.ReadAllBytes(existing));
        Assert.Empty(PartFiles(fixture.Directory));
    }

    [Fact]
    public async Task SaveIntoAMissingFolderIsRejected()
    {
        using var fixture = ShellFixture.Open();
        var pick = fixture.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.ExportOriginal, "Export as .eml") { InitialDirectory = fixture.Directory.Root });
        fixture.Shell.ActiveFileDialog!.Accept(fixture.Directory.File(Path.Combine("no-such-folder", "a.eml")));
        fixture.Dispatcher.Drain();
        var result = await pick;
        Assert.Equal(MailFilePickStatus.Rejected, result.Status);
        Assert.Equal("Save into an existing folder.", result.Message);
    }

    [Fact]
    public async Task AFolderReachedThroughAJunctionIsASaveTargetUnlessItLeadsNowhere()
    {
        using var fixture = ShellFixture.Open();
        string real = fixture.Directory.File("real");
        Directory.CreateDirectory(real);
        string linked = fixture.Directory.File("linked");
        using var junction = Junction.Create(linked, real);
        Assert.True((File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0);

        // A junction to an existing folder, as a redirected or synced folder can be, is an ordinary save target.
        Assert.True(MailFileDialogs.IsUsableDirectory(linked));
        Assert.Null(MailFileDialogs.SaveProblem(Path.Combine(linked, "kept.eml")));
        var save = fixture.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.SaveMessage, "Save as .eml") { InitialDirectory = linked });
        Assert.Equal(linked, Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog).CurrentDirectory);
        fixture.Shell.ActiveFileDialog!.Accept(Path.Combine(linked, "kept.eml"));
        fixture.Dispatcher.Drain();
        var picked = await save;
        Assert.Equal(MailFilePickStatus.Picked, picked.Status);
        await picked.Item!.WriteNewAsync((stream, token) => stream.WriteAsync(new byte[] { 5, 6 }, token).AsTask());
        Assert.Equal([5, 6], File.ReadAllBytes(Path.Combine(real, "kept.eml")));

        // Save allâ€¦ into the same junction is allowed too.
        var all = fixture.Shell.Files.PickDirectoryAsync(new(MailFilePurpose.SaveAttachment, "Save all attachments") { InitialDirectory = linked });
        var folder = MailFolderDialogTests.Content(Assert.IsType<StandardDialog>(fixture.Shell.ActiveFileDialog));
        Assert.Equal(linked, folder.CurrentDirectory);
        Assert.True(folder.Accept());
        fixture.Dispatcher.Drain();
        Assert.Equal(MailFilePickStatus.Picked, (await all).Status);

        // Once the folder it leads to is gone, the junction is not a place to save.
        Directory.Delete(real, recursive: true);
        Assert.False(MailFileDialogs.IsUsableDirectory(linked));
        Assert.Equal("Save into an existing folder.", MailFileDialogs.SaveProblem(Path.Combine(linked, "kept.eml")));
    }

    [Fact]
    public async Task NothingIsWrittenThroughALinkAtTheFileNameItself()
    {
        using var directory = new TestDirectory();
        directory.Create();
        string elsewhere = directory.File("elsewhere");
        Directory.CreateDirectory(elsewhere);
        string taken = directory.File("taken.eml");
        using var junction = Junction.Create(taken, elsewhere);
        // The link now leads nowhere: the name looks free to a check that follows links.
        Directory.Delete(elsewhere);

        Assert.True(MailFileDialogs.IsTaken(taken));
        // Windows still reports the dangling junction as a folder, so the refusal says that; elsewhere the
        // dangling link is refused as a taken name.
        if (OperatingSystem.IsWindows()) Assert.Equal("'taken.eml' is a folder. Type a file name.", MailFileDialogs.SaveProblem(taken));
        else Assert.NotNull(MailFileDialogs.SaveProblem(taken));
        await Assert.ThrowsAsync<IOException>(() => new LocalMailWritableFile(taken).WriteNewAsync((stream, token) => stream.WriteAsync(new byte[] { 1 }, token).AsTask()));

        var written = await new LocalMailDirectory(directory.Root).WriteNewFileAsync("taken.eml", (stream, token) => stream.WriteAsync(new byte[] { 2 }, token).AsTask());
        Assert.Equal("taken (2).eml", written.DisplayName);
        Assert.False(Directory.Exists(elsewhere));
        Assert.Empty(PartFiles(directory));

        // A file symbolic link, where this account may create one, is refused the same way.
        string fileLink = directory.File("linked.eml");
        try { File.CreateSymbolicLink(fileLink, directory.File("missing-target.eml")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        try
        {
            Assert.True(MailFileDialogs.IsTaken(fileLink));
            Assert.NotNull(MailFileDialogs.SaveProblem(fileLink));
            await Assert.ThrowsAsync<IOException>(() => new LocalMailWritableFile(fileLink).WriteNewAsync((stream, token) => stream.WriteAsync(new byte[] { 3 }, token).AsTask()));
            Assert.False(File.Exists(directory.File("missing-target.eml")));
        }
        finally { File.Delete(fileLink); }
    }

    /// <summary>
    /// A directory junction made with <c>mklink /J</c>, which needs no extra privilege, and removed again on
    /// dispose by itself: a recursive delete of the test folder cannot remove one that leads nowhere.
    /// </summary>
    private sealed class Junction : IDisposable
    {
        private readonly string _link;
        private Junction(string link) => _link = link;

        public static Junction Create(string link, string target)
        {
            if (!OperatingSystem.IsWindows())
            {
                // Elsewhere a directory symbolic link is the folder link an account can make without privilege.
                Directory.CreateSymbolicLink(link, target);
                return new Junction(link);
            }
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0 && Directory.Exists(link), "mklink /J could not create a junction here.");
            return new Junction(link);
        }

        public void Dispose()
        {
            try
            {
                if (OperatingSystem.IsWindows()) Directory.Delete(_link);
                else File.Delete(_link); // unlinks the symbolic link itself, even when it leads nowhere
            }
            catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public async Task WriteNewAsyncPublishesAtomicallyAndCleansUp()
    {
        using var directory = new TestDirectory();
        directory.Create();
        string target = directory.File("saved.bin");

        var written = await new LocalMailWritableFile(target).WriteNewAsync(async (stream, token) => await stream.WriteAsync(new byte[] { 1, 2, 3 }, token));
        Assert.Equal(new MailWrittenFile("saved.bin", target, 3), written);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
        Assert.Empty(PartFiles(directory));

        // A write that throws, as a hash mismatch does, leaves neither the target nor a temporary file.
        string failed = directory.File("failed.bin");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalMailWritableFile(failed).WriteNewAsync(async (stream, token) =>
        {
            await stream.WriteAsync(new byte[] { 1 }, token);
            throw new InvalidDataException("The attachment does not match its saved original.");
        }));
        Assert.False(File.Exists(failed));
        Assert.Empty(PartFiles(directory));

        // So does a cancelled write.
        using var cancellation = new CancellationTokenSource();
        string canceled = directory.File("canceled.bin");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalMailWritableFile(canceled).WriteNewAsync(async (stream, token) =>
        {
            await stream.WriteAsync(new byte[] { 1 }, token);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        }, cancellation.Token));
        Assert.False(File.Exists(canceled));
        Assert.Empty(PartFiles(directory));

        // A file that appears while the new one is written is never replaced.
        string raced = directory.File("raced.bin");
        await Assert.ThrowsAsync<IOException>(() => new LocalMailWritableFile(raced).WriteNewAsync(async (stream, token) =>
        {
            await File.WriteAllBytesAsync(raced, [7], token);
            await stream.WriteAsync(new byte[] { 1, 2 }, token);
        }));
        Assert.Equal([7], File.ReadAllBytes(raced));
        Assert.Empty(PartFiles(directory));

        // And an existing target is refused before anything is written.
        bool called = false;
        await Assert.ThrowsAsync<IOException>(() => new LocalMailWritableFile(target).WriteNewAsync((_, _) => { called = true; return Task.CompletedTask; }));
        Assert.False(called);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
    }

    [Fact]
    public async Task WriteNewFileAsyncMakesUniqueNames()
    {
        using var directory = new TestDirectory();
        directory.Create();
        File.WriteAllBytes(directory.File("notes.txt"), [0]);
        var folder = new LocalMailDirectory(directory.Root);
        static Task Write(Stream stream, CancellationToken token) => stream.WriteAsync(new byte[] { 4, 2 }, token).AsTask();

        var second = await folder.WriteNewFileAsync("notes.txt", Write);
        var third = await folder.WriteNewFileAsync("notes.txt", Write);
        var fresh = await folder.WriteNewFileAsync("other.txt", Write);

        Assert.Equal("notes (2).txt", second.DisplayName);
        Assert.Equal("notes (3).txt", third.DisplayName);
        Assert.Equal("other.txt", fresh.DisplayName);
        Assert.Equal([0], File.ReadAllBytes(directory.File("notes.txt")));
        Assert.Equal([4, 2], File.ReadAllBytes(directory.File("notes (2).txt")));
        Assert.Empty(PartFiles(directory));

        for (int number = 4; number <= LocalMailDirectory.MaximumNumberedName; number++)
            File.WriteAllBytes(directory.File($"notes ({number}).txt"), [0]);
        var unique = await folder.WriteNewFileAsync("notes.txt", Write);
        Assert.StartsWith("notes ", unique.DisplayName, StringComparison.Ordinal);
        Assert.EndsWith(".txt", unique.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("(", unique.DisplayName, StringComparison.Ordinal);

        foreach (var bad in new[] { "", "..", Path.Combine("sub", "x.txt") })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => folder.WriteNewFileAsync(bad, Write));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void FileDialogUsesOwnerThemeAndFitsViewport(double textScale)
    {
        using var fixture = ShellFixture.Open(640, 480);
        var theme = StandardThemeTokens.Dark.WithTextScale(textScale);
        StandardControlPaint.SetSessionTheme(fixture.Session, theme);
        StandardThemeController.ApplyToSubtree(fixture.Shell.Window, theme);
        fixture.Shell.ShowView("compose");
        fixture.Session.RenderFrame();

        _ = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"));
        var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);
        fixture.Session.RenderFrame();

        Assert.Equal(theme.Surface, dialog.Background);
        Assert.Equal(theme.Accent, dialog.TitleBarBackground);
        Assert.True(dialog.Bounds.Left >= 0 && dialog.Bounds.Top >= 0 && dialog.Bounds.Right <= 640 && dialog.Bounds.Bottom <= 480, $"{dialog.Bounds}");
        Assert.True(dialog.Bounds.Width > 0 && dialog.Bounds.Height > 0);
        // A smaller window keeps the dialog inside it.
        fixture.Resize(600, 440);
        Assert.True(dialog.Bounds.Right <= 600 && dialog.Bounds.Bottom <= 440, $"{dialog.Bounds}");
        dialog.Cancel();
    }

    [Fact]
    public async Task RemembersLastDirectoryPerPurpose()
    {
        using var fixture = ShellFixture.Open();
        string chosen = fixture.Directory.File("attachments");
        Directory.CreateDirectory(chosen);
        File.WriteAllBytes(Path.Combine(chosen, "a.txt"), [1]);
        var files = (MailFileDialogs)fixture.Shell.Files;

        var pick = files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file") { InitialDirectory = fixture.Directory.Root });
        files.ActiveDialog!.Accept(Path.Combine(chosen, "a.txt"));
        fixture.Dispatcher.Drain();
        Assert.Equal(MailFilePickStatus.Picked, (await pick).Status);
        Assert.Equal(chosen, files.LastDirectory(MailFilePurpose.Attachment));

        _ = files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"));
        Assert.Equal(chosen, Assert.IsType<StandardFileDialog>(files.ActiveDialog).CurrentDirectory);
        files.ActiveDialog!.Cancel();
        fixture.Dispatcher.Drain();

        _ = files.PickSaveFileAsync(new(MailFilePurpose.ExportOriginal, "Export as .eml"));
        Assert.NotEqual(chosen, Assert.IsType<StandardFileDialog>(files.ActiveDialog).CurrentDirectory);
        Assert.Null(files.LastDirectory(MailFilePurpose.ExportOriginal));
        files.ActiveDialog!.Cancel();
    }

    [Fact]
    public async Task PickDirectoryReturnsTheChosenFolder()
    {
        using var fixture = ShellFixture.Open();
        string parent = fixture.Directory.Root;
        Directory.CreateDirectory(Path.Combine(parent, "backups"));
        var pick = fixture.Shell.Files.PickDirectoryAsync(new(MailFilePurpose.BackupLocation, "Choose backup location") { InitialDirectory = parent });
        var dialog = Assert.IsType<StandardDialog>(fixture.Shell.ActiveFileDialog);
        Assert.Equal("Choose backup location", dialog.Title);
        var folder = MailFolderDialogTests.Content(dialog);
        Assert.Same(folder.Folders, fixture.Session.FocusedElement);
        Assert.Equal("Use this folder", folder.AcceptButton.Text);

        folder.Folders.SelectedItemId = Path.Combine(parent, "backups");
        folder.AcceptButton.Click();
        fixture.Dispatcher.Drain();

        var result = await pick;
        Assert.Equal(MailFilePickStatus.Picked, result.Status);
        Assert.Equal(Path.Combine(parent, "backups"), result.Item!.LocalPath);
        Assert.Equal("backups", result.Item.DisplayName);
    }

    [Fact]
    public async Task APickerOverABrokenOutFormOpensOverItAndEscapeClosesOnlyThePicker()
    {
        using var fixture = ShellFixture.Open(windowHost: true);
        fixture.Shell.ShowView("compose");
        var compose = fixture.Shell.ActiveDialog!;
        Assert.True(compose.IsBrokenOut);
        var composeSession = compose.Session!;
        var trigger = fixture.Shell.Composer.Body!;
        composeSession.SetFocus(trigger);

        var pick = fixture.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file"));
        var dialog = Assert.IsType<StandardFileDialog>(fixture.Shell.ActiveFileDialog);
        var dialogSession = dialog.Session!;
        Assert.Same(dialog, dialogSession.ModalElement);
        Assert.Same(dialog.FileNameEdit, dialogSession.FocusedElement);

        var keyboard = fixture.Shell.CreateKeyboardNavigation(fixture.Session);
        var escape = KeyInput.Key(0x1B);
        Assert.False(keyboard.Handle(escape));
        dialogSession.DispatchInput(escape);
        fixture.Dispatcher.Drain();

        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Null(fixture.Shell.ActiveFileDialog);
        Assert.Same(compose, fixture.Shell.ActiveDialog);
        Assert.Equal("compose", fixture.Shell.ActiveViewId);
        Assert.Same(trigger, composeSession.FocusedElement);
    }

    private static string[] PartFiles(TestDirectory directory) =>
        Directory.GetFiles(directory.Root, ".broiler-save-*.part");

    internal sealed class ShellFixture : IDisposable
    {
        private readonly ResizableHost _host;

        private ShellFixture(TestDirectory directory, ResizableHost host, TestQueueDispatcher dispatcher, MailShellView shell, UiSession session)
        {
            Directory = directory; _host = host; Dispatcher = dispatcher; Shell = shell; Session = session;
        }

        public TestDirectory Directory { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public MailShellView Shell { get; }
        public UiSession Session { get; }

        public static ShellFixture Open(int width = 1100, int height = 720, bool windowHost = false, IMailFilePicker? files = null)
        {
            var directory = new TestDirectory();
            directory.Create();
            var account = TestDirectory.Profile();
            var dispatcher = new TestQueueDispatcher();
            var receiver = new TestMailReceiver();
            var model = new MailShellViewModel(
                new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
                new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
                new(receiver, dispatcher),
                new ComposerViewModel(dispatcher: dispatcher));
            var shell = new MailShellView(model, files: files);
            ResizableHost host = windowHost ? new WindowHostingHost(width, height) : new ResizableHost(width, height);
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            shell.Attach(session);
            session.RenderFrame();
            return new(directory, host, dispatcher, shell, session);
        }

        public void Resize(int width, int height)
        {
            _host.Size = new BSize(width, height);
            Shell.Window.InvalidateMeasure();
            Session.RenderFrame();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Shell.Dispose();
            Session.Dispose();
            StandardControlPaint.ClearSessionTheme(Session);
            Directory.Dispose();
        }
    }

    internal class ResizableHost(int width, int height) : IUiHost
    {
        public BSize Size { get; set; } = new(width, height);
        public BSize ViewportSize => Size;
        public double Scale => 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    private sealed class WindowHostingHost(int width, int height) : ResizableHost(width, height), IUiWindowHost
    {
        public IUiHostWindow CreateHostWindow(UiHostWindowRequest request) => new HostWindow();
    }

    private sealed class HostWindow : IUiHostWindow
    {
        public event EventHandler? CloseRequested { add { } remove { } }
        public BSize ViewportSize => new(800, 600);
        public double Scale => 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
        public void Bind(UiSession session) { }
        public void Activate() { }
        public void SetTitle(string title) { }
        public void Dispose() { }
    }
}

/// <summary>Synthetic key presses, as the hosts deliver them.</summary>
internal static class KeyInput
{
    public static UiInputEvent Key(int code, Broiler.Input.Keyboard.KeyboardModifierState modifiers = Broiler.Input.Keyboard.KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new Broiler.Input.Keyboard.KeyboardKeyEvent(
            new Broiler.Input.InputEventHeader(Broiler.Input.InputDeviceId.FromOpaqueValue("test"), new Broiler.Input.InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            Broiler.Input.Keyboard.KeyboardKey.FromName("VirtualKey:" + code), Broiler.Input.Keyboard.KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: Broiler.Input.InputEventSource.Synthetic));
}
