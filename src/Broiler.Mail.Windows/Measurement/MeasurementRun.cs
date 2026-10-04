using System.Diagnostics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Windowing;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Windows.Hosting;
using Broiler.UI;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Splitter;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>Fixed interaction sequences for the UI-12 baseline. Each runs on a prepared demo fixture.</summary>
internal enum MeasureWorkload { Idle, Scroll, Select, Type, Theme, Resize, Splitter }

/// <summary>
/// Runs one workload against the live window and writes a report. Inputs are synthesized and dispatched
/// through the window's own input path, one at a time: each waits for the frame that shows it, then a
/// short pause stands in for human cadence. Native message delivery and the input bridge are not part
/// of the measurement.
/// </summary>
internal sealed class MeasurementRun
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);
    private const int CadenceMs = 8;
#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _input = new("broiler-mail-measure");
#pragma warning restore CS0618
    private readonly WindowsMailWindow _window;
    private readonly FrameRecorder _recorder;
    private readonly MeasureWorkload _workload;
    private int _steps;
    private int _unpainted;

    private MeasurementRun(WindowsMailWindow window, FrameRecorder recorder, MeasureWorkload workload)
    {
        _window = window;
        _recorder = recorder;
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
            using var sampler = AllocationSampler.Requested ? new AllocationSampler() : null;
            _recorder.Reset();
            sampler?.Start();
            var elapsed = Stopwatch.StartNew();
            RunWorkload();
            // Frames still caused by the last input belong to the section.
            Thread.Sleep(250);
            elapsed.Stop();
            sampler?.Stop();
            double dpiScale = 0, systemScale = 0;
            Ui(() => (dpiScale, systemScale) = (_window.DpiScale, _window.SystemDpiScale));
            var report = MeasurementReport.Create(options, _workload, _recorder, _recorder.Snapshot(), _steps, _unpainted, elapsed.Elapsed,
                new MeasurementScale(dpiScale, systemScale, options.ScalePercent));
            report.Write(options.Report);
            Console.WriteLine(report.Summary());
            if (sampler is not null)
            {
                Console.WriteLine("Sampled allocations by type (MB, samples):");
                foreach (var (type, mb, samples) in sampler.Top(25))
                    Console.WriteLine($"  {mb,8:0.00}  {samples,5}  {type}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Measurement failed: {exception.Message}");
            exitCode = 1;
        }
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
        }
    }

    private void Key(int virtualKey)
    {
        _window.DispatchMeasured(_input.FromKey(new BKeyEventArgs(virtualKey, false, false, false), KeyboardKeyTransition.Down));
        _window.DispatchUnmeasured(_input.FromKey(new BKeyEventArgs(virtualKey, false, false, false), KeyboardKeyTransition.Up));
    }

    private void Step(Action action)
    {
        int before = _recorder.Frames;
        Ui(action);
        _steps++;
        if (!_recorder.WaitForFrame(before, FrameTimeout))
        {
            _unpainted++;
            _recorder.DropPendingInputs();
        }
        Thread.Sleep(CadenceMs);
    }

    /// <summary>Runs on the window's thread and waits for it.</summary>
    private void Ui(Action action)
    {
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        if (!_window.RunOnUiThread(() => { try { action(); } catch (Exception e) { failure = e; } finally { done.Set(); } }))
            throw new InvalidOperationException("The window is closed.");
        if (!done.Wait(FrameTimeout * 5)) throw new TimeoutException("The window did not run the step.");
        if (failure is not null) throw failure;
    }

    private T Find<T>(UiElement root) where T : UiElement
    {
        T? found = null;
        Ui(() => found = Descendants(root).OfType<T>().FirstOrDefault());
        return found ?? throw new InvalidOperationException($"The fixture has no {typeof(T).Name}.");
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
