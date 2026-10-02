using System.Globalization;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Windows.Measurement;

namespace Broiler.Mail.Windows;

internal enum DemoScenario { Inbox, Empty, LongMessage, LargeInbox, LargeDraft, ReceiveError, SaveError, SendUnknown, HtmlOnly, BodyError }

/// <summary>Interactive: plain <c>--demo</c>, where the user drives the synthetic inbox. Otherwise the named fixture is prepared on start.</summary>
internal sealed record DemoOptions(DemoScenario Scenario, AppTheme Theme = AppTheme.System, int Width = 1100, int Height = 720, bool Interactive = false,
    MeasureWorkload? Measure = null, string? Report = null)
{
    internal static IReadOnlyList<(string Name, DemoScenario Scenario, string Description)> Gallery { get; } = Array.AsReadOnly(new[]
    {
        ("inbox", DemoScenario.Inbox, "Newest page of 55 messages; welcome message selected"),
        ("empty", DemoScenario.Empty, "Successfully loaded empty inbox"),
        ("long-message", DemoScenario.LongMessage, "Long subject/address and Unicode reading content"),
        ("large-inbox", DemoScenario.LargeInbox, "500 loaded messages (session limit) with the newest selected"),
        ("large-draft", DemoScenario.LargeDraft, "Recovered large draft with Cc and Bcc"),
        ("receive-error", DemoScenario.ReceiveError, "Failed refresh over a loaded inbox and open message"),
        ("save-error", DemoScenario.SaveError, "Failed settings save with inline error feedback"),
        ("send-unknown", DemoScenario.SendUnknown, "Recovered uncertain-send state; sending disabled"),
        ("html-only", DemoScenario.HtmlOnly, "Selected HTML-only message with embedded image and text fallback"),
        ("body-error", DemoScenario.BodyError, "Selected message whose body fails to load, with Retry beside it"),
    });

    internal const string Usage = "--demo [<scenario>] [--theme light|dark|system] [--size <width>x<height>] [--measure <workload> [--report <file.json>]]";

    internal static IReadOnlyList<(string Name, MeasureWorkload Workload, string Description)> Workloads { get; } = Array.AsReadOnly(new[]
    {
        ("idle", MeasureWorkload.Idle, "Three seconds without input; frames drawn should be zero"),
        ("scroll", MeasureWorkload.Scroll, "150 wheel notches down and up over the message list"),
        ("select", MeasureWorkload.Select, "Down arrow through 40 messages, loading each body"),
        ("type", MeasureWorkload.Type, "600 characters typed into the composer body, with autosave"),
        ("theme", MeasureWorkload.Theme, "20 live switches between the dark and light themes"),
        ("resize", MeasureWorkload.Resize, "30 window size changes"),
        ("splitter", MeasureWorkload.Splitter, "60 keyboard moves of the inbox splitter"),
    });

    internal string Name => Gallery.Single(item => item.Scenario == Scenario).Name;
    internal string InitialTab => Scenario is DemoScenario.LargeDraft or DemoScenario.SendUnknown ? "compose"
        : Scenario == DemoScenario.SaveError ? "settings" : "inbox";

    internal static bool TryParse(string[] args, out DemoOptions? options)
    {
        options = null;
        if (args.Length == 0 || args[0] != "--demo") return false;
        int index = 1;
        var scenario = DemoScenario.Inbox;
        bool interactive = true;
        if (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            string name = args[index++];
            var match = Gallery.FirstOrDefault(item => item.Name == name);
            if (match.Name is null) return false;
            scenario = match.Scenario;
            interactive = false;
        }
        var theme = AppTheme.System;
        int width = 1100, height = 720;
        MeasureWorkload? measure = null;
        string? report = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (index < args.Length)
        {
            string flag = args[index++];
            if (!seen.Add(flag) || index >= args.Length) return false;
            string value = args[index++];
            if (flag == "--theme")
            {
                theme = value switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, "system" => AppTheme.System, _ => (AppTheme)(-1) };
                if (!Enum.IsDefined(theme)) return false;
            }
            else if (flag == "--size")
            {
                string[] size = value.Split('x');
                if (size.Length != 2 || !int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
                    || !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
                    || width is < 640 or > 7680 || height is < 480 or > 4320) return false;
            }
            else if (flag == "--measure")
            {
                var match = Workloads.FirstOrDefault(item => item.Name == value);
                if (match.Name is null) return false;
                measure = match.Workload;
            }
            else if (flag == "--report" && value.Length > 0) report = Path.GetFullPath(value);
            else return false;
        }
        // A measurement runs on a prepared fixture, and a report without a measurement is meaningless.
        if ((measure is not null && interactive) || (report is not null && measure is null)) return false;
        options = new(scenario, theme, width, height, interactive, measure, report);
        return true;
    }
}
