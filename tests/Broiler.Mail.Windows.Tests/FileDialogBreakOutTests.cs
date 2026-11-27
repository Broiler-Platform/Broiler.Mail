// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Hosting.Windows.Accessibility;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Core.Settings;
using Broiler.Native.Windows.Accessibility;
using Broiler.UI;
using Broiler.UI.FileDialog.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// UX-03.2: in the real Windows window at its 640x480 minimum, a file dialog opens over the Compose form, whether
/// that form is in its own window or not; Escape closes only the file dialog, focus goes back to the control that
/// opened it, and the dialog's controls carry their names to UI Automation.
/// </summary>
[Collection("UI theme")]
public sealed class FileDialogBreakOutTests
{
    [Fact]
    public async Task FileDialogOverBrokenOutComposeAt640x480()
    {
        using var fixture = HiddenMailWindow.Start(demo: new DemoOptions(DemoScenario.Inbox, AppTheme.Light, 640, 480));
        var (to, _) = NativeInputFidelityTests.StartDraft(fixture);
        var compose = fixture.Ui(() => fixture.Window.Shell.ActiveDialog);
        Assert.NotNull(compose);
        var composeSession = fixture.Ui(() => compose.Session ?? fixture.Window.Session);
        Assert.Same(to, fixture.Ui(() => composeSession.FocusedElement));

        var pick = fixture.Ui(() => fixture.Window.Shell.Files.PickOpenFileAsync(new(MailFilePurpose.Attachment, "Attach file") { AcceptText = "Attach" }));
        fixture.Layout();
        var dialog = fixture.Ui(() => Assert.IsType<StandardFileDialog>(fixture.Window.Shell.ActiveFileDialog));
        // The dialog belongs to the Compose form: it was shown over it, and, unless it broke out into a window of its
        // own (which clears Owner), Compose is its owner in the window tree too.
        var files = Assert.IsType<MailFileDialogs>(fixture.Window.Shell.Files);
        Assert.Same(compose, fixture.Ui(() => files.ActiveOwner));
        Assert.NotSame(fixture.Window.Shell.Window, fixture.Ui(() => files.ActiveOwner));
        var (owner, brokenOut) = fixture.Ui(() => (dialog.Owner, dialog.IsBrokenOut));
        if (brokenOut) Assert.Null(owner);
        else Assert.Same(compose, owner);
        Assert.Equal("Attach file", fixture.Ui(() => dialog.Title));
        var dialogSession = fixture.Ui(() => dialog.Session!);
        Assert.Same(dialog, fixture.Ui(() => dialogSession.ModalElement));
        Assert.Same(dialog.FileNameEdit, fixture.Ui(() => dialogSession.FocusedElement));
        // In the main window, or in a window of its own: either way inside what the person sees.
        fixture.Ui(() => dialogSession.RenderFrame());
        var bounds = fixture.Ui(() => dialog.Bounds);
        var viewport = fixture.Ui(() => dialogSession.Host.ViewportSize);
        Assert.True(bounds.Width > 0 && bounds.Right <= viewport.Width + 0.5 && bounds.Bottom <= viewport.Height + 0.5, $"{bounds} in {viewport}");

        // Escape, through the window's keyboard filter, closes only the file dialog.
        Assert.False(fixture.Ui(() =>
        {
            var escape = Key(0x1B);
            bool handled = fixture.Window.Shell.CreateKeyboardNavigation(fixture.Window.Session).Handle(escape);
            if (!handled) dialogSession.DispatchInput(escape);
            return handled;
        }));
        fixture.WaitUntil(() => pick.IsCompleted, "the pick completes");
        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Null(fixture.Ui(() => fixture.Window.Shell.ActiveFileDialog));
        Assert.Same(compose, fixture.Ui(() => fixture.Window.Shell.ActiveDialog));
        Assert.Equal("compose", fixture.Ui(() => fixture.Window.Shell.ActiveViewId));
        Assert.Same(to, fixture.Ui(() => composeSession.FocusedElement));
    }

    [Fact]
    public async Task FileDialogUiaNames()
    {
        using var fixture = HiddenMailWindow.Start(demo: new DemoOptions(DemoScenario.Inbox, AppTheme.Light, 640, 480));
        NativeInputFidelityTests.StartDraft(fixture);
        var pick = fixture.Ui(() => fixture.Window.Shell.Files.PickSaveFileAsync(new(MailFilePurpose.ExportOriginal, "Export as .eml")
        {
            Types = [MailFileType.Email], SuggestedFileName = "message", AcceptText = "Export",
        }));
        fixture.Layout();
        var names = fixture.Ui(() =>
        {
            var dialog = Assert.IsType<StandardFileDialog>(fixture.Window.Shell.ActiveFileDialog);
            var session = dialog.Session ?? fixture.Window.Session;
            session.RenderFrame();
            var bridge = new WindowsAutomationBridge(nint.Zero, session, dialog);
            var found = new List<string>();
            void Walk(IRawElementProviderFragment node)
            {
                if (node.GetPropertyValue(UiaNative.UiaNamePropertyId) is string name) found.Add(name);
                for (var child = node.Navigate(NavigateDirection.FirstChild); child != null; child = child.Navigate(NavigateDirection.NextSibling))
                    Walk(child);
            }
            Walk(bridge);
            return found;
        });
        foreach (var expected in new[] { "File name", "Places", "Files", "Folders", "File type", "Sort files by", "Export", "Cancel" })
            Assert.Contains(expected, names);

        fixture.Ui(() => fixture.Window.Shell.CloseDialog());
        fixture.WaitUntil(() => pick.IsCompleted, "the pick completes");
        Assert.Equal(MailFilePickStatus.Canceled, (await pick).Status);
        Assert.Equal("compose", fixture.Ui(() => fixture.Window.Shell.ActiveViewId));
    }

    /// <summary>
    /// UX-03.5d: Ctrl+O in the reading window opens the real Open .eml file dialog over the main window and nothing
    /// else; Escape closes only the picker, and Mail opens no dialog of its own when nothing was chosen.
    /// </summary>
    [Fact]
    public void CtrlOOpensFileDialogAndEscapeClosesOnlyThePicker()
    {
        var root = Path.Combine(Path.GetTempPath(), "broiler-mail-ctrl-o-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var fixture = HiddenMailWindow.Start(() =>
            {
                var demo = DemoApplication.Create(new DemoOptions(DemoScenario.Inbox, AppTheme.Light, 640, 480));
                return new MailApplication(demo.Accounts, demo.Settings, demo.Receiver, demo.Sender, demo.Credentials,
                    sourceExports: new FileMessageSourceExports(root));
            }, new DemoOptions(DemoScenario.Inbox, AppTheme.Light, 640, 480));
            var shell = fixture.Window.Shell;
            fixture.WaitUntil(() => shell.ActiveDialog is null && shell.CanOpenLocalOriginal, "the inbox can open local originals");
            Assert.True(fixture.Ui(() => shell.CreateKeyboardNavigation(fixture.Window.Session).Handle(Key('O', KeyboardModifierState.Control))));
            fixture.Layout();
            var dialog = fixture.Ui(() => Assert.IsType<StandardFileDialog>(shell.ActiveFileDialog));
            Assert.Equal("Open .eml file", fixture.Ui(() => dialog.Title));
            var dialogSession = fixture.Ui(() => dialog.Session!);
            // The file dialog is the only modal: the exemption covers the dialog the person asked for, and nothing else.
            Assert.Same(dialog, fixture.Ui(() => dialogSession.ModalElement));
            Assert.Null(fixture.Ui(() => shell.ActiveDialog));
            Assert.Null(fixture.Ui(() => shell.ActiveAboutDialog));
            Assert.Equal("inbox", fixture.Ui(() => shell.ActiveViewId));

            Assert.False(fixture.Ui(() =>
            {
                var escape = Key(0x1B);
                bool handled = shell.CreateKeyboardNavigation(fixture.Window.Session).Handle(escape);
                if (!handled) dialogSession.DispatchInput(escape);
                return handled;
            }));
            fixture.WaitUntil(() => shell.ActiveFileDialog is null, "the picker closes");
            fixture.Layout();
            Assert.Null(fixture.Ui(() => shell.ActiveDialog));
            Assert.Equal("inbox", fixture.Ui(() => shell.ActiveViewId));
            Assert.Null(fixture.Ui(() => fixture.Window.Session.ModalElement));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, modifiers, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));
}
