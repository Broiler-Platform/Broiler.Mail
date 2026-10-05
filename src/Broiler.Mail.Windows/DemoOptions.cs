using System.Globalization;
using Broiler.Hosting.Windows;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Windows.Measurement;

namespace Broiler.Mail.Windows;

internal enum DemoScenario { Inbox, Empty, LongMessage, LargeInbox, LargeDraft, ReceiveError, SaveError, SendUnknown, HtmlOnly, BodyError,
    InvalidSetup, TestCanceled, DraftConflict, SendRejected, SentCopyFailed, LongHtml, SmtpTestFailed, SmtpTestPassed, ReceiveCanceled, LoadError,
    DraftInvalid, NewMail }

/// <summary>
/// What the new-mail fixture's server does to the open message on the first receive after the fixture
/// is prepared. Acceptance checks use it to reach each refresh outcome through an ordinary F5.
/// </summary>
internal enum DemoServerChange { None, Vanish, Outside, Renumber }

/// <summary>Interactive: plain <c>--demo</c>, where the user drives the synthetic inbox. Otherwise the named fixture is prepared on start.</summary>
internal sealed record DemoOptions(DemoScenario Scenario, AppTheme Theme = AppTheme.System, int Width = 1100, int Height = 720, bool Interactive = false,
    MeasureWorkload? Measure = null, string? Report = null, int? TextScalePercent = null, bool HighContrast = false,
    DemoServerChange ServerChange = DemoServerChange.None, int? ScalePercent = null, bool Detail = false, string? ContrastTheme = null)
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
        ("body-error", DemoScenario.BodyError, "Selected message whose body fails to load, with Retry below its date"),
        ("invalid-setup", DemoScenario.InvalidSetup, "Account save rejected for an email address with a display name"),
        ("test-canceled", DemoScenario.TestCanceled, "Connection test canceled before the server answered"),
        ("draft-conflict", DemoScenario.DraftConflict, "Autosave refused because another instance changed the saved draft"),
        ("send-rejected", DemoScenario.SendRejected, "Send rejected by the server with its reason; the draft is kept"),
        ("sent-copy-failed", DemoScenario.SentCopyFailed, "Message accepted but its Sent copy was not saved"),
        ("long-html", DemoScenario.LongHtml, "Selected HTML message longer than the preview's render budget, with links; the next message is HTML too"),
        ("smtp-test-failed", DemoScenario.SmtpTestFailed, "SMTP sign-in test rejected by the demo server; no message was sent"),
        ("smtp-test-passed", DemoScenario.SmtpTestPassed, "SMTP sign-in test accepted by the demo server; no message was sent"),
        ("receive-canceled", DemoScenario.ReceiveCanceled, "Receiving canceled over a loaded inbox and open message, with Retry"),
        ("load-error", DemoScenario.LoadError, "Load older failed; the loaded messages stay, with Retry above the list"),
        ("draft-invalid", DemoScenario.DraftInvalid, "Check draft rejected a recipient typed without @; the error stays until the next edit"),
        ("new-mail", DemoScenario.NewMail, "Three new messages arrived above the open one, which keeps its body and is now read; each receive adds more"),
    });

    internal const string Usage = "--demo [<scenario>] [--theme light|dark|system] [--size <width>x<height>] [--text-scale <100-225>] [--contrast high|aquatic|desert|dusk|night-sky] [--measure <workload> [--report <file.json>] [--detail]] [--scale <100-300>] [--server-change vanish|outside|renumber]";

    /// <summary>
    /// The Windows 11 contrast themes --contrast can simulate, by the name the option takes. Their palette is
    /// built as for the system's own contrast colors; --contrast high keeps the theme's preset instead.
    /// </summary>
    internal static IReadOnlyList<(string Name, WindowsSystemColors Colors)> ContrastThemes { get; } = Array.AsReadOnly(new[]
    {
        ("aquatic", WindowsSystemColors.Aquatic),
        ("desert", WindowsSystemColors.Desert),
        ("dusk", WindowsSystemColors.Dusk),
        ("night-sky", WindowsSystemColors.NightSky),
    });

    /// <summary>The colors of the simulated contrast theme, or null for none or for the preset (--contrast high).</summary>
    internal WindowsSystemColors? ContrastColors =>
        ContrastThemes.Where(theme => theme.Name == ContrastTheme).Select(theme => (WindowsSystemColors?)theme.Colors).FirstOrDefault();

    /// <summary>Options for acceptance scripts only. They simulate conditions; they are not settings a user would choose.</summary>
    internal static IReadOnlyList<(string Name, string Description)> AcceptanceOptions { get; } = Array.AsReadOnly(new[]
    {
        ("--contrast high|aquatic|desert|dusk|night-sky", "Renders as if Windows high contrast were on; the system's settings are unchanged. high uses the theme's own high-contrast preset. aquatic, desert, dusk and night-sky use the palette built from the colors of the Windows 11 contrast theme of that name (Aquatic, Desert, Dusk, Night sky), as it is built from the system's own contrast colors."),
        ("--scale <100-300>", "Renders the main window at a simulated display scale in percent, named in the window title, at the requested size in DIPs even if that is larger than the screen. HTML previews keep Windows' own scale, except the one a long-html or preview-zoom measurement opens; Windows' own scale is unchanged."),
        ("--server-change vanish|outside|renumber", "With new-mail only: the next receive deletes the open message on the server, pushes it below the newest page, or renumbers the inbox."),
    });

    internal static IReadOnlyList<(string Name, MeasureWorkload Workload, string Description)> Workloads { get; } = Array.AsReadOnly(new[]
    {
        ("idle", MeasureWorkload.Idle, "Three seconds without input; frames drawn should be zero"),
        ("scroll", MeasureWorkload.Scroll, "150 wheel notches down and up over the message list"),
        ("select", MeasureWorkload.Select, "Down arrow through 40 messages, loading each body"),
        ("type", MeasureWorkload.Type, "600 characters typed into the composer body, with autosave"),
        ("theme", MeasureWorkload.Theme, "20 live switches between the dark and light themes"),
        ("resize", MeasureWorkload.Resize, "30 window size changes"),
        ("splitter", MeasureWorkload.Splitter, "60 keyboard moves of the inbox splitter"),
        ("long-html", MeasureWorkload.LongHtml, "On long-html: opens the HTML preview (900x700 DIPs) and scrolls it to the end and back, three wheel notches per input"),
        ("preview-zoom", MeasureWorkload.PreviewZoom, "On long-html: opens the HTML preview and zooms it in to 300 %, out to 50 %, and back, one level per input"),
    });

    /// <summary>Workloads that measure the HTML preview window; they need the long-html fixture's document.</summary>
    internal static bool MeasuresPreview(MeasureWorkload workload) => workload is MeasureWorkload.LongHtml or MeasureWorkload.PreviewZoom;

    internal string Name => Gallery.Single(item => item.Scenario == Scenario).Name;

    /// <summary>Names the fixture, and a simulated scale, so a capture cannot be mistaken for real mail or a real display.</summary>
    internal string WindowTitle
    {
        get
        {
            string fixture = Interactive ? "Demo" : $"Demo: {Name}";
            string scale = ScalePercent is { } percent ? $", simulated {percent.ToString(CultureInfo.InvariantCulture)}% scale" : "";
            return $"Broiler.Mail — {fixture}{scale} (no network or saved data)";
        }
    }
    internal string InitialTab => Scenario switch
    {
        DemoScenario.LargeDraft or DemoScenario.SendUnknown or DemoScenario.DraftConflict or DemoScenario.SendRejected or DemoScenario.SentCopyFailed
            or DemoScenario.DraftInvalid => "compose",
        DemoScenario.InvalidSetup or DemoScenario.TestCanceled or DemoScenario.SmtpTestFailed or DemoScenario.SmtpTestPassed => "account",
        DemoScenario.SaveError => "settings",
        _ => "inbox",
    };

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
        int? textScale = null;
        bool highContrast = false;
        string? contrastTheme = null;
        var serverChange = DemoServerChange.None;
        int? scale = null;
        bool detail = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (index < args.Length)
        {
            string flag = args[index++];
            if (!seen.Add(flag)) return false;
            // Phase and render-and-present timers for a measurement; the only option without a value.
            if (flag == "--detail")
            {
                detail = true;
                continue;
            }
            if (index >= args.Length) return false;
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
            // The system text size in percent, as Windows offers it (100 to 225), without changing the setting.
            else if (flag == "--text-scale")
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent) || percent is < 100 or > 225) return false;
                textScale = percent;
            }
            // High contrast as if a Windows contrast theme were on: the theme's high-contrast preset (high), or the
            // palette a Windows 11 contrast theme's colors give, built as for the system's own contrast colors.
            else if (flag == "--contrast")
            {
                if (value != "high" && !ContrastThemes.Any(theme => theme.Name == value)) return false;
                highContrast = true;
                contrastTheme = value == "high" ? null : value;
            }
            // A render scale in percent for checks on a single monitor; Windows' own scale is unchanged.
            else if (flag == "--scale")
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent) || percent is < 100 or > 300) return false;
                scale = percent;
            }
            else if (flag == "--server-change")
            {
                serverChange = value switch
                {
                    "vanish" => DemoServerChange.Vanish, "outside" => DemoServerChange.Outside, "renumber" => DemoServerChange.Renumber, _ => DemoServerChange.None,
                };
                if (serverChange == DemoServerChange.None) return false;
            }
            else return false;
        }
        // A measurement runs on a prepared fixture, and a report or detail without a measurement is meaningless.
        if ((measure is not null && interactive) || ((report is not null || detail) && measure is null)) return false;
        // The preview workloads measure the long-html fixture's document.
        if (measure is { } workload && MeasuresPreview(workload) && scenario != DemoScenario.LongHtml) return false;
        // Only the new-mail fixture has a server that changes between receives.
        if (serverChange != DemoServerChange.None && scenario != DemoScenario.NewMail) return false;
        options = new(scenario, theme, width, height, interactive, measure, report, textScale, highContrast, serverChange, scale, detail, contrastTheme);
        return true;
    }
}
