using System.Diagnostics;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>
/// Records the UI side of every frame: how long layout and render-list construction took, what it
/// allocated, and how long each measured input waited for the frame that shows its result. With
/// <see cref="Detail"/> (--detail) it also times each frame's phases and the host's render and
/// present call; without it, a frame is timed as one span, as in the 2 October baseline.
/// </summary>
internal sealed class FrameRecorder(bool detail = false)
{
    private readonly object _gate = new();
    private readonly List<long> _pendingInputs = [];
    private readonly List<double> _buildMs = [];
    private readonly List<double> _allocatedKb = [];
    private readonly List<double> _inputToFrameMs = [];
    private readonly List<int> _tilesDrawn = [];
    // --detail only.
    private readonly List<double> _dispatchMs = [];
    private readonly List<double> _drainMs = [];
    private readonly List<double> _measureMs = [];
    private readonly List<double> _arrangeMs = [];
    private readonly List<double> _renderListMs = [];
    private readonly List<double> _renderPresentMs = [];
    private readonly List<double> _inputToPresentMs = [];
    // The inputs the last frame showed, until the paint that built it returns.
    private readonly List<long> _framedInputs = [];
    private long _lastFrameEnded;
    private bool _paintPending;
    private readonly SemaphoreSlim _frameBuilt = new(0);
    private int _frames;

    /// <summary>Phase and render-and-present timers are on (--detail).</summary>
    public bool Detail { get; } = detail;
    public double? FirstFrameMs { get; private set; }
    /// <summary>When the first frame was built, as a <see cref="Stopwatch"/> timestamp.</summary>
    public long? FirstFrameAt { get; private set; }
    public double? InteractiveMs { get; private set; }
    public int Frames { get { lock (_gate) return _frames; } }

    public (long Started, long Allocated) BeginFrame() => (Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    /// <param name="phases">With --detail, when the frame's phases ended; see <see cref="FramePhaseTimer"/>.</param>
    /// <param name="tilesDrawn">HTML preview tiles the frame drew because they were not cached.</param>
    public void EndFrame((long Started, long Allocated) begin, FramePhases? phases = null, int tilesDrawn = 0) =>
        EndFrame(begin, phases, tilesDrawn, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    internal void EndFrame((long Started, long Allocated) begin, FramePhases? phases, int tilesDrawn, long ended, long allocatedAtEnd)
    {
        lock (_gate)
        {
            _frames++;
            _buildMs.Add(Milliseconds(begin.Started, ended));
            _allocatedKb.Add((allocatedAtEnd - begin.Allocated) / 1024.0);
            _tilesDrawn.Add(tilesDrawn);
            foreach (long input in _pendingInputs)
                _inputToFrameMs.Add(Milliseconds(input, ended));
            if (Detail)
            {
                if (phases is { } phase)
                {
                    _drainMs.Add(Milliseconds(begin.Started, phase.Drained));
                    _measureMs.Add(Milliseconds(phase.Drained, phase.Measured));
                    _arrangeMs.Add(Milliseconds(phase.Measured, phase.Arranged));
                    _renderListMs.Add(Milliseconds(phase.Arranged, ended));
                }
                // A frame whose paint was never timed (one built without a paint message) gets no present sample.
                _framedInputs.Clear();
                _framedInputs.AddRange(_pendingInputs);
                _lastFrameEnded = ended;
                _paintPending = true;
            }
            _pendingInputs.Clear();
            FirstFrameAt ??= ended;
            FirstFrameMs ??= SinceProcessStart();
        }
        _frameBuilt.Release();
    }

    /// <summary>
    /// --detail: the paint (or resize) that built the last frame has returned, so the host has rendered and
    /// presented it. The time since the build ended is the host's render-and-present call on the UI thread:
    /// Direct2D drawing, EndDraw, and a vsynced Present, which can wait for a buffer. It is CPU wall time,
    /// not GPU execution or the moment the frame reaches the screen.
    /// </summary>
    public void EndPaint() => EndPaint(Stopwatch.GetTimestamp());

    internal void EndPaint(long ended)
    {
        lock (_gate)
        {
            if (!_paintPending) return;
            _paintPending = false;
            _renderPresentMs.Add(Milliseconds(_lastFrameEnded, ended));
            foreach (long input in _framedInputs)
                _inputToPresentMs.Add(Milliseconds(input, ended));
            _framedInputs.Clear();
        }
    }

    /// <summary>Marks an input whose effect the next frame shows; call just before dispatching it.</summary>
    public void MarkInput() => MarkInput(Stopwatch.GetTimestamp());

    internal void MarkInput(long at)
    {
        lock (_gate) _pendingInputs.Add(at);
    }

    /// <summary>--detail: the input marked last has been dispatched; <paramref name="started"/> is when dispatching began.</summary>
    public void EndDispatch(long started) => EndDispatch(started, Stopwatch.GetTimestamp());

    internal void EndDispatch(long started, long ended)
    {
        if (!Detail) return;
        lock (_gate) _dispatchMs.Add(Milliseconds(started, ended));
    }

    /// <summary>Forgets inputs that changed nothing on screen, so a later frame is not charged to them.</summary>
    public void DropPendingInputs()
    {
        lock (_gate) _pendingInputs.Clear();
    }

    /// <summary>The prepared fixture is on screen and accepts input.</summary>
    public void MarkInteractive()
    {
        lock (_gate) InteractiveMs ??= SinceProcessStart();
    }

    /// <summary>Waits until a frame newer than <paramref name="after"/> was built.</summary>
    public bool WaitForFrame(int after, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Frames <= after)
        {
            var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
            if (remaining <= TimeSpan.Zero || !_frameBuilt.Wait(remaining)) return Frames > after;
        }
        return true;
    }

    /// <summary>Starts a measured section: frame and latency samples before it are discarded.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _buildMs.Clear();
            _allocatedKb.Clear();
            _inputToFrameMs.Clear();
            _tilesDrawn.Clear();
            _pendingInputs.Clear();
            _dispatchMs.Clear();
            _drainMs.Clear();
            _measureMs.Clear();
            _arrangeMs.Clear();
            _renderListMs.Clear();
            _renderPresentMs.Clear();
            _inputToPresentMs.Clear();
            _framedInputs.Clear();
            _paintPending = false;
        }
    }

    public FrameSamples Snapshot()
    {
        lock (_gate)
            return new([.. _buildMs], [.. _allocatedKb], [.. _inputToFrameMs])
            {
                TilesDrawn = [.. _tilesDrawn],
                Phases = Detail ? new([.. _dispatchMs], [.. _drainMs], [.. _measureMs], [.. _arrangeMs], [.. _renderListMs],
                    [.. _renderPresentMs], [.. _inputToPresentMs]) : null,
            };
    }

    private static double Milliseconds(long started, long ended) => (ended - started) * 1000.0 / Stopwatch.Frequency;

    private static double SinceProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return (DateTime.Now - process.StartTime).TotalMilliseconds;
    }
}

/// <summary>When a frame's phases ended, as <see cref="Stopwatch"/> timestamps: posted UI work drained, roots measured, roots arranged.</summary>
internal readonly record struct FramePhases(long Drained, long Measured, long Arranged);

/// <summary>Samples from a measured section.</summary>
internal sealed record FrameSamples(double[] BuildMs, double[] AllocatedKb, double[] InputToFrameMs)
{
    /// <summary>Per frame, the HTML preview tiles it drew; zero for the main window.</summary>
    public int[] TilesDrawn { get; init; } = [];

    /// <summary>With --detail, the phase timers; otherwise null.</summary>
    public FramePhaseSamples? Phases { get; init; }

    /// <summary>Nearest-rank percentile; NaN for no samples.</summary>
    public static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return double.NaN;
        var sorted = values.Order().ToArray();
        int rank = (int)Math.Ceiling(percentile / 100 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>Build times of the frames that drew at least one tile (true) or none (false).</summary>
    public double[] BuildMsWhere(bool drewTiles) =>
        BuildMs.Where((_, index) => index < TilesDrawn.Length && (TilesDrawn[index] > 0) == drewTiles).ToArray();
}

/// <summary>
/// --detail timers. Dispatch is per input; drain, measure, arrange, and render list are per frame and add up
/// to the frame build; render and present (and input to present) are per frame whose paint returned.
/// </summary>
internal sealed record FramePhaseSamples(double[] DispatchMs, double[] DrainMs, double[] MeasureMs, double[] ArrangeMs, double[] RenderListMs,
    double[] RenderPresentMs, double[] InputToPresentMs);
