using System.Globalization;
using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Windows;

internal enum DemoScenario { Inbox, Empty, LongMessage, LargeInbox, LargeDraft, ReceiveError, SaveError, SendUnknown, HtmlOnly }

internal sealed record DemoOptions(DemoScenario Scenario, AppTheme Theme = AppTheme.Light, int Width = 1100, int Height = 720)
{
    internal static IReadOnlyList<(string Name, DemoScenario Scenario, string Description)> Gallery { get; } = Array.AsReadOnly(new[]
    {
        ("inbox", DemoScenario.Inbox, "55 messages; welcome message selected"),
        ("empty", DemoScenario.Empty, "Successfully loaded empty inbox"),
        ("long-message", DemoScenario.LongMessage, "Long subject/address and Unicode reading content"),
        ("large-inbox", DemoScenario.LargeInbox, "500 loaded messages with the first selected"),
        ("large-draft", DemoScenario.LargeDraft, "Recovered large draft with Cc and Bcc"),
        ("receive-error", DemoScenario.ReceiveError, "Failed receive with deterministic retry behavior"),
        ("save-error", DemoScenario.SaveError, "Failed settings save with inline error feedback"),
        ("send-unknown", DemoScenario.SendUnknown, "Recovered uncertain-send state; sending disabled"),
        ("html-only", DemoScenario.HtmlOnly, "Selected HTML-only message with embedded image and text fallback"),
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
        if (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            var match = Gallery.FirstOrDefault(item => item.Name == args[index++]);
            if (match.Name is null) return false;
            scenario = match.Scenario;
        }
        var theme = AppTheme.Light;
        int width = 1100, height = 720;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (index < args.Length)
        {
            string flag = args[index++];
            if (!seen.Add(flag) || index >= args.Length) return false;
            string value = args[index++];
            if (flag == "--theme")
            {
                if (value is not ("light" or "dark")) return false;
                theme = value == "dark" ? AppTheme.Dark : AppTheme.Light;
            }
            else if (flag == "--size")
            {
                string[] size = value.Split('x');
                if (size.Length != 2 || !int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
                    || !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
                    || width is < 640 or > 7680 || height is < 480 or > 4320) return false;
            }
            else return false;
        }
        options = new(scenario, theme, width, height);
        return true;
    }
}
