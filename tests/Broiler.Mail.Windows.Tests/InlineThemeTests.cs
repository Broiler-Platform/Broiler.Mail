using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
public sealed class InlineThemeTests
{
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("contrast-light")]
    [InlineData("contrast-dark")]
    public void ActualRasterUsesReaderPaletteForPageTextAndLinks(string name)
    {
        var theme = name switch
        {
            "dark" => StandardThemeTokens.Dark, "contrast-light" => StandardThemeTokens.HighContrastLight,
            "contrast-dark" => StandardThemeTokens.HighContrastDark, _ => StandardThemeTokens.Light,
        };
        using var host = name == "dark" ? new WindowsHtmlPreviewHost() : new WindowsHtmlPreviewHost(() => theme);
        if (name == "dark") host.ApplyTheme(theme); // A new document also inherits a previously applied palette.
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(Message()));
        surface.Content.CalculateLayout(640);
        using var bitmap = surface.Content.PaintTile(0, 1, 640, 256);
        var pixels = bitmap.ToPixelBuffer().Rgba;
        Assert.True(Count(pixels, theme.Surface) > 1000, "Page background must use the reader palette.");
        Assert.True(Count(pixels, theme.Text) > 10, "Readable text must use the reader palette.");
        Assert.True(Count(pixels, theme.AccentText) > 5, "Links must use the palette's text accent.");
    }

    [Fact]
    public void RecoloringChangesOnlyHostStyleAndPreservesContentAndResourceRestrictions()
    {
        const string visible = "Literal color:#202124;background:white and a{color:#185abc}";
        var document = HtmlPreviewPolicy.Create($"<p style='background:red' onclick='bad()'>{visible}</p><style>body{{background:red}}</style><img src='https://images.example.test/a'>");
        string html = InlineReaderTheme.Apply(document.Html, StandardThemeTokens.HighContrastDark);
        Assert.Contains(visible, html);
        Assert.DoesNotContain("background:red", html); Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("src=\"https:", html); Assert.Contains("default-src 'none'", html);
        Assert.Contains("text-decoration:underline", html);
    }

    [Fact]
    public void LivePaletteChangesPreserveZoomReadingPositionAndUsableFocus()
    {
        using var host = new WindowsHtmlPreviewHost(() => StandardThemeTokens.Light);
        var key = new MailMessageKey(new(Guid.NewGuid()), "INBOX", 7, 1);
        using var surface = Assert.IsType<ScrollableHtmlView>(host.CreateInlineContent(new(key, "Fallback",
            string.Concat(Enumerable.Repeat("<p>Readable paragraph for reflow.</p>", 80)) + "<a href='https://example.test/end'>Closing link</a>")));
        var dispatcher = new Queue(); using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(surface); session.RenderFrame();
        surface.Scroll.SetOffset(new(0, 700)); session.RenderFrame(); surface.ZoomBy(1); session.RenderFrame();
        double zoom = surface.Zoom; double fraction = surface.Scroll.VerticalOffset / (surface.Scroll.ExtentSize.Height - surface.Scroll.ViewportSize.Height);
        host.ApplyTheme(StandardThemeTokens.Dark); dispatcher.Drain(); session.RenderFrame();
        Assert.Equal(zoom, surface.Zoom);
        double after = surface.Scroll.VerticalOffset / (surface.Scroll.ExtentSize.Height - surface.Scroll.ViewportSize.Height);
        Assert.InRange(after, fraction - 0.02, fraction + 0.02);
        session.SetFocus(surface.Content.LinkTargets.Single());
        host.ApplyTheme(StandardThemeTokens.HighContrastDark); dispatcher.Drain(); session.RenderFrame();
        Assert.True(session.FocusedElement == surface.Content || session.FocusedElement is HtmlLinkTarget);
        host.ApplyTheme(StandardThemeTokens.Light); dispatcher.Drain(); session.RenderFrame();
        using var bitmap = surface.Content.PaintTile(0, 1, 640, 256);
        Assert.True(Count(bitmap.ToPixelBuffer().Rgba, StandardThemeTokens.Light.Surface) > 1000);
    }

    private static MailMessageBody Message() => new(new(new(Guid.NewGuid()), "INBOX", 7, 1), "Fallback",
        "<p>Readable theme fixture</p><a href='https://example.test/link'>Visible link</a><table><tr><td>Cell</td></tr></table><img src='https://images.example.test/blocked'>");
    private static int Count(byte[] pixels, BColor color)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 200 && Math.Abs(pixels[i] - color.R) < 8 && Math.Abs(pixels[i + 1] - color.G) < 8 && Math.Abs(pixels[i + 2] - color.B) < 8) count++;
        return count;
    }
    private sealed class Queue : IUiDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _actions = new();
        public bool CheckAccess() => true; public void Post(Action action) => _actions.Enqueue(action);
        public void Drain() { while (_actions.TryDequeue(out var action)) action(); }
    }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList list) { }
    }
}
