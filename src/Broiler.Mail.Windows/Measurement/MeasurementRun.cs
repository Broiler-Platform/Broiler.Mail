using System.Diagnostics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Windows.Hosting;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>Fixed interaction sequences for the UI-12 baseline. Each runs on a prepared demo fixture.</summary>
internal enum MeasureWorkload { Idle, Scroll, Select, Type, Theme, Resize, Splitter, LongHtml, PreviewZoom }

/// <summary>
/// Runs one workload against the live window and writes a report. Inputs are synthesized and dispatched
/// through the window's own input path, one at a time: each waits for the frame that shows it, then a
/// short pause stands in for human cadence. Native message delivery and the input bridge are not part
/// of the measurement. The preview workloads open the reader's HTML preview and measure that window,
/// which draws on its own thread.
/// </summary>
internal sealed class MeasurementRun
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);
    // A preview frame draws every tile that comes into view, and a zoom lays the whole document out first.
    private static readonly TimeSpan PreviewFrameTimeout = TimeSpan.FromSeconds(15);
    // The opening frames have ended once the preview has drawn none for this long.
    private static readonly TimeSpan PreviewQuiet = TimeSpan.FromMilliseconds(300);
    private const int CadenceMs = 8;
    // A fast turn of the wheel: three notches (96 DIPs) per input, so one pass through the long document is
    // about 340 inputs rather than a thousand.
    private const int PreviewWheelNotches = 3;
    private const int MaxPreviewSteps = 2000;
#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _input = new("broiler-mail-measure");
    private readonly StandardLegacyGraphicsInputAdapter _previewInput = new("broiler-mail-measure-preview");
#pragma warning restore CS0618
    private readonly WindowsMailWindow _window;
    private readonly FrameRecorder _recorder;
    private readonly MeasureWorkload _workload;
    // The recorder of the window whose frames the steps wait for: the main window's, or the preview's.
    private FrameRecorder _measured;
    private TimeSpan _frameTimeout = FrameTimeout;
    private PreviewMeasurement? _preview;
    private HtmlPreviewWindow? _previewWindow;
    private double _openToFirstFrameMs = double.NaN;
    private FrameSamples? _opening;
    private int _steps;
    private int _unpainted;

    private MeasurementRun(WindowsMailWindow window, FrameRecorder recorder, MeasureWorkload workload)
    {
        _window = window;
        _recorder = recorder;
        _measured = recorder;
        _workload = workload;
    }

    public static void Start(WindowsMailWindow window, FrameRecorder recorder, DemoOptions options, Task scenarioReady)
    {
        var run = new MeasurementRun(window, recorder, options.Measure!.Value);
        new Thread(() => run.Run(options, scenarioReady)) { IsBackground = true, Name = "Broiler.Mail measurement" }.Start();
    }

    private void Run(DemoOptions options, Task scenarioReady)
    {
        int exitCode = 0;
        try
        {
            if (!scenarioReady.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("The demo scenario did not become ready.");
            int before = _recorder.Frames;
            Ui(() => { _recorder.MarkInteractive(); _window.Invalidate(); });
            _recorder.WaitForFrame(before, FrameTimeout);
            // Let posted work and the first animations settle before the measured section starts.
            Thread.Sleep(500);
            if (DemoOptions.MeasuresPreview(_workload))
            {
                // Previews opened from now on record their frames and tiles, at the simulated scale if there is one.
                _preview = new PreviewMeasurement(new FrameRecorder(options.Detail), new HtmlTileStatistics(), options.ScalePercent / 100.0);
                _window.HtmlPreview.Measurement = _preview;
                _frameTimeout = PreviewFrameTimeout;
            }
            using var sampler = AllocationSampler.Requested ? new AllocationSampler() : null;
            _recorder.Reset();
            sampler?.Start();
            var elapsed = Stopwatch.StartNew();
            RunWorkload();
            // Frames still caused by the last input belong to the section.
            Thread.Sleep(250);
            elapsed.Stop();
            sampler?.Stop();
            var samples = _measured.Snapshot();
            PreviewResult? preview = _previewWindow is null ? null : ReadPreview();
            double dpiScale = 0, systemScale = 0;
            Ui(() => (dpiScale, systemScale) = (_window.DpiScale, _window.SystemDpiScale));
            var report = MeasurementReport.Create(options, _workload, _recorder, samples, _steps, _unpainted, elapsed.Elapsed,
                new MeasurementScale(dpiScale, systemScale, options.ScalePercent), preview);
            report.Write(options.Report);
            Console.WriteLine(report.Summary());
            if (sampler is not null)
            {
                Console.WriteLine("Sampled allocations by type (MB, samples):");
                foreach (var (type, mb, sampled) in sampler.Top(25))
                    Console.WriteLine($"  {mb,8:0.00}  {sampled,5}  {type}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Measurement failed: {exception.Message}");
            exitCode = 1;
        }
        ClosePreview();
        Ui(() => _window.CloseAfterMeasurement(exitCode));
    }

    private void RunWorkload()
    {
        var shell = _window.Shell;
        switch (_workload)
        {
            case MeasureWorkload.Idle:
                // Nothing changes, so nothing should be drawn.
                Thread.Sleep(3000);
                break;
            case MeasureWorkload.Scroll:
            {
                var list = Find<StandardListView>(shell.Navigation.SelectedTab!.Content!);
                BPoint over = default;
                Ui(() => over = new(list.Bounds.X + list.Bounds.Width / 2, list.Bounds.Y + list.Bounds.Height / 2));
                for (int i = 0; i < 150; i++) Step(() => _window.DispatchMeasured(_input.FromMouseWheel(new BMouseWheelEventArgs(over, -1, BMouseButtons.None))));
                for (int i = 0; i < 150; i++) Step(() => _window.DispatchMeasured(_input.FromMouseWheel(new BMouseWheelEventArgs(over, 1, BMouseButtons.None))));
                break;
            }
            case MeasureWorkload.Select:
            {
                var list = Find<StandardListView>(shell.Navigation.SelectedTab!.Content!);
                Ui(() => _window.Session.SetFocus(list));
                for (int i = 0; i < 40; i++) Step(() => Key(0x28)); // Down selects the next message and loads its body.
                break;
            }
            case MeasureWorkload.Type:
            {
                Ui(() =>
                {
                    shell.Navigation.SelectTab("compose");
                    if (!_window.Model.Composer.HasDraft) _window.Model.Composer.StartNew();
                });
                Thread.Sleep(300);
                var body = Find<StandardRichEdit>(shell.Navigation.SelectedTab!.Content!);
                Ui(() => _window.Session.SetFocus(body));
                const string text = "The quick brown fox jumps over the lazy dog. ";
                for (int i = 0; i < 600; i++)
                {
                    char character = text[i % text.Length];
                    if (i % 60 == 59) Step(() => Key(0x0D));
                    else Step(() => _window.DispatchMeasured(_input.FromText(new BTextInputEventArgs(character))));
                }
                break;
            }
            case MeasureWorkload.Theme:
                for (int i = 0; i < 20; i++)
                {
                    var tokens = i % 2 == 0 ? StandardThemeTokens.Dark : StandardThemeTokens.Light;
                    Step(() => _window.ApplyThemeForMeasurement(tokens));
                }
                break;
            case MeasureWorkload.Resize:
            {
                int width = 0, height = 0;
                Ui(() => (width, height) = _window.OuterSize());
                for (int i = 0; i < 30; i++)
                {
                    int w = width - (i % 2 == 0 ? 160 : 0), h = height - (i % 3 == 0 ? 80 : 0);
                    Step(() => _window.ResizeForMeasurement(w, h));
                }
                Ui(() => _window.ResizeForMeasurement(width, height));
                _recorder.DropPendingInputs();
                break;
            }
            case MeasureWorkload.Splitter:
            {
                var splitter = Find<UiSplitter>(shell.Navigation.SelectedTab!.Content!);
                Ui(() => _window.Session.SetFocus(splitter));
                for (int i = 0; i < 60; i++) Step(() => Key(i / 15 % 2 == 0 ? 0x27 : 0x25)); // Right, then Left, in runs of 15.
                break;
            }
            case MeasureWorkload.LongHtml:
            {
                var preview = OpenPreview();
                BPoint over = default;
                OnPreview(() => over = new(preview.HtmlView.Bounds.X + preview.HtmlView.Bounds.Width / 2, preview.HtmlView.Bounds.Y + preview.HtmlView.Bounds.Height / 2));
                // To the end of the document and back to the top: every tile is drawn on the way down, and on the
                // way back only the last ones the cache could hold are still there.
                foreach (int direction in (int[])[1, -1])
                {
                    bool atEnd = false;
                    for (int step = 0; !atEnd; step++)
                    {
                        if (step == MaxPreviewSteps) throw new InvalidOperationException("The preview did not reach the end of the document.");
                        PreviewStep(() =>
                        {
                            preview.DispatchMeasured(_previewInput.FromMouseWheel(new BMouseWheelEventArgs(over, -direction * PreviewWheelNotches, BMouseButtons.None)));
                            var scroll = preview.HtmlView.Scroll;
                            atEnd = direction > 0
                                ? scroll.VerticalOffset >= scroll.ExtentSize.Height - scroll.ViewportSize.Height - 0.5
                                : scroll.VerticalOffset <= 0.5;
                        });
                    }
                }
                break;
            }
            case MeasureWorkload.PreviewZoom:
            {
                var preview = OpenPreview();
                double start = 0;
                OnPreview(() => start = preview.Zoom);
                // Ctrl+Plus to the largest zoom, Ctrl+Minus to the smallest, and Ctrl+Plus back to where it started.
                // Every step lays the document out again and draws each tile in view anew, on the preview's thread.
                ZoomUntil(preview, 1, () => PreviewZoom.IsAtMaximum(preview.Zoom));
                ZoomUntil(preview, -1, () => PreviewZoom.IsAtMinimum(preview.Zoom));
                ZoomUntil(preview, 1, () => PreviewZoom.AreSame(preview.Zoom, start));
                break;
            }
        }
    }

    private void Key(int virtualKey)
    {
        _window.DispatchMeasured(_input.FromKey(new BKeyEventArgs(virtualKey, false, false, false), KeyboardKeyTransition.Down));
        _window.DispatchUnmeasured(_input.FromKey(new BKeyEventArgs(virtualKey, false, false, false), KeyboardKeyTransition.Up));
    }

    private void ZoomUntil(HtmlPreviewWindow preview, int direction, Func<bool> reached)
    {
        // "=" (Ctrl+Plus) or "-" (Ctrl+Minus) on the main keys, with Control held.
        var key = new BKeyEventArgs(direction > 0 ? 0xBB : 0xBD, control: true, shift: false, alt: false);
        bool done = false;
        for (int step = 0; !done; step++)
        {
            if (step == PreviewZoom.Levels(1).Count + 1) throw new InvalidOperationException("The preview zoom did not reach its level.");
            PreviewStep(() =>
            {
                preview.DispatchMeasured(_previewInput.FromKey(key, KeyboardKeyTransition.Down));
                preview.Dispatch(_previewInput.FromKey(key, KeyboardKeyTransition.Up));
                done = reached();
            });
        }
    }

    /// <summary>
    /// Selects the reader's Open HTML preview and waits for the preview's first frame and for its opening
    /// frames to end. The opening is reported apart; the workload's frames start after it.
    /// </summary>
    private HtmlPreviewWindow OpenPreview()
    {
        var frames = _preview!.Frames;
        var open = Find<StandardButton>(_window.Shell.Navigation.SelectedTab!.Content!, button => button.Text == HtmlMessagePreview.OpenText);
        long clicked = 0;
        Ui(() =>
        {
            clicked = Stopwatch.GetTimestamp();
            open.Click(UiButtonActivationReason.Pointer);
        });
        if (!frames.WaitForFrame(0, PreviewFrameTimeout)) throw new TimeoutException("The HTML preview drew no frame.");
        _openToFirstFrameMs = Stopwatch.GetElapsedTime(clicked, frames.FirstFrameAt!.Value).TotalMilliseconds;
        _previewWindow = _window.HtmlPreview.Window ?? throw new InvalidOperationException("The HTML preview closed while opening.");
        var deadline = Stopwatch.StartNew();
        while (frames.WaitForFrame(frames.Frames, PreviewQuiet))
            if (deadline.Elapsed > PreviewFrameTimeout) throw new TimeoutException("The HTML preview kept drawing after it opened.");
        _opening = frames.Snapshot();
        frames.Reset();
        _measured = frames;
        return _previewWindow;
    }

    private PreviewResult ReadPreview()
    {
        var window = _previewWindow!;
        BSize size = default;
        double scale = 0, zoom = 0;
        long cachedBytes = 0;
        int cachedTiles = 0;
        OnPreview(() =>
        {
            (size, scale, zoom) = (window.ClientSize, window.DpiScale, window.DefaultZoom);
            (cachedBytes, cachedTiles) = (window.HtmlView.Content.CachedTileBytes, window.HtmlView.Content.CachedTileCount);
        });
        return new PreviewResult(size, scale, zoom, _openToFirstFrameMs, _opening!, _preview!.Tiles.Snapshot(), cachedBytes, cachedTiles);
    }

    /// <summary>Closes a preview this run opened and waits until its thread has let it go.</summary>
    private void ClosePreview()
    {
        if (_previewWindow is null) return;
        _window.HtmlPreview.Close();
        var deadline = Stopwatch.StartNew();
        while (_window.HtmlPreview.Window is not null && deadline.Elapsed < PreviewFrameTimeout) Thread.Sleep(20);
        _window.HtmlPreview.Measurement = null;
    }

    private void Step(Action action) => Step(Ui, action);

    private void PreviewStep(Action action) => Step(OnPreview, action);

    private void Step(Action<Action> run, Action action)
    {
        int before = _measured.Frames;
        run(action);
        _steps++;
        if (!_measured.WaitForFrame(before, _frameTimeout))
        {
            _unpainted++;
            _measured.DropPendingInputs();
        }
        Thread.Sleep(CadenceMs);
    }

    /// <summary>Runs on the main window's thread and waits for it.</summary>
    private void Ui(Action action) => RunOn(_window.RunOnUiThread, action, "window");

    /// <summary>Runs on the preview window's thread and waits for it.</summary>
    private void OnPreview(Action action) => RunOn(_previewWindow!.RunOnUiThread, action, "HTML preview");

    private void RunOn(Func<Action, bool> post, Action action, string window)
    {
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        if (!post(() => { try { action(); } catch (Exception e) { failure = e; } finally { done.Set(); } }))
            throw new InvalidOperationException($"The {window} is closed.");
        if (!done.Wait(_frameTimeout * 5)) throw new TimeoutException($"The {window} did not run the step.");
        if (failure is not null) throw failure;
    }

    private T Find<T>(UiElement root, Func<T, bool>? match = null) where T : UiElement
    {
        T? found = null;
        Ui(() => found = Descendants(root).OfType<T>().FirstOrDefault(item => match?.Invoke(item) ?? true));
        return found ?? throw new InvalidOperationException($"The fixture has no {typeof(T).Name}.");
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
