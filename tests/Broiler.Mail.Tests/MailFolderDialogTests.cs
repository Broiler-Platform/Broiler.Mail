// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Window.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.2: the folder dialog selects without navigating, opens on Enter, and accepts only when asked to.</summary>
[Collection("UI theme")]
public sealed class MailFolderDialogTests
{
    private static readonly string Root = InMemoryFileDialogDirectoryProvider.PathOf("root");
    private static readonly string Mail = Path.Combine(Root, "Mail");
    private static readonly string Photos = Path.Combine(Root, "Photos");

    [Fact]
    public async Task SelectingFolderDoesNotNavigateEnterDoes()
    {
        using var fixture = Fixture.Open(Provider());
        var folder = fixture.Folder;
        Assert.Equal(Root, folder.CurrentDirectory);
        Assert.Equal(["Mail", "Photos"], folder.Folders.Items.Select(item => item.Text));
        fixture.Session.SetFocus(folder.Folders);

        fixture.Session.DispatchInput(KeyInput.Key(0x28)); // Down selects Mail, without opening it.
        Assert.Equal(Mail, folder.Folders.SelectedItemId);
        Assert.Equal(Root, folder.CurrentDirectory);
        Assert.Equal($"Choose folder: {Mail}", folder.Message);

        fixture.Session.DispatchInput(KeyInput.Key(0x0D)); // Enter opens it.
        Assert.Equal(Mail, folder.CurrentDirectory);
        Assert.Equal(["Archive"], folder.Folders.Items.Select(item => item.Text));
        Assert.False(fixture.Dialog.IsResultCompleted);

        fixture.Session.SetFocus(folder.Folders);
        fixture.Session.DispatchInput(KeyInput.Key(0x08)); // Backspace goes up.
        Assert.Equal(Root, folder.CurrentDirectory);

        folder.Navigate(Mail);
        fixture.Session.SetFocus(folder.PathEdit);
        Assert.True(fixture.Keyboard(KeyInput.Key(0x26, KeyboardModifierState.Alt))); // Alt+Up goes up from anywhere.
        Assert.Equal(Root, folder.CurrentDirectory);

        folder.PathEdit.Text = Photos;
        fixture.Session.DispatchInput(KeyInput.Key(0x0D)); // Enter in the path goes there.
        Assert.Equal(Photos, folder.CurrentDirectory);
        Assert.False(fixture.Dialog.IsResultCompleted);
        Assert.Equal(MailFilePickStatus.Canceled, await fixture.CancelAsync());
    }

    [Fact]
    public async Task AcceptReturnsSelectedSubfolderElseCurrent()
    {
        using (var fixture = Fixture.Open(Provider()))
        {
            fixture.Folder.Folders.SelectedItemId = Photos;
            Assert.Equal($"Choose folder: {Photos}", fixture.Folder.Message);
            fixture.Folder.AcceptButton.Click();
            Assert.Equal(Photos, await fixture.ResultAsync());
        }
        using (var fixture = Fixture.Open(Provider()))
        {
            Assert.Equal($"Choose folder: {Root}", fixture.Folder.Message);
            fixture.Folder.AcceptButton.Click();
            Assert.Equal(Root, await fixture.ResultAsync());
        }
        using (var fixture = Fixture.Open(Provider()))
        {
            // Ctrl+Enter accepts even from the path field, which takes a plain Enter itself.
            fixture.Session.SetFocus(fixture.Folder.PathEdit);
            Assert.True(fixture.Keyboard(KeyInput.Key(0x0D, KeyboardModifierState.Control)));
            Assert.Equal(Root, await fixture.ResultAsync());
        }
    }

    [Fact]
    public async Task AcceptUsesATypedPathThatWasNotOpenedYet()
    {
        using (var fixture = Fixture.Open(Provider()))
        {
            // Typed, not submitted: Ctrl+Enter opens the typed folder and chooses it, never the one listed before.
            fixture.Session.SetFocus(fixture.Folder.PathEdit);
            fixture.Folder.PathEdit.Text = Photos;
            Assert.True(fixture.Folder.HasTypedPath);
            Assert.Equal($"Choose folder: {Photos}. It is opened first.", fixture.Folder.Message);
            Assert.True(fixture.Keyboard(KeyInput.Key(0x0D, KeyboardModifierState.Control)));
            Assert.Equal(Photos, await fixture.ResultAsync());
        }
        using (var fixture = Fixture.Open(Provider()))
        {
            // The typed path wins over a selected subfolder, as the field shows it; so does the accept button.
            fixture.Folder.Folders.SelectedItemId = Photos;
            fixture.Folder.PathEdit.Text = Mail;
            fixture.Folder.AcceptButton.Click();
            Assert.Equal(Mail, await fixture.ResultAsync());
        }
    }

    [Fact]
    public async Task ATypedPathThatCannotBeOpenedIsNotAcceptedAndSaysWhy()
    {
        string locked = Path.Combine(Root, "Locked");
        using var fixture = Fixture.Open(Provider().Fail(locked, new UnauthorizedAccessException("Access is denied.")));
        fixture.Folder.PathEdit.Text = locked;

        Assert.False(fixture.Folder.Accept());

        Assert.False(fixture.Dialog.IsResultCompleted);
        Assert.Equal("Cannot open Locked: Access is denied.", fixture.Folder.Message);
        Assert.Equal(Root, fixture.Folder.PathEdit.Text);
        Assert.False(fixture.Folder.HasTypedPath);

        // A cleared field is not a folder either.
        fixture.Folder.PathEdit.Text = "";
        Assert.Equal("Type a folder path.", fixture.Folder.Message);
        Assert.False(fixture.Folder.Accept());
        Assert.False(fixture.Dialog.IsResultCompleted);
        Assert.Equal("Type a folder path.", fixture.Folder.Message);
        Assert.Equal(Root, fixture.Folder.PathEdit.Text);

        // With the field showing the listed folder again, Accept chooses it.
        Assert.True(fixture.Folder.Accept());
        Assert.Equal(Root, await fixture.ResultAsync());
    }

    [Fact]
    public async Task AcceptWhileATypedFolderLoadsWaitsForItsListing()
    {
        var provider = Provider();
        var slow = provider.Hold(Mail);
        using var fixture = Fixture.Open(provider);
        fixture.Folder.PathEdit.Text = Mail;

        Assert.False(fixture.Folder.Accept());
        Assert.True(fixture.Folder.IsLoading);
        Assert.False(fixture.Dialog.IsResultCompleted);

        slow.SetResult(InMemoryFileDialogDirectoryProvider.Subfolders(Mail, "Archive"));
        Assert.Equal(Mail, await fixture.ResultAsync());
    }

    [Fact]
    public async Task EnterNeverAcceptsImplicitly()
    {
        using var fixture = Fixture.Open(Provider());
        foreach (var target in new UiElement[] { fixture.Folder.PathEdit, fixture.Folder.Places, fixture.Folder.Folders, fixture.Folder.UpButton })
        {
            fixture.Folder.Navigate(Root);
            fixture.Session.SetFocus(target);
            fixture.Keyboard(KeyInput.Key(0x0D));
            Assert.False(fixture.Dialog.IsResultCompleted, target.GetType().Name);
            Assert.Same(fixture.Dialog, fixture.Session.ModalElement);
        }
        Assert.False(fixture.Folder.AcceptButton.IsDefault);
        Assert.Equal(MailFilePickStatus.Canceled, await fixture.CancelAsync());
    }

    [Fact]
    public async Task UnreadableFolderShowsMessageAndStaysOpen()
    {
        string locked = Path.Combine(Root, "Locked");
        var provider = Provider().Fail(locked, new UnauthorizedAccessException("Access is denied."));
        using var fixture = Fixture.Open(provider);

        fixture.Folder.Navigate(locked);

        Assert.Equal(Root, fixture.Folder.CurrentDirectory);
        Assert.Equal(Root, fixture.Folder.PathEdit.Text);
        Assert.Equal("Cannot open Locked: Access is denied.", fixture.Folder.Message);
        Assert.True(fixture.Dialog.IsPresented);
        Assert.Same(fixture.Dialog, fixture.Session.ModalElement);
        fixture.Folder.AcceptButton.Click();
        Assert.Equal(Root, await fixture.ResultAsync());
    }

    [Fact]
    public async Task AsyncListingIgnoresStaleResults()
    {
        var provider = Provider();
        var slow = provider.Hold(Mail);
        var quick = provider.Hold(Photos);
        using var fixture = Fixture.Open(provider);

        fixture.Folder.Navigate(Mail);
        Assert.True(fixture.Folder.IsLoading);
        Assert.Equal("Loading Mail…", fixture.Folder.Message);
        fixture.Folder.Navigate(Photos);
        quick.SetResult(InMemoryFileDialogDirectoryProvider.Subfolders(Photos, "2025", "2026"));
        fixture.Dispatcher.Drain();
        Assert.Equal(Photos, fixture.Folder.CurrentDirectory);

        // The first folder's listing arrives last and is ignored.
        slow.SetResult(InMemoryFileDialogDirectoryProvider.Subfolders(Mail, "Archive"));
        fixture.Dispatcher.Drain();
        Assert.Equal(Photos, fixture.Folder.CurrentDirectory);
        Assert.Equal(["2025", "2026"], fixture.Folder.Folders.Items.Select(item => item.Text));
        Assert.False(fixture.Folder.IsLoading);
        Assert.Equal(MailFilePickStatus.Canceled, await fixture.CancelAsync());
    }

    [Fact]
    public async Task TabOrderFollowsThePathTheListsAndTheButtons()
    {
        using var fixture = Fixture.Open(Provider());
        var stops = MailKeyboardNavigation.TabStops(fixture.Folder);
        Assert.Equal(
            ["Folder path", "Go to typed folder", "Up one level", "Places", "Folders in root", fixture.Folder.AcceptButton.Text, "Cancel"],
            stops.Select(stop => stop.GetSemanticNode().Name));
        Assert.Equal(MailFilePickStatus.Canceled, await fixture.CancelAsync());
    }

    internal static MailFolderDialog Content(StandardDialog dialog) => dialog.Children.OfType<MailFolderDialog>().Single();

    private static InMemoryFileDialogDirectoryProvider Provider() => new InMemoryFileDialogDirectoryProvider()
        .Folder(Root, ["Photos", "Mail"], ["readme.txt"])
        .Folder(Mail, ["Archive"])
        .Folder(Photos, [])
        .Folder(Path.Combine(Mail, "Archive"), []);

    /// <summary>A shell window with the folder dialog of a <see cref="MailFileDialogs"/> open over it.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly StandardWindow _window;
        private readonly MailFileDialogs _files;
        private readonly Task<MailFilePick<IMailPickedDirectory>> _pick;

        private Fixture(StandardWindow window, MailFileDialogs files, UiSession session, TestQueueDispatcher dispatcher, Task<MailFilePick<IMailPickedDirectory>> pick)
        {
            _window = window; _files = files; Session = session; Dispatcher = dispatcher; _pick = pick;
            Dialog = Assert.IsType<StandardDialog>(files.ActiveDialog);
            Folder = Content(Dialog);
        }

        public UiSession Session { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public StandardDialog Dialog { get; }
        public MailFolderDialog Folder { get; }

        public static Fixture Open(InMemoryFileDialogDirectoryProvider provider)
        {
            var dispatcher = new TestQueueDispatcher();
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());
            var window = new StandardWindow { Title = "Mail" };
            session.AddRoot(window);
            session.RenderFrame();
            var files = new MailFileDialogs(() => window, provider);
            var pick = files.PickDirectoryAsync(new(MailFilePurpose.BackupLocation, "Choose backup location") { InitialDirectory = Root });
            session.RenderFrame();
            return new(window, files, session, dispatcher, pick);
        }

        /// <summary>What the shell's keyboard filter would do: the folder dialog's own keys first.</summary>
        public bool Keyboard(UiInputEvent input) => _files.PreviewInput(input) || Session.DispatchInput(input);

        public async Task<string?> ResultAsync()
        {
            Dispatcher.Drain();
            var result = await _pick;
            Assert.Equal(MailFilePickStatus.Picked, result.Status);
            return result.Item!.LocalPath;
        }

        public async Task<MailFilePickStatus> CancelAsync()
        {
            Dialog.Cancel();
            Dispatcher.Drain();
            return (await _pick).Status;
        }

        public void Dispose()
        {
            _files.Dispose();
            Session.Dispose();
            _window.Dispose();
        }
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(900, 640);
        public double Scale => 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
