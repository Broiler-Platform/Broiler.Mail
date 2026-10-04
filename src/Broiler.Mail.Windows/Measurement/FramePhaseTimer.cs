using System.Diagnostics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.UI;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>
/// --detail: builds a session's frame as <see cref="UiSession.RenderFrame"/> does, with layout and the render
/// list timed apart. The roots are measured, then arranged, at the viewport RenderFrame uses; RenderFrame then
/// finds that layout current and only renders. With one root, as in both Mail windows, the calls and their
/// order are RenderFrame's own. Work a root's arrange or render invalidates again stays where it happens.
/// </summary>
internal static class FramePhaseTimer
{
    /// <param name="drained">When the frame's posted UI work was drained, just before this call.</param>
    public static BRenderList Render(UiSession session, long drained, out FramePhases phases)
    {
        var viewport = session.Host.ViewportSize;
        foreach (var root in session.Roots) root.Measure(viewport);
        long measured = Stopwatch.GetTimestamp();
        foreach (var root in session.Roots) root.Arrange(new BRect(0, 0, viewport.Width, viewport.Height));
        long arranged = Stopwatch.GetTimestamp();
        var renderList = session.RenderFrame();
        phases = new FramePhases(drained, measured, arranged);
        return renderList;
    }
}
