using System.Diagnostics;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>
/// Records the UI side of every frame: how long layout and render-list construction took, what it
/// allocated, and how long each measured input waited for the frame that shows its result. GPU
/// rendering and presentation are not included; Broiler.Graphics reports them from a later release.
/// </summary>
internal sealed class FrameRecorder
{
    private readonly object _gate = new();
    private readonly List<long> _pendingInputs = [];
    private readonly List<double> _buildMs = [];
    private readonly List<double> _allocatedKb = [];
    private readonly List<double> _inputToFrameMs = [];
    private readonly SemaphoreSlim _frameBuilt = new(0);
    private int _frames;

    public double? FirstFrameMs { get; private set; }
    public double? InteractiveMs { get; private set; }
    public int Frames { get { lock (_gate) return _frames; } }

    public (long Started, long Allocated) BeginFrame() => (Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    public void EndFrame((long Started, long Allocated) begin)
    {
        long ended = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - begin.Allocated;
        lock (_gate)
        {
            _frames++;
            _buildMs.Add(Stopwatch.GetElapsedTime(begin.Started, ended).TotalMilliseconds);
            _allocatedKb.Add(allocated / 1024.0);
            foreach (long input in _pendingInputs)
                _inputToFrameMs.Add(Stopwatch.GetElapsedTime(input, ended).TotalMilliseconds);
            _pendingInputs.Clear();
            FirstFrameMs ??= SinceProcessStart();
        }
        _frameBuilt.Release();
    }

    /// <summary>Marks an input whose effect the next frame shows; call just before dispatching it.</summary>
    public void MarkInput()
    {
        lock (_gate) _pendingInputs.Add(Stopwatch.GetTimestamp());
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
            _pendingInputs.Clear();
        }
    }

    public FrameSamples Snapshot()
    {
        lock (_gate) return new([.. _buildMs], [.. _allocatedKb], [.. _inputToFrameMs]);
    }

    private static double SinceProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return (DateTime.Now - process.StartTime).TotalMilliseconds;
    }
}

/// <summary>Samples from a measured section.</summary>
internal sealed record FrameSamples(double[] BuildMs, double[] AllocatedKb, double[] InputToFrameMs)
{
    /// <summary>Nearest-rank percentile; NaN for no samples.</summary>
    public static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return double.NaN;
        var sorted = values.Order().ToArray();
        int rank = (int)Math.Ceiling(percentile / 100 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
