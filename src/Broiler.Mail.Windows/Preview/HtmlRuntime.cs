using Broiler.HTML.Image.Compat;
using Broiler.Layout.IR;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Explicitly bind the shipped managed renderer's compatibility backend.</summary>
internal static class HtmlRuntime
{
    // Broiler.HTML reads it once, at the first fill it rasters; it has no setting of its own for it.
    private const string RasterThreadsVariable = "BROILER_RASTER_THREADS";

    private static readonly Lazy<bool> Registration = new(() =>
    {
        StubCompatBootstrapper.EnsureRegistered();
        // The preview paints its tiles on its UI thread. Broiler.HTML can split a tile, and a large
        // fill, across threads, but the UI thread's wait for them is an STA wait, which in the
        // NativeAOT build pumps window messages: input, UI Automation calls, and posted work then ran
        // in the middle of a tile, and after a zoom the window stopped taking keyboard and mouse
        // input. One thread draws.
        TileParallelReplay.MaxDegreeOfParallelism = 1;
        Environment.SetEnvironmentVariable(RasterThreadsVariable, "1");
        return true;
    });

    internal static void Initialize() => _ = Registration.Value;
}
