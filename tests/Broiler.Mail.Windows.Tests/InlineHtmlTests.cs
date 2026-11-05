using System.Net;
using System.Net.Sockets;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Windows.Preview;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
public sealed class InlineHtmlTests
{
    [Fact]
    public void InlineZoomUsesSystemTextDefaultAndClampsWhileReflowingLongMail()
    {
        using var host = new WindowsHtmlPreviewHost(() => Broiler.UI.Standard.StandardThemeTokens.Light.WithTextScale(1.5));
        var key = new MailMessageKey(new(Guid.NewGuid()), "INBOX", 7, 1);
        using var surface = host.CreateInlineContent(new(key, "Fallback", string.Concat(Enumerable.Repeat("<p>Long inline message with readable text.</p>", 100))));
        var html = Assert.IsType<ScrollableHtmlView>(surface);
        Assert.Equal(1.5, html.Zoom);
        html.Measure(new(640, 480)); html.Arrange(new(0, 0, 640, 480));
        double normalHeight = html.Scroll.ExtentSize.Height;
        for (int i = 0; i < 30; i++) html.ZoomBy(1);
        Assert.Equal(3, html.Zoom); Assert.False(html.CanZoomIn);
        html.Measure(new(640, 480)); html.Arrange(new(0, 0, 640, 480));
        Assert.True(html.Scroll.ExtentSize.Height > normalHeight);
        for (int i = 0; i < 30; i++) html.ZoomBy(-1);
        Assert.Equal(0.5, html.Zoom); Assert.False(html.CanZoomOut);
        html.ResetZoom(); Assert.Equal(1.5, html.Zoom);
        host.ApplyTheme(Broiler.UI.Standard.StandardThemeTokens.Dark.WithTextScale(1.25));
        Assert.Equal(1.25, html.Zoom);
        html.ZoomBy(1); double custom = html.Zoom;
        host.ApplyTheme(Broiler.UI.Standard.StandardThemeTokens.Light.WithTextScale(1.75));
        Assert.Equal(custom, html.Zoom);
        html.ResetZoom(); Assert.Equal(1.75, html.Zoom);
    }

    [Fact]
    public void ManagedPaintFailureIsReportedOnceAndStopsRepeatedRendering()
    {
        using var host = new WindowsHtmlPreviewHost { RendererProvider = () => throw new InvalidOperationException("Fixture renderer unavailable") };
        var key = new MailMessageKey(new(Guid.NewGuid()), "INBOX", 7, 1);
        using var surface = host.CreateInlineContent(new(key, "Fallback", "<p>Readable</p>"));
        int failures = 0;
        ((Broiler.Mail.Application.Preview.IInlineHtmlReader)surface).RenderingFailed += (_, _) => failures++;
        using var session = new Broiler.UI.Standard.StandardUiSessionBuilder().Build(new UiHost());
        session.AddRoot(surface); session.RenderFrame(); session.RenderFrame();
        Assert.Equal(1, failures);
    }

    private sealed class UiHost : Broiler.UI.IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(640, 480); public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(Broiler.UI.UiInvalidation invalidation) { }
        public void Present(Broiler.Graphics.RenderList.BRenderList renderList) { }
    }

    [Fact]
    public void InlineSurfaceRendersRestrictedMailWithoutOpeningAWindowOrFetchingResources()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/blocked";
        var key = new MailMessageKey(new(Guid.NewGuid()), "INBOX", 7, 1);
        using var host = new WindowsHtmlPreviewHost();
        string markup = $$"""
            <base href="{{target}}"><meta http-equiv="refresh" content="0;url={{target}}">
            <link rel="stylesheet" href="{{target}}">
            <style>@import url('{{target}}'); @font-face { font-family: hostile; src: url('{{target}}'); }</style>
            <h1>Inline fixture</h1><table><tr><td>Readable</td></tr></table>
            <p style="background-image:url('{{target}}')" onclick="fetch('{{target}}')">Body</p>
            <script>fetch('{{target}}')</script><iframe src="{{target}}"></iframe><object data="{{target}}"></object>
            <video poster="{{target}}"><source src="{{target}}"></video><img src="{{target}}" srcset="{{target}} 2x">
            <a href="file:///C:/private.txt">Local file</a><a href="javascript:alert(1)">Active link</a>
            <a href="https://example.test/explicit" ping="{{target}}">Explicit link</a>
            """;
        using var surface = host.CreateInlineContent(new(key, "Fallback", markup));
        var html = Assert.IsType<ScrollableHtmlView>(surface);
        var layout = html.Content.CalculateLayout(640);
        Assert.True(layout.ContentHeight > 0); Assert.Contains(layout.Diagnostics, box => box.TagName == "table");
        Assert.All(layout.Links, link => Assert.Equal("https://example.test/explicit", link.Href));
        using var bitmap = html.Content.PaintTile(0, 1, 640, 256);
        Assert.NotEmpty(bitmap.ToPixelBuffer().Rgba);
        Assert.False(listener.Pending());
    }
}
