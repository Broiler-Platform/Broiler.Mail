using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Mail.Linux.Hosting;

namespace Broiler.Mail.Linux.Tests;

public sealed class LinuxUiHostTests
{
    [Fact]
    public void ViewportUsesCurrentSurfaceSizeAndDpiWithoutDoubleScaling()
    {
        using var renderer = new RecordingRenderer();
        using var surface = new TestSurface();
        var host = new LinuxUiHost(renderer, surface);
        surface.Resize(new BSize(800, 600), 1.5);
        Assert.Equal(new BSize(800, 600), host.ViewportSize);
        Assert.Equal(1.5, host.Scale);
        surface.Resize(new BSize(1024, 768), 2);
        Assert.Equal(new BSize(1024, 768), host.ViewportSize);
        Assert.Equal(2, host.Scale);
    }

    [Fact]
    public void FramesReachTheSelectedSurfaceAndInvalidationRaisedDuringRenderIsPreserved()
    {
        using var renderer = new RecordingRenderer();
        using var surface = new TestSurface();
        var host = new LinuxUiHost(renderer, surface);
        var list = host.CreateRenderList();
        Assert.True(host.IsInvalidated);
        host.Present(list);
        Assert.False(host.IsInvalidated);
        Assert.Same(surface, renderer.LastSurface);
        Assert.Same(list, renderer.LastList);
        Assert.Equal(0, renderer.LastFrame.FrameIndex);

        host.Invalidate(default);
        Assert.True(host.IsInvalidated);
        renderer.OnRender = () => host.Invalidate(default);
        host.Present(list);
        Assert.True(host.IsInvalidated);
        Assert.Equal(1, renderer.LastFrame.FrameIndex);
        renderer.OnRender = null;
        host.Present(list);
        Assert.False(host.IsInvalidated);
        Assert.Equal(2, renderer.LastFrame.FrameIndex);
    }

    [Fact]
    public void RenderingFailureRemainsPendingAndPropagatesToWindowOwner()
    {
        using var renderer = new RecordingRenderer();
        using var surface = new TestSurface();
        var host = new LinuxUiHost(renderer, surface);
        var failure = new InvalidOperationException("Synthetic renderer failure");
        renderer.OnRender = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => host.Present(host.CreateRenderList())));
        Assert.True(host.IsInvalidated);
        renderer.OnRender = null;
        host.Present(host.CreateRenderList());
        Assert.False(host.IsInvalidated);
        Assert.Equal(1, renderer.LastFrame.FrameIndex);
    }

    private sealed class TestSurface : IBroilerSurface
    {
        public BSize Size { get; private set; } = new(1100, 720);
        public double DpiScale { get; private set; } = 1;
        public void Resize(BSize size, double dpiScale) { Size = size; DpiScale = dpiScale; }
        public void Dispose() { }
    }

    private sealed class RecordingRenderer : IBroilerRenderer
    {
        public Action? OnRender { get; set; }
        public IBroilerSurface? LastSurface { get; private set; }
        public BRenderList? LastList { get; private set; }
        public BFrameContext LastFrame { get; private set; }
        public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext)
        {
            LastSurface = surface;
            LastList = renderList;
            LastFrame = frameContext;
            OnRender?.Invoke();
        }
        public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage) => throw new NotSupportedException();
        public BImageHandle CreateImage(BPixelBuffer pixels) => throw new NotSupportedException();
        public void ReleaseImage(BImageHandle image) => throw new NotSupportedException();
        public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
