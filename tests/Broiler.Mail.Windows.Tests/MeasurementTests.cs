using System.Diagnostics;
using System.Text.Json;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Windows.Measurement;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>UI-12: the measurement harness's recorder, report, and phase timer.</summary>
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
        recorder.EndFrame((At(1), 0), new FramePhases(At(2), At(3), At(5)), At(9), 10 * 1024);
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
        recorder.EndFrame((At(1), 0), new FramePhases(At(1), At(1), At(1)), At(2), 0);
        recorder.EndFrame((At(10), 0), new FramePhases(At(10), At(10), At(10)), At(11), 0);
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
        recorder.EndFrame((At(1), 0), new FramePhases(At(2), At(3), At(5)), At(9), 0);
        recorder.EndPaint(At(12));

        var samples = recorder.Snapshot();
        Assert.Null(samples.Phases);
        AssertMs([8], samples.BuildMs);
        AssertMs([9], samples.InputToFrameMs);
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
        recorder.EndFrame((At(0), 0), null, At(4), 0);
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
