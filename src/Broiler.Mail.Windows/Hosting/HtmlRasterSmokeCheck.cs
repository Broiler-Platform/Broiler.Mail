using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.UI;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Exercises the actual HTML layout/raster backend without opening a native window.</summary>
internal static class HtmlRasterSmokeCheck
{
    public static void Run()
    {
        MessageFindSmokeCheck.Run();
        var document = HtmlPreviewPolicy.Create("<h1>Published HTML fixture</h1><table><tr><td>Readable</td></tr></table>" +
            "<script>throw new Error('must be removed')</script><img src='https://example.test/blocked.png'>");
        if (document.Html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HTML policy retained executable content.");
        using var host = new WindowsHtmlPreviewHost();
        var key = new Broiler.Mail.Core.Messages.MailMessageKey(new(Guid.NewGuid()), "INBOX", 7, 1);
        using var surface = host.CreateInlineContent(new(key, "Published HTML fixture", document.Html));
        if (surface is not ScrollableHtmlView view)
            throw new InvalidOperationException("Inline HTML must use a reader surface without a preview window.");
        var element = view.Content;
        UiInputEvent ZoomKey(int code) => UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("published-inline"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "smoke"), 1),
            KeyboardKey.FromName("VirtualKey:" + code), KeyboardKeyTransition.Down, KeyboardModifierState.Control, code, 0, 0, false, false,
            Source: InputEventSource.Synthetic));
        if (!view.HandleReaderInput(ZoomKey(0xBB))) throw new InvalidOperationException("Inline zoom shortcut was not handled.");
        if (view.Zoom <= 1) throw new InvalidOperationException("Inline zoom did not advance.");
        if (!view.HandleReaderInput(ZoomKey(0x30))) throw new InvalidOperationException("Inline reset shortcut was not handled.");
        if (view.Zoom != 1) throw new InvalidOperationException("Inline zoom reset failed.");
        var layout = element.CalculateLayout(640);
        if (!double.IsFinite(layout.ContentHeight) || layout.ContentHeight <= 0 ||
            !layout.Diagnostics.Any(box => box.TagName == "table"))
            throw new InvalidOperationException("Published HTML layout fixture failed.");
        using var bitmap = element.PaintTile(0, 1, 640, 256);
        var pixels = bitmap.ToPixelBuffer();
        int visiblePixels = 0;
        for (int i = 0; i < pixels.Rgba.Length; i += 4)
            if (pixels.Rgba[i + 3] > 0 && (pixels.Rgba[i] < 200 || pixels.Rgba[i + 1] < 200 || pixels.Rgba[i + 2] < 200))
                visiblePixels++;
        if (visiblePixels < 20) throw new InvalidOperationException("Published HTML raster produced no visible text.");
        var contrast = Broiler.UI.Standard.StandardThemeTokens.HighContrastDark;
        host.ApplyTheme(contrast);
        element.CalculateLayout(640);
        using var contrastBitmap = element.PaintTile(0, 1, 640, 256);
        var contrastPixels = contrastBitmap.ToPixelBuffer().Rgba;
        int background = 0, foreground = 0;
        for (int i = 0; i < contrastPixels.Length; i += 4)
        {
            if (contrastPixels[i + 3] < 200) continue;
            if (contrastPixels[i] == contrast.Surface.R && contrastPixels[i + 1] == contrast.Surface.G && contrastPixels[i + 2] == contrast.Surface.B) background++;
            if (contrastPixels[i] == contrast.Text.R && contrastPixels[i + 1] == contrast.Text.G && contrastPixels[i + 2] == contrast.Text.B) foreground++;
        }
        if (background < 1000 || foreground < 20) throw new InvalidOperationException("Published high-contrast reader palette failed.");
        string longText = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "Published paragraph " + i + ": readable text for viewport restoration."));
        string longHtml = string.Concat(Enumerable.Range(1, 80).Select(i => "<p>Published paragraph " + i + ": readable text for viewport restoration.</p>"));
        using var viewportSurface = host.CreateInlineContent(new(key with { Uid = 2 }, longText, longHtml));
        var htmlViewport = (Broiler.Mail.Application.Preview.IReaderViewport)viewportSurface;
        htmlViewport.RestoreReadingPosition(0.6); viewportSurface.Measure(new(640, 480)); viewportSurface.Arrange(new(0, 0, 640, 480));
        if (Math.Abs(htmlViewport.ReadingPosition - 0.6) > 0.01) throw new InvalidOperationException("Published HTML viewport restore failed.");
        using var textViewport = new Broiler.Mail.Application.Preview.ScrollableMessageText { Text = longText };
        textViewport.RestoreReadingPosition(htmlViewport.ReadingPosition); textViewport.Measure(new(640, 480)); textViewport.Arrange(new(0, 0, 640, 480));
        if (Math.Abs(textViewport.ReadingPosition - htmlViewport.ReadingPosition) > 0.01) throw new InvalidOperationException("Published HTML/text viewport transfer failed.");
        textViewport.RestoreReadingPosition(1); textViewport.Measure(new(640, 480)); textViewport.Arrange(new(0, 0, 640, 480));
        if (Math.Abs(textViewport.ReadingPosition - 1) > 0.01) throw new InvalidOperationException("Published reader end navigation failed.");
        ((ScrollableHtmlView)viewportSurface).UpdateHtml("<p>New message</p>"); viewportSurface.Measure(new(640, 480)); viewportSurface.Arrange(new(0, 0, 640, 480));
        if (htmlViewport.ReadingPosition != 0) throw new InvalidOperationException("Published new document retained old reading position.");

        host.CreateImageBroker = () => throw new InvalidOperationException("Local HTML must not create an image broker.");
        using var local = (ScrollableHtmlView)host.CreateLocalOriginalContent(
            "<table><tr><td>Local original</td></tr></table><a href='https://example.test/'>Inactive link</a>" +
            "<img src='https://example.test/blocked.png'><img src='data:image/png;base64," + RemoteImageBrokerSmokeCheck.Pixel + "'>");
        var localLayout = local.Content.CalculateLayout(640);
        if (!localLayout.Diagnostics.Any(box => box.TagName == "table") || localLayout.Diagnostics.Any(box => box.TagName == "img") ||
            localLayout.Links.Count != 0 || local.RemoteImages.Total != 0 || local.RemoteImages.CanLoad)
            throw new InvalidOperationException("Published local HTML formatting or resource policy failed.");
        using var localBitmap = local.Content.PaintTile(0, 1, 640, 256);
        if (localBitmap.ToPixelBuffer().Rgba.Length == 0) throw new InvalidOperationException("Published local HTML produced no raster.");
    }
}
