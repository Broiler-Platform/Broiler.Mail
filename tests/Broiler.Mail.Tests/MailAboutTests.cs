// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.AboutDialog.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class MailAboutTests
{
    [Theory]
    [InlineData(1200, 800, 0x0D, UiDialogResultKind.Accepted)]
    [InlineData(380, 300, 0x1B, UiDialogResultKind.Cancelled)]
    public async Task About_Menu_Shows_Mail_Metadata_And_Restores_Reader_Focus(
        double width, double height, int keyCode, UiDialogResultKind result)
    {
        using var directory = new TestDirectory();
        var account = TestDirectory.Profile();
        var message = new MailMessageSummary { Key = new(account.Id, "INBOX", 7, 1), Sender = "Author <author@example.test>", Subject = "Subject" };
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([message], null)),
            Body = (key, _) => Task.FromResult(new MailMessageBody(key, "Some message text.")),
        };
        var dispatcher = new TestQueueDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, account, null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        Assert.Equal("Broiler Mail", model.Title);

        using var renderer = new BImageRenderer();
        var host = new ImageHost(new BSize(width, height), renderer);
        Uri? openedUrl = null;
        using var shell = new MailShellView(model, openUrl: url => openedUrl = url);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
        shell.Attach(session);
        shell.ShowView("inbox");
        await model.Inbox.ReceiveAsync();
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        await model.Inbox.SelectAsync(message.Key);
        dispatcher.DrainUntil(() => !model.Inbox.IsBusy);
        session.RenderFrame();

        var keyboard = shell.CreateKeyboardNavigation(session);
        bool Dispatch(UiInputEvent input) => keyboard.Handle(input) || session.DispatchInput(input);

        var list = Descendants(shell.GetContent("inbox")).OfType<StandardListView>().Single();
        session.SetFocus(list);

        // Open Help -> About Broiler Mail via keyboard navigation on the menu bar
        Assert.True(Dispatch(Key(0x79))); // F10
        Assert.True(shell.Menu.IsOpen);
        Assert.True(Dispatch(Key(0x27))); // Message
        Assert.True(Dispatch(Key(0x27))); // Tools
        Assert.True(Dispatch(Key(0x27))); // Help
        Assert.True(Dispatch(Key(0x28))); // About Broiler Mail
        Assert.True(Dispatch(Key(0x0D))); // Enter
        dispatcher.Drain();

        using var dialog = Assert.IsType<StandardAboutDialog>(shell.ActiveAboutDialog);
        Assert.Same(dialog, session.FocusedElement);
        BRenderList frame = session.RenderFrame();

        string version = typeof(MailShellView).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        Assert.Equal("Broiler Mail", dialog.ProductName);
        Assert.Equal("About Broiler Mail", dialog.Title);
        Assert.Equal(version, dialog.ProductVersion);
        Assert.Equal(version, dialog.ComponentVersions["Broiler.Mail.Application"]);
        Assert.Contains("Broiler.UI.AboutDialog.Standard", dialog.ComponentVersions.Keys);
        Assert.NotNull(dialog.Picture);
        Assert.True(dialog.Picture.HasIntrinsicSize);
        Assert.Equal("https://broiler-platform.github.io/", dialog.ProductUrl!.AbsoluteUri);
        BRenderCommand.DrawImage picture = Assert.Single(frame.Commands.OfType<BRenderCommand.DrawImage>());
        Assert.Equal((double)dialog.Picture.PixelWidth!.Value / dialog.Picture.PixelHeight!.Value,
            picture.Destination.Width / picture.Destination.Height, 6);
        dialog.WebsiteButton.Click();
        Assert.Equal(dialog.ProductUrl, openedUrl);
        Assert.True(dialog.IsPresented);
        Assert.True(dialog.IsModal);
        Assert.InRange(dialog.Bounds.Left, 0, width);
        Assert.InRange(dialog.Bounds.Right, 0, width);
        Assert.InRange(dialog.Bounds.Top, 0, height);
        Assert.InRange(dialog.Bounds.Bottom, 0, height);
        Assert.True(dialog.OkButton.Bounds.Bottom <= dialog.Bounds.Bottom);

        session.SetFocus(dialog.ComponentList);
        Assert.True(Dispatch(Key(keyCode)));

        Assert.False(dialog.IsPresented);
        Assert.Null(shell.ActiveAboutDialog);
        Assert.Equal(result, dialog.CompletedResult.Kind);
        Assert.Same(list, session.FocusedElement);

        Assert.True(shell.ShowAbout());
        using var reopened = Assert.IsType<StandardAboutDialog>(shell.ActiveAboutDialog);
        Assert.NotSame(dialog, reopened);
        Assert.True(shell.CloseDialog());
        Assert.Null(shell.ActiveAboutDialog);
    }

    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("mail-about-test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName(code == 0x0D ? "Enter" : code == 0x1B ? "Escape" : "VirtualKey:" + code),
            KeyboardKeyTransition.Down, modifiers, code, 0, 1, false, false,
            Source: InputEventSource.Synthetic));

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class ImageHost(BSize viewportSize, IBroilerRenderer renderer) : IUiHost, IUiImageHost
    {
        public BSize ViewportSize => viewportSize;
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
        public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage) => renderer.CreateImage(encodedImage);
        public BImageHandle CreateImage(BPixelBuffer pixels) => renderer.CreateImage(pixels);
        public void ReleaseImage(BImageHandle image) => renderer.ReleaseImage(image);
    }
}
