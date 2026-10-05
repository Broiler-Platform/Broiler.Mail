using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Measurement;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.UI.Window;

namespace Broiler.Mail.Windows.Tests;

/// <summary>UI-12: the measurement harness's recorder, report, phase timer, preview hooks, and summary script.</summary>
[Collection("UI theme")]
public sealed class MeasurementTests
{
    private const long Origin = 1_000_000;

    private static long At(double milliseconds) => Origin + (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);

    private static void AssertMs(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++) Assert.Equal(expected[index], actual[index], 3);
    }

    [Fact]
    public void Detail_Splits_A_Frame_Into_Its_Phases_And_Times_Render_And_Present()
    {
        var recorder = new FrameRecorder(detail: true);
        recorder.MarkInput(At(0));
        recorder.EndDispatch(At(0), At(0.5));
        recorder.EndFrame((At(1), 0), new FramePhases(At(2), At(3), At(5)), 0, At(9), 10 * 1024);
        recorder.EndPaint(At(12));
        // A paint that built no frame adds nothing.
        recorder.EndPaint(At(20));

        var samples = recorder.Snapshot();
        AssertMs([8], samples.BuildMs);
        AssertMs([9], samples.InputToFrameMs);
        AssertMs([10], samples.AllocatedKb);
        var phases = samples.Phases!;
        AssertMs([0.5], phases.DispatchMs);
        AssertMs([1], phases.DrainMs);
        AssertMs([1], phases.MeasureMs);
        AssertMs([2], phases.ArrangeMs);
        AssertMs([4], phases.RenderListMs);
        AssertMs([3], phases.RenderPresentMs);
        AssertMs([12], phases.InputToPresentMs);
    }

    [Fact]
    public void A_Frame_Without_A_Timed_Paint_Has_No_Present_And_Keeps_Its_Inputs_To_Itself()
    {
        var recorder = new FrameRecorder(detail: true);
        recorder.MarkInput(At(0));
        // Built outside a paint message: no present sample, and its input is not charged to the next present.
        recorder.EndFrame((At(1), 0), new FramePhases(At(1), At(1), At(1)), 0, At(2), 0);
        recorder.EndFrame((At(10), 0), new FramePhases(At(10), At(10), At(10)), 0, At(11), 0);
        recorder.EndPaint(At(15));

        var phases = recorder.Snapshot().Phases!;
        AssertMs([4], phases.RenderPresentMs);
        Assert.Empty(phases.InputToPresentMs);
        AssertMs([2], recorder.Snapshot().InputToFrameMs);
    }

    [Fact]
    public void Without_Detail_A_Frame_Is_Timed_As_One_Span_As_In_The_Baseline()
    {
        var recorder = new FrameRecorder();
        recorder.MarkInput(At(0));
        recorder.EndDispatch(At(0), At(0.5));
        recorder.EndFrame((At(1), 0), new FramePhases(At(2), At(3), At(5)), 0, At(9), 0);
        recorder.EndPaint(At(12));

        var samples = recorder.Snapshot();
        Assert.Null(samples.Phases);
        AssertMs([8], samples.BuildMs);
        AssertMs([9], samples.InputToFrameMs);
    }

    [Fact]
    public void Preview_Frames_Are_Split_By_Whether_They_Drew_Tiles()
    {
        var recorder = new FrameRecorder();
        recorder.EndFrame((At(0), 0), null, 0, At(1), 0);
        recorder.EndFrame((At(10), 0), null, 2, At(110), 0);
        recorder.EndFrame((At(200), 0), null, 0, At(202), 0);

        var samples = recorder.Snapshot();
        AssertMs([1, 2], samples.BuildMsWhere(drewTiles: false));
        AssertMs([100], samples.BuildMsWhere(drewTiles: true));
    }

    [Fact]
    public void Percentiles_Of_A_Hundred_Frames_Use_The_Nearest_Rank()
    {
        double[] values = Enumerable.Range(1, 100).Select(value => (double)(101 - value)).ToArray();
        Assert.Equal(50, FrameSamples.Percentile(values, 50));
        Assert.Equal(95, FrameSamples.Percentile(values, 95));
        Assert.Equal(99, FrameSamples.Percentile(values, 99));
        Assert.Equal(100, FrameSamples.Percentile(values, 100));
        Assert.Equal(7, FrameSamples.Percentile([7], 99));
    }

    // The fields of a version 1 report (2 October baseline); a default report keeps every one under its name.
    private static readonly string[] VersionOneFields =
    [
        "workload", "scenario", "theme", "windowSize", "dpiScale", "build", "machine", "startupFirstFrameMs", "startupInteractiveMs",
        "sectionSeconds", "steps", "unpaintedSteps", "frames", "buildMsP50", "buildMsP95", "buildMsP99", "buildMsMax",
        "inputToFrameMsP50", "inputToFrameMsP95", "inputToFrameMsP99", "inputToFrameMsMax", "allocatedKbPerFrameP50",
        "allocatedKbPerFrameP95", "allocatedKbPerFrameP99", "allocatedKbPerFrameMax", "allocatedMbTotal", "workingSetMb", "privateMb",
        "managedHeapMb", "gcCollections",
    ];

    [Fact]
    public void A_Default_Report_Keeps_The_Baseline_Fields_And_Names_The_System_Scale()
    {
        var recorder = new FrameRecorder();
        recorder.EndFrame((At(0), 0), null, 0, At(4), 0);
        var options = new DemoOptions(DemoScenario.LargeInbox, AppTheme.Light, Measure: MeasureWorkload.Scroll);

        using var report = Write(MeasurementReport.Create(options, MeasureWorkload.Scroll, recorder, recorder.Snapshot(), 1, 0,
            TimeSpan.FromSeconds(1), new MeasurementScale(1.5, 1.5, null)));
        var root = report.RootElement;

        Assert.All(VersionOneFields, field => Assert.True(root.TryGetProperty(field, out _), field));
        Assert.Equal(2, root.GetProperty("reportVersion").GetInt32());
        Assert.Equal("scroll", root.GetProperty("workload").GetString());
        Assert.Equal("system", root.GetProperty("scaleKind").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("simulatedScalePercent").ValueKind);
        Assert.False(root.GetProperty("detail").GetBoolean());
        Assert.False(root.TryGetProperty("measureMsP50", out _));
        Assert.False(root.TryGetProperty("tileMisses", out _));
        Assert.Equal(4, root.GetProperty("buildMsP50").GetDouble(), 3);
    }

    [Fact]
    public void A_Detailed_Preview_Report_Names_The_Simulated_Scale_Phases_And_Tiles()
    {
        var recorder = new FrameRecorder(detail: true);
        recorder.EndFrame((At(0), 0), new FramePhases(At(1), At(2), At(3)), 0, At(4), 0);
        recorder.EndFrame((At(10), 0), new FramePhases(At(11), At(12), At(13)), 1, At(130), 0);
        recorder.EndPaint(At(140));
        var tiles = new HtmlTileStatistics();
        tiles.Hit();
        tiles.Drawn((0, 0), Stopwatch.Frequency / 10, Stopwatch.Frequency / 100, 4 * 1048576);
        var preview = new PreviewResult(new BSize(900, 700), 2, 1, 250, new FrameSamples([40], [0], []), tiles.Snapshot(), 4 * 1048576, 1);
        var options = new DemoOptions(DemoScenario.LongHtml, AppTheme.Light, Measure: MeasureWorkload.LongHtml, ScalePercent: 200, Detail: true);

        using var report = Write(MeasurementReport.Create(options, MeasureWorkload.LongHtml, recorder, recorder.Snapshot(), 2, 0,
            TimeSpan.FromSeconds(1), new MeasurementScale(2, 1.5, 200), preview));
        var root = report.RootElement;

        Assert.Equal("long-html", root.GetProperty("workload").GetString());
        Assert.Equal("simulated", root.GetProperty("scaleKind").GetString());
        Assert.Equal(200, root.GetProperty("simulatedScalePercent").GetInt32());
        Assert.Equal(1.5, root.GetProperty("systemDpiScale").GetDouble());
        Assert.True(root.GetProperty("detail").GetBoolean());
        Assert.Equal(1, root.GetProperty("measureMsP50").GetDouble(), 3);
        Assert.Equal(10, root.GetProperty("renderPresentMsP95").GetDouble(), 3);
        Assert.Equal("900x700", root.GetProperty("previewWindowSize").GetString());
        Assert.Equal(1, root.GetProperty("previewOpeningZoom").GetDouble());
        Assert.False(root.TryGetProperty("previewZoom", out _));
        Assert.Equal(1, root.GetProperty("framesDrawingTiles").GetInt32());
        Assert.Equal(4, root.GetProperty("buildMsCachedTilesP50").GetDouble(), 3);
        Assert.Equal(120, root.GetProperty("buildMsDrawingTilesP50").GetDouble(), 3);
        Assert.Equal(1, root.GetProperty("tileHits").GetInt32());
        Assert.Equal(1, root.GetProperty("tileMisses").GetInt32());
        Assert.Equal(100, root.GetProperty("tileRasterMsP50").GetDouble(), 3);
        Assert.Equal(10, root.GetProperty("tileUploadMsP95").GetDouble(), 3);
        Assert.Equal(4, root.GetProperty("tilePeakCachedMb").GetDouble(), 3);
        Assert.Equal(250, root.GetProperty("openToFirstFrameMs").GetDouble(), 3);
    }

    private static JsonDocument Write(MeasurementReport report)
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-mail-report-{Guid.NewGuid():N}.json");
        try
        {
            report.Write(path);
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_Phase_Timed_Frame_Draws_What_RenderFrame_Draws()
    {
        // Two copies of the same fixture, one built as every frame is and one as --detail builds it; then both
        // lay out again at a new size.
        using var plain = SettledShell.Start();
        using var timed = SettledShell.Start(probed: true);

        var expected = Commands(plain.Session.RenderFrame());
        var actual = Commands(FramePhaseTimer.Render(timed.Session, Stopwatch.GetTimestamp(), out var phases));
        Assert.Equal(expected, actual);
        Assert.True(phases.Drained <= phases.Measured && phases.Measured <= phases.Arranged);
        // RenderFrame lays out again whatever the phases left undone. The shell was laid out once, before RenderFrame
        // asked the host for its render list, and RenderFrame only rendered it: nothing the shell did while it was
        // measured or arranged asked for layout again, so the phase timers hold all of the frame's layout.
        var probe = timed.Probe!;
        Assert.Equal((1, 1, 1), probe.Counts);
        Assert.Equal((0, 0), probe.LaidOutAfterRenderLists);

        plain.Host.ViewportSize = timed.Host.ViewportSize = new BSize(900, 640);
        Assert.Equal(Commands(plain.Session.RenderFrame()), Commands(FramePhaseTimer.Render(timed.Session, Stopwatch.GetTimestamp(), out _)));
        Assert.Equal((2, 2, 2), probe.Counts);
        Assert.Equal((1, 1), probe.LaidOutAfterRenderLists);
    }

    private static string[] Commands(BRenderList list) => list.Commands.Select(command => command.ToString()).ToArray();

    [Fact]
    public void The_Phase_Timer_Leaves_RenderFrame_Only_Rendering()
    {
        var host = new ResizableHost { ViewportSize = new BSize(800, 600) };
        using var session = new StandardUiSessionBuilder().Build(host);
        var root = new LayoutProbe();
        session.AddRoot(root);

        // Measured and arranged before RenderFrame asked the host for its render list, so RenderFrame only rendered.
        FramePhaseTimer.Render(session, Stopwatch.GetTimestamp(), out _);
        Assert.Equal((1, 1, 1), root.Counts);
        Assert.Equal((0, 0), root.LaidOutAfterRenderLists);
        Assert.Equal(new BRect(0, 0, 800, 600), root.Arranged);
        FramePhaseTimer.Render(session, Stopwatch.GetTimestamp(), out _);
        Assert.Equal((1, 1, 2), root.Counts);
        host.ViewportSize = new BSize(640, 480);
        FramePhaseTimer.Render(session, Stopwatch.GetTimestamp(), out _);
        Assert.Equal((2, 2, 3), root.Counts);
        Assert.Equal((2, 2), root.LaidOutAfterRenderLists);
    }

    [Fact]
    public void Help_Names_The_Detail_Switch_After_The_Workloads()
    {
        using var output = new StringWriter();
        Program.WriteHelp(output);
        string[] lines = output.ToString().Split(Environment.NewLine);
        int workloads = Array.FindIndex(lines, line => line.StartsWith("--measure", StringComparison.Ordinal));
        int detail = Array.FindIndex(lines, line => line.StartsWith("--detail", StringComparison.Ordinal));
        int acceptance = Array.FindIndex(lines, line => line.StartsWith("Acceptance-only", StringComparison.Ordinal));
        Assert.True(workloads < detail && detail < acceptance);
        Assert.Contains(lines[workloads..detail], line => line.TrimStart().StartsWith("long-html ", StringComparison.Ordinal));
        Assert.Contains(lines[workloads..detail], line => line.TrimStart().StartsWith("preview-zoom ", StringComparison.Ordinal));
        Assert.Contains("[--detail]", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Measured_Preview_Renders_At_The_Simulated_Scale_And_Records_Its_Frames_And_Tiles()
    {
        // A scale other than Windows' own, so the window cannot pass by rendering at that.
        double scale = Math.Abs(WindowsScreen.SystemScale() - 2) < 0.01 ? 1.25 : 2;
        var measurement = new PreviewMeasurement(new FrameRecorder(), new HtmlTileStatistics(), scale);
        var (preview, closed) = await OpenPreviewAsync(measurement);
        try
        {
            Assert.True(measurement.Frames.WaitForFrame(0, TimeSpan.FromSeconds(15)), "The preview drew no frame.");
            var read = new TaskCompletionSource<(double Scale, BSize Size, (int PixelWidth, int PixelHeight, double WidthDip, double HeightDip)[] Tiles)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(preview.RunOnUiThread(() => read.TrySetResult((preview.DpiScale, preview.ClientSize, preview.HtmlView.Content.CachedTileSizes.ToArray()))));
            var (dpiScale, size, cached) = await read.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // WM_GETMINMAXINFO: the simulated pixels are not limited to the real desktop's size.
            var limits = new TaskCompletionSource<(int X, int Y)>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(preview.RunOnUiThread(() => limits.TrySetResult(MaximumTrackSize(preview.NativeHandle))));
            Assert.Equal((short.MaxValue, short.MaxValue), ((int, int))await limits.Task.WaitAsync(TimeSpan.FromSeconds(15)));

            Assert.Equal(scale, dpiScale);
            // Named in the title, as the main window names it, so a capture is not taken for a real display.
            Assert.EndsWith($", simulated {Math.Round(scale * 100)}% scale", preview.Options.Title, StringComparison.Ordinal);
            Assert.Equal(900, size.Width, 1.0);
            Assert.Equal(700, size.Height, 1.0);
            var tile = Assert.Single(cached);
            Assert.Equal(Math.Ceiling(tile.WidthDip * scale), tile.PixelWidth);
            var counts = measurement.Tiles.Snapshot();
            Assert.Equal(1, counts.Misses);
            Assert.Equal(1, counts.Layouts);
            Assert.Equal(measurement.Frames.Snapshot().TilesDrawn.Sum(), counts.Misses);
        }
        finally
        {
            preview.CloseWindow();
            await closed.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task A_Detailed_Preview_Times_The_Dispatch_Phases_And_Paint_Of_A_Measured_Input()
    {
        var measurement = new PreviewMeasurement(new FrameRecorder(detail: true), new HtmlTileStatistics(), SimulatedScale: null);
        var (preview, closed) = await OpenPreviewAsync(measurement);
        try
        {
            var frames = measurement.Frames;
            Assert.True(frames.WaitForFrame(0, TimeSpan.FromSeconds(15)), "The preview drew no frame.");
            // Once the opening frames have ended, only the input's frame and its paint are recorded.
            while (frames.WaitForFrame(frames.Frames, TimeSpan.FromMilliseconds(300))) { }
            frames.Reset();
            int before = frames.Frames;
            Assert.True(preview.RunOnUiThread(() => preview.DispatchMeasured(CtrlPlus())));
            Assert.True(frames.WaitForFrame(before, TimeSpan.FromSeconds(15)), "The zoom drew no frame.");
            // The paint that built the frame returns once the frame is rendered and presented.
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (frames.Snapshot().Phases!.InputToPresentMs.Length == 0)
            {
                Assert.True(DateTime.UtcNow < deadline, "The zoom's frame was not presented.");
                await Task.Delay(10);
            }

            var samples = frames.Snapshot();
            var phases = samples.Phases!;
            Assert.Single(phases.DispatchMs);
            Assert.Single(samples.InputToFrameMs);
            Assert.Single(phases.InputToPresentMs);
            Assert.True(phases.InputToPresentMs[0] >= samples.InputToFrameMs[0]);
            Assert.NotEmpty(phases.RenderPresentMs);
            Assert.All(new[] { phases.DrainMs, phases.MeasureMs, phases.ArrangeMs, phases.RenderListMs },
                phase => Assert.Equal(samples.BuildMs.Length, phase.Length));
            Assert.DoesNotContain("simulated", preview.Options.Title, StringComparison.Ordinal);

            // A resize draws its frame inside SetWindowPos, outside any paint message. The frame window's WM_PAINT
            // that follows draws nothing, so it must not be taken for that frame's present.
            frames.Reset();
            before = frames.Frames;
            Assert.True(preview.RunOnUiThread(() => WindowsScreen.Resize(preview.NativeHandle, WindowsScreen.OuterSize(preview.NativeHandle).Width - 80,
                WindowsScreen.OuterSize(preview.NativeHandle).Height)));
            Assert.True(frames.WaitForFrame(before, TimeSpan.FromSeconds(15)), "The resize drew no frame.");
            await Task.Delay(500);
            Assert.Empty(frames.Snapshot().Phases!.RenderPresentMs);
        }
        finally
        {
            preview.CloseWindow();
            await closed.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private static UiInputEvent CtrlPlus() => UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
        new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
        KeyboardKey.FromName("VirtualKey:" + 0xBB), KeyboardKeyTransition.Down, KeyboardModifierState.Control, 0xBB, 0, 0, false, false,
        Source: InputEventSource.Synthetic));

    /// <summary>A measured preview of a short document in a real window on its own thread, open until it is closed.</summary>
    private static async Task<(HtmlPreviewWindow Window, Task Closed)> OpenPreviewAsync(PreviewMeasurement measurement)
    {
        var ready = new TaskCompletionSource<HtmlPreviewWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HtmlPreviewWindow(new HtmlPreviewDocument("<h1>Agenda</h1><p>Items</p>", new HashSet<string>()), "Agenda as text", _ => { },
                    measurement: measurement) { ShowInTaskbar = false, Opacity = 0 };
                window.Shown += (_, _) => ready.TrySetResult(window);
                window.Run();
                closed.TrySetResult();
            }
            catch (Exception error) { ready.TrySetException(error); closed.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return (await ready.Task.WaitAsync(TimeSpan.FromSeconds(30)), closed.Task);
    }

    [Fact]
    public async Task A_Detailed_Splitter_Run_Times_Each_Dispatch_Phase_And_Paint()
    {
        var (report, samples) = await MeasureInWindowAsync(MeasureWorkload.Splitter);
        using (report)
        {
            var root = report.RootElement;
            Assert.True(root.GetProperty("detail").GetBoolean());
            Assert.Equal(60, root.GetProperty("steps").GetInt32());
            Assert.Equal(0, root.GetProperty("unpaintedSteps").GetInt32());
            foreach (string field in new[] { "dispatchMsP50", "drainMsP50", "measureMsP50", "arrangeMsP50", "renderListMsP50", "renderPresentMsP50", "inputToPresentMsP50" })
                Assert.True(root.GetProperty(field).ValueKind == JsonValueKind.Number, field);
        }
        var phases = samples.Phases!;
        // Each key press was dispatched, shown by a frame built in its own paint, and presented when that paint returned.
        Assert.Equal(60, phases.DispatchMs.Length);
        Assert.Equal(60, samples.InputToFrameMs.Length);
        Assert.Equal(60, phases.InputToPresentMs.Length);
        Assert.True(phases.RenderPresentMs.Length >= 60, $"{phases.RenderPresentMs.Length} presents were timed.");
        Assert.All(new[] { phases.DrainMs, phases.MeasureMs, phases.ArrangeMs, phases.RenderListMs },
            phase => Assert.Equal(samples.BuildMs.Length, phase.Length));
    }

    [Fact]
    public async Task A_Detailed_Resize_Run_Times_The_Render_And_Present_Inside_Each_Resize()
    {
        var (report, samples) = await MeasureInWindowAsync(MeasureWorkload.Resize);
        using (report)
        {
            var root = report.RootElement;
            Assert.Equal(30, root.GetProperty("steps").GetInt32());
            Assert.Equal(0, root.GetProperty("unpaintedSteps").GetInt32());
            foreach (string field in new[] { "measureMsP50", "renderPresentMsP50", "inputToPresentMsP50" })
                Assert.True(root.GetProperty(field).ValueKind == JsonValueKind.Number, field);
        }
        // A resize draws and presents its frame before SetWindowPos returns; no paint message follows for it.
        var phases = samples.Phases!;
        Assert.True(phases.InputToPresentMs.Length >= 30, $"{phases.InputToPresentMs.Length} resizes were presented.");
        Assert.True(phases.RenderPresentMs.Length >= 30, $"{phases.RenderPresentMs.Length} presents were timed.");
    }

    /// <summary>
    /// Runs a --measure --detail demo on the inbox fixture in a real window, as the app does: the run closes the
    /// window once it has written its report. Returns the report and every sample the window's recorder kept.
    /// </summary>
    private static async Task<(JsonDocument Report, FrameSamples Samples)> MeasureInWindowAsync(MeasureWorkload workload)
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-mail-report-{Guid.NewGuid():N}.json");
        var options = new DemoOptions(DemoScenario.Inbox, AppTheme.Light, Measure: workload, Report: path, Detail: true);
        var palette = StandardControlPaint.Theme;
        var finished = new TaskCompletionSource<(int ExitCode, FrameSamples Samples)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = DemoApplication.Create(options);
                application.InitializeAsync().GetAwaiter().GetResult();
                using var window = new WindowsMailWindow(application, options);
                int exitCode = window.Run();
                finished.TrySetResult((exitCode, window.Recorder!.Snapshot()));
            }
            catch (Exception error) { finished.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            var (exitCode, samples) = await finished.Task.WaitAsync(TimeSpan.FromSeconds(90));
            Assert.Equal(0, exitCode);
            return (JsonDocument.Parse(File.ReadAllText(path)), samples);
        }
        finally
        {
            // The window sets the process-wide palette; the next window or test starts from the one before.
            StandardControlPaint.ApplyTheme(palette);
            File.Delete(path);
        }
    }

    private static (int X, int Y) MaximumTrackSize(nint window)
    {
        // MINMAXINFO: five POINTs; ptMaxTrackSize is the fifth.
        nint info = Marshal.AllocHGlobal(40);
        try
        {
            for (int offset = 0; offset < 40; offset += 4) Marshal.WriteInt32(info, offset, 0);
            SendMessage(window, 0x0024, 0, info);
            return (Marshal.ReadInt32(info, 32), Marshal.ReadInt32(info, 36));
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [Fact]
    public void The_Summary_Compares_Medians_With_The_Proposed_Budgets_And_Fails_Only_When_Strict()
    {
        using var directory = new ScratchDirectory();
        WriteReport(directory.Path, "idle-1", new("idle", 0, null, null, null));
        WriteReport(directory.Path, "scroll-1", new("scroll", 300, 5, 6, 300) { Simulated = 200 });
        WriteReport(directory.Path, "scroll-2", new("scroll", 300, 9, 7, 310) { Simulated = 200 });
        // The median of two runs is their mean: 8.5 ms is over 8, although one of them took 6.
        WriteReport(directory.Path, "splitter-1", new("splitter", 60, 6, 7, 200));
        WriteReport(directory.Path, "splitter-2", new("splitter", 60, 11, 12, 210));
        WriteReport(directory.Path, "resize-1", new("resize", 30, 15, 18, 400));
        WriteReport(directory.Path, "long-html-1", new("long-html", 600, 110, 112, 4) { CachedTilesBuildP95 = 0.2, Preview = true });

        var (exitCode, output) = RunSummary("-Evaluate", directory.Path);
        Assert.True(exitCode == 0, output);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "summary-evaluated.json")));
        var verdicts = Verdicts(summary).ToDictionary(item => $"{item.Workload}/{item.Field}", item => (item.Result, item.Scale));
        Assert.Equal(("pass", "system 150 %"), verdicts["idle/frames"]);
        Assert.Equal(("pass", "simulated 200 %"), verdicts["scroll/buildMsP95"]);
        Assert.Equal(("over", "simulated 200 %"), verdicts["scroll/allocatedKbPerFrameP50"]);
        Assert.Equal("over", verdicts["splitter/buildMsP95"].Result);
        Assert.Equal("pass", verdicts["splitter/inputToFrameMsP95"].Result);
        Assert.Equal("over", verdicts["resize/buildMsP95"].Result);
        Assert.Equal("over", verdicts["resize/inputToFrameMsP95"].Result);
        // The preview workloads are judged too: every frame against the build and input targets, and the frames that
        // found every tile cached on their own.
        Assert.Equal("over", verdicts["long-html/buildMsP95"].Result);
        Assert.Equal("pass", verdicts["long-html/buildMsCachedTilesP95"].Result);
        Assert.Equal("over", verdicts["long-html/inputToFrameMsP95"].Result);
        Assert.Equal("pass", verdicts["long-html/allocatedKbPerFrameP50"].Result);
        Assert.All(new[] { "scroll", "splitter", "resize", "long-html" }, workload => Assert.Equal("pass", verdicts[$"{workload}/unpaintedSteps"].Result));
        Assert.False(verdicts.ContainsKey("idle/unpaintedSteps"));
        Assert.Equal(6, summary.RootElement.GetProperty("budgets").GetProperty("over").GetInt32());
        string markdown = File.ReadAllText(Path.Combine(directory.Path, "summary-evaluated.md"));
        Assert.Contains("| scroll | simulated 200 % | 300 | 0 | 300 |", markdown);
        Assert.Contains("not real DPI results", markdown);
        Assert.Contains("- long-html: A frame that brings a new tile into view", markdown);
        Assert.Contains("| Re-rasters after eviction |", markdown);
        Assert.Contains("opened at zoom 1", markdown);
        Assert.Contains("6 result(s) over budget, 0 without data.", markdown);
        // Startup is the median of the baseline workloads' runs, as on 2 October, apart from other scales and fixtures.
        Assert.Contains("Startup, baseline workloads at system 150 %, 4 runs:", markdown);
        Assert.Contains("Startup, baseline workloads at simulated 200 %, 2 runs:", markdown);
        Assert.Contains("Startup, HTML preview workloads (long-html fixture) at system 150 %, 1 run:", markdown);

        Assert.Equal(1, RunSummary("-Evaluate", directory.Path, "-Strict").ExitCode);
        Assert.Equal(0, RunSummary("-Evaluate", directory.Path, "-Strict", "-Workloads", "idle").ExitCode);
    }

    [Fact]
    public void Strict_Fails_A_Run_That_Painted_Nothing_And_A_Budget_Without_Data()
    {
        using var directory = new ScratchDirectory();
        WriteReport(directory.Path, "scroll-1", new("scroll", 300, null, null, null) { Frames = 0, Unpainted = 300 });
        // A report without the allocation the budget judges.
        WriteReport(directory.Path, "type-1", new("type", 600, 1.2, 5, null));

        var (exitCode, output) = RunSummary("-Evaluate", directory.Path);
        Assert.True(exitCode == 0, output);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "summary-evaluated.json")));
        var verdicts = Verdicts(summary).ToDictionary(item => $"{item.Workload}/{item.Field}", item => item.Result);
        Assert.Equal("over", verdicts["scroll/unpaintedSteps"]);
        Assert.Equal("no data", verdicts["scroll/buildMsP95"]);
        Assert.Equal("pass", verdicts["type/buildMsP95"]);
        Assert.Equal("no data", verdicts["type/allocatedKbPerFrameP50"]);
        Assert.Equal(4, summary.RootElement.GetProperty("budgets").GetProperty("noData").GetInt32());
        Assert.Contains("| scroll | system 150 % | 300 | 300 | 0 |", File.ReadAllText(Path.Combine(directory.Path, "summary-evaluated.md")));

        Assert.Equal(1, RunSummary("-Evaluate", directory.Path, "-Strict").ExitCode);
        // Nothing over budget, but a budget without data.
        Assert.Equal(1, RunSummary("-Evaluate", directory.Path, "-Strict", "-Workloads", "type").ExitCode);
    }

    [Fact]
    public void Evaluating_Stored_Reports_Keeps_Their_Summary_And_Says_When_It_Was_Evaluated()
    {
        using var directory = new ScratchDirectory();
        using var elsewhere = new ScratchDirectory();
        WriteReport(directory.Path, "idle-1", new("idle", 0, null, null, null));
        const string stored = "# UI measurements, 2026-10-02 19:04\n";
        File.WriteAllText(Path.Combine(directory.Path, "summary.md"), stored);
        File.WriteAllText(Path.Combine(directory.Path, "summary.json"), "{}");

        Assert.Equal(0, RunSummary("-Evaluate", directory.Path).ExitCode);
        Assert.Equal(stored, File.ReadAllText(Path.Combine(directory.Path, "summary.md")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(directory.Path, "summary.json")));
        string[] evaluated = File.ReadAllLines(Path.Combine(directory.Path, "summary-evaluated.md"));
        Assert.StartsWith($"# UI measurements in {Path.GetFileName(directory.Path)}, evaluated ", evaluated[0], StringComparison.Ordinal);
        Assert.StartsWith("- Reports: 1 in ", evaluated[2], StringComparison.Ordinal);

        // Evaluated again into another directory: the first evaluation is not taken for a report.
        Assert.Equal(0, RunSummary("-Evaluate", directory.Path, "-Output", elsewhere.Path).ExitCode);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(elsewhere.Path, "summary-evaluated.json")));
        Assert.Equal(1, Assert.Single(summary.RootElement.GetProperty("rows").EnumerateArray()).GetProperty("runs").GetInt32());
    }

    [Fact]
    public void Detailed_And_Plain_Runs_Are_Summarized_Apart()
    {
        using var directory = new ScratchDirectory();
        WriteReport(directory.Path, "scroll-1", new("scroll", 300, 5, 6, 100));
        WriteReport(directory.Path, "scroll-detail-1", new("scroll", 300, 20, 22, 100) { Detail = true });

        Assert.Equal(0, RunSummary("-Evaluate", directory.Path).ExitCode);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, "summary-evaluated.json")));
        var builds = Verdicts(summary).Where(item => item.Field == "buildMsP95").ToDictionary(item => item.Detail, item => item.Result);
        Assert.Equal("pass", builds[false]);
        Assert.Equal("over", builds[true]);
        Assert.Contains("| scroll | system 150 %, detail | 300 |", File.ReadAllText(Path.Combine(directory.Path, "summary-evaluated.md")));
    }

    [Fact]
    public void A_Run_Summarizes_Only_Its_Own_Reports_And_Passes_Its_Scale_And_Detail()
    {
        using var tools = new ScratchDirectory();
        using var output = new ScratchDirectory();
        // Stands in for the app: records its arguments and writes the report it was given.
        File.WriteAllText(Path.Combine(tools.Path, "run.cmd"), string.Join("\r\n",
            "@echo off", "set \"here=%~dp0\"", ">>\"%here%arguments.txt\" echo %*",
            ":next", "if \"%~1\"==\"\" exit /b 0", "if \"%~1\"==\"--report\" goto report", "shift", "goto next",
            ":report", "copy /y \"%here%report.json\" \"%~2\" >nul", "exit /b 0", ""));
        File.WriteAllText(Path.Combine(tools.Path, "silent.cmd"), "@exit /b 0\r\n");
        WriteReport(tools.Path, "report", new("idle", 0, null, null, null) { Simulated = 200, Detail = true });
        // Earlier runs in the same directory, which drew frames while idle.
        WriteReport(output.Path, "idle-s200-earlier-1", new("idle", 0, null, null, null) { Frames = 5, Simulated = 200, Detail = true });
        WriteReport(output.Path, "idle-s200-earlier-2", new("idle", 0, null, null, null) { Frames = 5, Simulated = 200, Detail = true });

        var (exitCode, text) = RunSummary("-Executable", Path.Combine(tools.Path, "run.cmd"), "-Output", output.Path, "-Workloads", "idle",
            "-Repeat", "1", "-Scales", "200", "-Detail");
        Assert.True(exitCode == 0, text);
        using (var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json"))))
        {
            Assert.Equal(1, Assert.Single(summary.RootElement.GetProperty("rows").EnumerateArray()).GetProperty("runs").GetInt32());
            Assert.Equal("pass", Assert.Single(Verdicts(summary)).Result);
        }
        string arguments = File.ReadAllText(Path.Combine(tools.Path, "arguments.txt"));
        Assert.Contains("--demo inbox", arguments);
        Assert.Contains("--measure idle", arguments);
        Assert.Contains("--scale 200", arguments);
        Assert.Contains("--detail", arguments);

        // A run that writes no report fails, although a report of the same name is left from the run before.
        Assert.True(File.Exists(Path.Combine(output.Path, "idle-s200-1.json")));
        Assert.NotEqual(0, RunSummary("-Executable", Path.Combine(tools.Path, "silent.cmd"), "-Output", output.Path, "-Workloads", "idle",
            "-Repeat", "1", "-Scales", "200").ExitCode);
    }

    private static (string Workload, string Scale, bool Detail, string Field, string Result)[] Verdicts(JsonDocument summary) =>
        summary.RootElement.GetProperty("verdicts").EnumerateArray().Select(item => (item.GetProperty("workload").GetString()!,
            item.GetProperty("scale").GetString()!, item.GetProperty("detail").GetBoolean(), item.GetProperty("field").GetString()!,
            item.GetProperty("result").GetString()!)).ToArray();

    /// <summary>A report as the app writes it, with the fields the summary reads; frames default to the steps.</summary>
    private sealed record SyntheticReport(string Workload, int Steps, double? BuildP95, double? InputP95, double? AllocationP50)
    {
        public int? Simulated { get; init; }
        public bool Detail { get; init; }
        public int? Frames { get; init; }
        public int Unpainted { get; init; }
        public double? CachedTilesBuildP95 { get; init; }
        /// <summary>A preview workload's report, with the preview's size, scale, and opening zoom.</summary>
        public bool Preview { get; init; }
    }

    private static void WriteReport(string directory, string name, SyntheticReport report)
    {
        using var stream = File.Create(Path.Combine(directory, name + ".json"));
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("reportVersion", 2);
        writer.WriteString("workload", report.Workload);
        writer.WriteString("theme", "light");
        writer.WriteString("windowSize", "1100x720");
        writer.WriteNumber("dpiScale", report.Simulated is { } percent ? percent / 100.0 : 1.5);
        if (report.Simulated is { } simulatedPercent) writer.WriteNumber("simulatedScalePercent", simulatedPercent);
        else writer.WriteNull("simulatedScalePercent");
        writer.WriteBoolean("detail", report.Detail);
        writer.WriteString("build", "Release, NativeAOT");
        writer.WriteString("machine", "test");
        writer.WriteNumber("startupFirstFrameMs", 230);
        writer.WriteNumber("startupInteractiveMs", 250);
        writer.WriteNumber("steps", report.Steps);
        writer.WriteNumber("unpaintedSteps", report.Unpainted);
        writer.WriteNumber("frames", report.Frames ?? report.Steps);
        foreach (var (field, value) in new[] { ("buildMsP50", report.BuildP95 / 2), ("buildMsP95", report.BuildP95), ("buildMsP99", report.BuildP95),
            ("inputToFrameMsP95", report.InputP95), ("allocatedKbPerFrameP50", report.AllocationP50), ("workingSetMb", (double?)70) })
        {
            if (value is { } number) writer.WriteNumber(field, number);
            else writer.WriteNull(field);
        }
        if (report.Preview)
        {
            writer.WriteString("previewWindowSize", "900x700");
            writer.WriteNumber("previewDpiScale", 1.5);
            writer.WriteNumber("previewOpeningZoom", 1);
            if (report.CachedTilesBuildP95 is { } cached) writer.WriteNumber("buildMsCachedTilesP95", cached);
        }
        writer.WriteEndObject();
    }

    /// <summary>An empty directory of its own under the temporary directory, deleted with everything in it.</summary>
    private sealed class ScratchDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Broiler.Mail.Measurements", Guid.NewGuid().ToString("N"));

        public ScratchDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static (int ExitCode, string Output) RunSummary(params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptPath() }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "Measure-UI.ps1 did not finish.");
        return (process.ExitCode, output.Result + error.Result);
    }

    private static string ScriptPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "scripts", "Measure-UI.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("scripts/Measure-UI.ps1 was not found above the test output.");
    }

    /// <summary>The inbox fixture, settled as the demo prepares it, in a headless session of its own.</summary>
    private sealed class SettledShell : IDisposable
    {
        private readonly MailShellView _shell;
        // Wakes the test thread for the dispatcher, which outlives Start.
        private readonly SemaphoreSlim _woken;

        private SettledShell(MailShellView shell, UiSession session, ResizableHost host, SemaphoreSlim woken, LayoutProbe? probe)
        {
            _shell = shell;
            Session = session;
            Host = host;
            _woken = woken;
            Probe = probe;
        }

        public UiSession Session { get; }
        public ResizableHost Host { get; }
        /// <summary>With <c>probed</c>, the session's root, which holds the shell and counts its own layout.</summary>
        public LayoutProbe? Probe { get; }

        public static SettledShell Start(bool probed = false)
        {
            var options = new DemoOptions(DemoScenario.Inbox, AppTheme.Light, 1100, 720);
            var application = DemoApplication.Create(options);
            application.InitializeAsync().GetAwaiter().GetResult();
            var woken = new SemaphoreSlim(0);
            var dispatcher = new StandardQueuedUiDispatcher(() => woken.Release());
            var model = application.CreateViewModel(dispatcher);
            var shell = new MailShellView(model, null, DemoApplication.CreateDateFormatter());
            var driver = DemoScenarioDriver.Start(options, model, shell, dispatcher);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!driver.Completion.IsCompleted)
            {
                Assert.True(DateTime.UtcNow < deadline, "The inbox fixture did not settle.");
                woken.Wait(TimeSpan.FromMilliseconds(100));
                dispatcher.Drain();
            }
            driver.Completion.GetAwaiter().GetResult();
            dispatcher.Drain();
            var host = new ResizableHost { ViewportSize = new BSize(options.Width, options.Height) };
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host);
            // Held by the probe, the window would count as a subwindow and draw a title bar of its own; without one it
            // lays out and draws as the root does.
            if (probed) shell.Window.Chrome = UiWindowChrome.None;
            var probe = probed ? new LayoutProbe(shell.Window) : null;
            session.AddRoot(probe ?? (UiElement)shell.Window);
            return new SettledShell(shell, session, host, woken, probe);
        }

        public void Dispose()
        {
            Session.Dispose();
            _shell.Dispose();
            _woken.Dispose();
        }
    }

    /// <summary>
    /// A root, holding at most one element at its own size, that counts how often it is measured, arranged, and rendered,
    /// and how many render lists its <see cref="ResizableHost"/> had made when it was last measured and arranged:
    /// RenderFrame makes its list before it lays out. An element that invalidates its layout invalidates every parent's,
    /// so the counts also show whether the session had layout left to do for anything inside.
    /// </summary>
    private sealed class LayoutProbe : UiElement
    {
        private int _measures, _arranges, _renders, _measuredAfter, _arrangedAfter;

        public LayoutProbe(UiElement? content = null)
        {
            if (content is not null) AddChild(content);
        }

        public (int Measures, int Arranges, int Renders) Counts => (_measures, _arranges, _renders);
        public (int Measured, int Arranged) LaidOutAfterRenderLists => (_measuredAfter, _arrangedAfter);
        public BRect Arranged { get; private set; }

        private int RenderLists => ((ResizableHost)Session!.Host).RenderListsCreated;

        protected override BSize MeasureCore(BSize availableSize)
        {
            _measures++;
            _measuredAfter = RenderLists;
            foreach (var child in Children) child.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(BRect finalRect)
        {
            _arranges++;
            _arrangedAfter = RenderLists;
            Arranged = finalRect;
            foreach (var child in Children) child.Arrange(finalRect);
        }

        protected override void RenderCore(UiRenderContext context)
        {
            _renders++;
            foreach (var child in Children) child.Render(context);
        }
    }

    private sealed class ResizableHost : IUiHost
    {
        public BSize ViewportSize { get; set; }
        public double Scale => 1;
        public int RenderListsCreated { get; private set; }

        public BRenderList CreateRenderList(int capacity = 0)
        {
            RenderListsCreated++;
            return new(capacity);
        }

        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
