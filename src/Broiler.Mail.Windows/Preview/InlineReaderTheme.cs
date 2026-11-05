using Broiler.Graphics.Color;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Recolors only the host-generated stylesheet and blocked-image placeholder.</summary>
internal static class InlineReaderTheme
{
    internal static string Apply(string restrictedHtml, StandardThemeTokens theme)
    {
        const string opening = "<style>";
        int start = restrictedHtml.IndexOf(opening, StringComparison.Ordinal);
        int end = start < 0 ? -1 : restrictedHtml.IndexOf("</style>", start, StringComparison.Ordinal);
        if (end < 0) return restrictedHtml;
        start += opening.Length;
        string css = restrictedHtml[start..end]
            .Replace("color:#202124;background:white", $"color:{Hex(theme.Text)};background:{Hex(theme.Surface)}", StringComparison.Ordinal)
            .Replace("solid #ccc", "solid " + Hex(theme.IsHighContrast ? theme.BorderStrong : theme.Border), StringComparison.Ordinal)
            .Replace("solid #aaa", "solid " + Hex(theme.BorderStrong), StringComparison.Ordinal)
            .Replace("a{color:#185abc}", $"a{{color:{Hex(theme.AccentText)};text-decoration:underline}}", StringComparison.Ordinal);
        string html = restrictedHtml[..start] + css + restrictedHtml[end..];
        return html.Replace("<span style=\"color:#5f6368;font-size:0.9em;border:1px dashed #ccc;",
            $"<span style=\"color:{Hex(theme.IsHighContrast ? theme.Text : theme.TextMuted)};font-size:0.9em;border:1px dashed {Hex(theme.BorderStrong)};", StringComparison.Ordinal);
    }

    private static string Hex(BColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
