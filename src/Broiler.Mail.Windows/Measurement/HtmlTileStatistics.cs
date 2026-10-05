using System.Diagnostics;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>
/// What the HTML preview's tile cache did while a --measure run watched it: tiles found in the cache, tiles
/// drawn because they were not (and how long drawing them and handing them to the renderer took), tiles drawn
/// again after the cache evicted them, evictions, and tiles discarded because the page width, zoom, or scale
/// changed. The preview window's thread counts; any thread may read. A preview nobody measures has none, and
/// its tiles cost one null check each.
/// </summary>
internal sealed class HtmlTileStatistics
{
    private readonly object _gate = new();
    private readonly List<double> _rasterMs = [];
    private readonly List<double> _uploadMs = [];
    // Tiles drawn since tiles were last discarded, so one drawn again after an eviction is told apart.
    private readonly HashSet<(int Row, int Column)> _drawn = [];
    private int _hits, _misses, _rerasters, _evictions, _discards, _layouts;
    private long _layoutTicks, _longestLayoutTicks, _peakBytes;

    /// <summary>Tiles drawn so far.</summary>
    public int Misses { get { lock (_gate) return _misses; } }

    /// <summary>A tile in view was cached.</summary>
    public void Hit()
    {
        lock (_gate) _hits++;
    }

    /// <summary>A tile in view was not cached and has been drawn (rasterized) and handed to the renderer (uploaded).</summary>
    /// <param name="cachedBytes">The cache's size with the new tile.</param>
    public void Drawn((int Row, int Column) tile, long rasterTicks, long uploadTicks, long cachedBytes)
    {
        lock (_gate)
        {
            _misses++;
            if (!_drawn.Add(tile)) _rerasters++;
            _rasterMs.Add(Milliseconds(rasterTicks));
            _uploadMs.Add(Milliseconds(uploadTicks));
            _peakBytes = Math.Max(_peakBytes, cachedBytes);
        }
    }

    /// <summary>The least recently used tile left the cache to keep it within its count and byte limits.</summary>
    public void Evicted()
    {
        lock (_gate) _evictions++;
    }

    /// <summary>Every cached tile (<paramref name="tiles"/> of them) was dropped: a new layout, width, zoom, or scale.</summary>
    public void Discarded(int tiles)
    {
        lock (_gate)
        {
            _discards += tiles;
            _drawn.Clear();
        }
    }

    /// <summary>The document was laid out, taking <paramref name="ticks"/> (<see cref="Stopwatch"/> ticks).</summary>
    public void LaidOut(long ticks)
    {
        lock (_gate)
        {
            _layouts++;
            _layoutTicks += ticks;
            _longestLayoutTicks = Math.Max(_longestLayoutTicks, ticks);
        }
    }

    public HtmlTileSnapshot Snapshot()
    {
        lock (_gate)
            return new(_hits, _misses, _rerasters, _evictions, _discards, [.. _rasterMs], [.. _uploadMs], _peakBytes,
                _layouts, Milliseconds(_layoutTicks), Milliseconds(_longestLayoutTicks));
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

/// <summary>Counts and times from <see cref="HtmlTileStatistics"/>; misses are tiles drawn, re-rasters the subset drawn again after an eviction.</summary>
internal sealed record HtmlTileSnapshot(int Hits, int Misses, int Rerasters, int Evictions, int Discards, double[] RasterMs, double[] UploadMs,
    long PeakBytes, int Layouts, double LayoutMsTotal, double LayoutMsMax);

/// <summary>
/// What a --measure run records about the HTML preview it opens: the preview's frames, its tile cache, and the
/// simulated scale (--scale) it renders at, or null for Windows' own.
/// </summary>
internal sealed record PreviewMeasurement(FrameRecorder Frames, HtmlTileStatistics Tiles, double? SimulatedScale);
