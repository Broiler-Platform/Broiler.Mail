using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>One workload's results with the machine and build they came from.</summary>
internal sealed class MeasurementReport
{
    /// <summary>2 added the scale and --detail fields; the fields of version 1 keep their names and meaning.</summary>
    public const int Version = 2;

    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    private MeasurementReport() { }

    public static MeasurementReport Create(DemoOptions options, MeasureWorkload workload, FrameRecorder recorder, FrameSamples samples,
        int steps, int unpainted, TimeSpan elapsed, MeasurementScale scale)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var report = new MeasurementReport();
        report.Add("reportVersion", Version);
        report.Add("workload", DemoOptions.Workloads.Single(item => item.Workload == workload).Name);
        report.Add("scenario", options.Name);
        report.Add("theme", options.Theme.ToString().ToLowerInvariant());
        report.Add("windowSize", $"{options.Width}x{options.Height}");
        report.Add("dpiScale", scale.DpiScale);
        // A simulated scale renders Mail at that scale on this display; Windows' DPI, the frame, and monitors are unchanged.
        report.Add("scaleKind", scale.SimulatedPercent is null ? "system" : "simulated");
        report.Add("simulatedScalePercent", scale.SimulatedPercent);
        report.Add("systemDpiScale", scale.SystemDpiScale);
        report.Add("detail", samples.Phases is not null);
        report.Add("build", BuildDescription());
        report.Add("machine", MachineDescription());
        report.Add("startupFirstFrameMs", recorder.FirstFrameMs ?? double.NaN);
        report.Add("startupInteractiveMs", recorder.InteractiveMs ?? double.NaN);
        report.Add("sectionSeconds", elapsed.TotalSeconds);
        report.Add("steps", steps);
        report.Add("unpaintedSteps", unpainted);
        report.Add("frames", samples.BuildMs.Length);
        report.AddDistribution("buildMs", samples.BuildMs);
        report.AddDistribution("inputToFrameMs", samples.InputToFrameMs);
        report.AddDistribution("allocatedKbPerFrame", samples.AllocatedKb);
        report.Add("allocatedMbTotal", samples.AllocatedKb.Sum() / 1024);
        report.Add("workingSetMb", process.WorkingSet64 / 1048576.0);
        report.Add("privateMb", process.PrivateMemorySize64 / 1048576.0);
        report.Add("managedHeapMb", GC.GetTotalMemory(forceFullCollection: false) / 1048576.0);
        report.Add("gcCollections", $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        if (samples.Phases is { } phases)
        {
            report.AddDistribution("dispatchMs", phases.DispatchMs);
            report.AddDistribution("drainMs", phases.DrainMs);
            report.AddDistribution("measureMs", phases.MeasureMs);
            report.AddDistribution("arrangeMs", phases.ArrangeMs);
            report.AddDistribution("renderListMs", phases.RenderListMs);
            report.AddDistribution("renderPresentMs", phases.RenderPresentMs);
            report.AddDistribution("inputToPresentMs", phases.InputToPresentMs);
        }
        return report;
    }

    public void Write(string? path)
    {
        if (path is null) return;
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (string key in _order)
        {
            switch (_values[key])
            {
                case string text: writer.WriteString(key, text); break;
                case int number: writer.WriteNumber(key, number); break;
                case bool flag: writer.WriteBoolean(key, flag); break;
                case double number when double.IsFinite(number): writer.WriteNumber(key, Math.Round(number, 3)); break;
                case double or null: writer.WriteNull(key); break;
            }
        }
        writer.WriteEndObject();
    }

    public string Summary()
    {
        var text = new StringBuilder();
        foreach (string key in _order)
            text.Append(CultureInfo.InvariantCulture, $"{key}: {Format(_values[key])}\n");
        return text.ToString();
    }

    private void Add(string key, object? value)
    {
        _order.Add(key);
        _values[key] = value;
    }

    private void AddDistribution(string name, double[] values)
    {
        Add(name + "P50", FrameSamples.Percentile(values, 50));
        Add(name + "P95", FrameSamples.Percentile(values, 95));
        Add(name + "P99", FrameSamples.Percentile(values, 99));
        Add(name + "Max", values.Length == 0 ? double.NaN : values.Max());
    }

    private static string Format(object? value) => value switch
    {
        double number when double.IsFinite(number) => number.ToString("0.###", CultureInfo.InvariantCulture),
        double or null => "n/a",
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty location is the signal used: the app runs as a native image.")]
    private static string BuildDescription()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        // The project's AOT settings turn off dynamic code under the JIT as well; a native image has no assembly file.
        string runtime = string.IsNullOrEmpty(typeof(MeasurementReport).Assembly.Location) ? "NativeAOT" : "JIT";
        return $"{configuration}, {runtime}, .NET {Environment.Version}, {RuntimeInformation.ProcessArchitecture}";
    }

    private static string MachineDescription()
    {
        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown CPU";
        return $"{cpu}, {Environment.ProcessorCount} logical processors, {RuntimeInformation.OSDescription}";
    }
}

/// <summary>The scale the main window rendered at, Windows' own scale for it, and the simulated percent (--scale) if any.</summary>
internal readonly record struct MeasurementScale(double DpiScale, double SystemDpiScale, int? SimulatedPercent);
