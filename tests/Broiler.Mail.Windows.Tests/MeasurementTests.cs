using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Measurement;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Standard;

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
        using var timed = SettledShell.Start();

        var expected = Commands(plain.Session.RenderFrame());
        var actual = Commands(FramePhaseTimer.Render(timed.Session, Stopwatch.GetTimestamp(), out var phases));
        Assert.Equal(expected, actual);
        Assert.True(phases.Drained <= phases.Measured && phases.Measured <= phases.Arranged);

        plain.Host.ViewportSize = timed.Host.ViewportSize = new BSize(900, 640);
        Assert.Equal(Commands(plain.Session.RenderFrame()), Commands(FramePhaseTimer.Render(timed.Session, Stopwatch.GetTimestamp(), out _)));
    }

    private static string[] Commands(BRenderList list) => list.Commands.Select(command => command.ToString()).ToArray();

    [Fact]
    public void The_Phase_Timer_Leaves_RenderFrame_Only_Rendering()
    {
        var host = new ResizableHost { ViewportSize = new BSize(800, 600) };
        using var session = new StandardUiSessionBuilder().Build(host);
        var root = new CountingElement();
        session.AddRoot(root);

        FramePhaseTimer.Render(session, Stopwatch.GetTimestamp(), out _);
        Assert.Equal((1, 1, 1), (root.Measures, root.Arranges, root.Renders));
        Assert.Equal(new BRect(0, 0, 800, 600), root.Arranged);
        FramePhaseTimer.Render(session, Stopwatch.GetTimestamp(), out _);
        Assert.Equal((1, 1, 2), (root.Measures, root.Arranges, root.Renders));
        host.ViewportSize = new BSize(640, 480);
        FramePhaseTimer.Render(session, Stopwatch.GetTimestamp(), out _);
        Assert.Equal((2, 2, 3), (root.Measures, root.Arranges, root.Renders));
    }

    private sealed class CountingElement : UiElement
    {
        public int Measures { get; private set; }
        public int Arranges { get; private set; }
        public int Renders { get; private set; }
        public BRect Arranged { get; private set; }

        protected override BSize MeasureCore(BSize availableSize)
        {
            Measures++;
            return availableSize;
        }

        protected override void ArrangeCore(BRect finalRect)
        {
            Arranges++;
            Arranged = finalRect;
        }

        protected override void RenderCore(UiRenderContext context) => Renders++;
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
        var preview = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
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
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(30));
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
        string directory = Path.Combine(Path.GetTempPath(), "Broiler.Mail.Measurements", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteReport(directory, "idle-1", "idle", null, frames: 0, buildP95: null, inputP95: null, allocationP50: null);
            // The lower median of two runs: 5 ms, within 8 ms, although the other run took 9.
            WriteReport(directory, "scroll-1", "scroll", 200, frames: 300, buildP95: 5, inputP95: 6, allocationP50: 300);
            WriteReport(directory, "scroll-2", "scroll", 200, frames: 300, buildP95: 9, inputP95: 7, allocationP50: 310);
            WriteReport(directory, "resize-1", "resize", null, frames: 30, buildP95: 15, inputP95: 18, allocationP50: 400);
            WriteReport(directory, "long-html-1", "long-html", null, frames: 600, buildP95: 110, inputP95: 112, allocationP50: 4);

            var (exitCode, output) = RunSummary("-Evaluate", directory);
            Assert.True(exitCode == 0, output);
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "summary.json")));
            var verdicts = summary.RootElement.GetProperty("verdicts").EnumerateArray()
                .ToDictionary(item => $"{item.GetProperty("workload").GetString()}/{item.GetProperty("field").GetString()}",
                    item => (item.GetProperty("result").GetString(), item.GetProperty("scale").GetString()));
            Assert.Equal(("pass", "system 150 %"), verdicts["idle/frames"]);
            Assert.Equal(("pass", "simulated 200 %"), verdicts["scroll/buildMsP95"]);
            Assert.Equal(("over", "simulated 200 %"), verdicts["scroll/allocatedKbPerFrameP50"]);
            Assert.Equal("over", verdicts["resize/buildMsP95"].Item1);
            Assert.Equal("over", verdicts["resize/inputToFrameMsP95"].Item1);
            // The preview workloads have no proposed target.
            Assert.DoesNotContain(verdicts.Keys, key => key.StartsWith("long-html/", StringComparison.Ordinal));
            Assert.Equal(3, summary.RootElement.GetProperty("budgets").GetProperty("over").GetInt32());
            string markdown = File.ReadAllText(Path.Combine(directory, "summary.md"));
            Assert.Contains("| scroll | simulated 200 % | 300 |", markdown);
            Assert.Contains("not real DPI results", markdown);
            Assert.Contains("long-html: No target is proposed", markdown);

            Assert.Equal(1, RunSummary("-Evaluate", directory, "-Strict").ExitCode);
            Assert.Equal(0, RunSummary("-Evaluate", directory, "-Strict", "-Workloads", "idle,long-html").ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteReport(string directory, string name, string workload, int? simulated, int frames, double? buildP95, double? inputP95, double? allocationP50)
    {
        using var stream = File.Create(Path.Combine(directory, name + ".json"));
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("reportVersion", 2);
        writer.WriteString("workload", workload);
        writer.WriteString("theme", "light");
        writer.WriteString("windowSize", "1100x720");
        writer.WriteNumber("dpiScale", simulated is { } percent ? percent / 100.0 : 1.5);
        if (simulated is { } simulatedPercent) writer.WriteNumber("simulatedScalePercent", simulatedPercent);
        else writer.WriteNull("simulatedScalePercent");
        writer.WriteBoolean("detail", false);
        writer.WriteString("build", "Release, NativeAOT");
        writer.WriteString("machine", "test");
        writer.WriteNumber("startupFirstFrameMs", 230);
        writer.WriteNumber("startupInteractiveMs", 250);
        writer.WriteNumber("steps", frames);
        writer.WriteNumber("frames", frames);
        foreach (var (field, value) in new[] { ("buildMsP50", buildP95 / 2), ("buildMsP95", buildP95), ("buildMsP99", buildP95),
            ("inputToFrameMsP95", inputP95), ("allocatedKbPerFrameP50", allocationP50), ("workingSetMb", (double?)70) })
        {
            if (value is { } number) writer.WriteNumber(field, number);
            else writer.WriteNull(field);
        }
        writer.WriteEndObject();
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

        private SettledShell(MailShellView shell, UiSession session, ResizableHost host, SemaphoreSlim woken)
        {
            _shell = shell;
            Session = session;
            Host = host;
            _woken = woken;
        }

        public UiSession Session { get; }
        public ResizableHost Host { get; }

        public static SettledShell Start()
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
            session.AddRoot(shell.Window);
            return new SettledShell(shell, session, host, woken);
        }

        public void Dispose()
        {
            Session.Dispose();
            _shell.Dispose();
            _woken.Dispose();
        }
    }

    private sealed class ResizableHost : IUiHost
    {
        public BSize ViewportSize { get; set; }
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
