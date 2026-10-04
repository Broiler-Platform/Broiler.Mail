using Broiler.HTML.Image.Compat;
using Broiler.Layout.IR;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Explicitly bind the shipped managed renderer's compatibility backend.</summary>
internal static class HtmlRuntime
{
    // Broiler.HTML reads it once, at the first fill it rasters; it has no public setting for it. The
    // variable is the process's: Broiler.Graphics' own software rasterizer (BCanvas) reads the same
    // name, and processes Mail starts, such as the browser a link opens in, inherit it.
    private const string RasterThreadsVariable = "BROILER_RASTER_THREADS";

    private static readonly Lazy<bool> Registration = new(() =>
    {
        StubCompatBootstrapper.EnsureRegistered();
        // The preview paints its tiles on its UI thread. Broiler.HTML can split a tile, and a large
        // fill, across threads, but the UI thread's wait for them is an STA wait, which in the
        // NativeAOT build pumps window messages: input, UI Automation calls, and posted work then ran
        // in the middle of a tile, and after a zoom the window stopped taking keyboard and mouse
        // input. One thread draws, at about twice the time a tile took on many cores, until
        // Broiler.HTML offers a sequential raster or one that does not wait that way.
        TileParallelReplay.MaxDegreeOfParallelism = 1;
        Environment.SetEnvironmentVariable(RasterThreadsVariable, "1");
        return true;
    });

    internal static void Initialize() => _ = Registration.Value;
}
