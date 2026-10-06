// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   19
// Annotated:        0/19
// Exempt:           2
// Human-reviewed:   0/19
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       19
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Net;
using System.Text.RegularExpressions;
using Broiler.Graphics.Geometry;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Preview;

/// <summary>
/// A keyboard and UI Automation stand-in for one link in the rendered HTML. It is transparent over the
/// painted link, takes part in Tab order, opens with Enter (or Invoke), and draws a focus ring.
/// </summary>
internal sealed class HtmlLinkTarget : UiButton
{
    public HtmlLinkTarget(string href, string name, BRect documentBounds, double zoom = 1)
    {
        Href = href;
        Text = name;
        DocumentBounds = documentBounds;
        // The document is laid out in CSS pixels and drawn zoomed; the target covers the drawn link.
        PreferredSize = new BSize(documentBounds.Width * zoom, documentBounds.Height * zoom);
    }

    public string Href { get; }

    /// <summary>The link's rectangle in document coordinates (CSS pixels), before the view's zoom and position.</summary>
    public BRect DocumentBounds { get; }

    protected override BSize MeasureCore(BSize availableSize) => PreferredSize;

    protected override bool OnInput(UiInputEvent input)
    {
        if (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down
            && input.NativeKeyCode == 0x0D && !PreviewModifiers.Any(input.KeyModifiers))
        {
            Click(UiButtonActivationReason.Keyboard);
            return true;
        }
        if (input.Kind == UiInputEventKind.PointerButton && input.MouseButton == MouseButton.Left)
        {
            if (input.MouseButtonTransition == MouseButtonTransition.Up && Bounds.Contains(input.Position))
                Click(UiButtonActivationReason.Pointer);
            return true;
        }
        return false;
    }

    protected override void RenderCore(UiRenderContext context)
    {
        // The link itself is part of the painted document; only keyboard focus needs drawing.
        if (Session is { IsFocusVisible: true } session && session.FocusedElement == this)
            StandardControlPaint.DrawFocusRing(context.RenderList, Inflate(Bounds, 3), 3);
    }

    protected override UiSemanticNode GetSemanticNodeCore() =>
        new(UiSemanticRole.Hyperlink, Text, Bounds, CreateSemanticState(), [], Id: SemanticId);

    private static BRect Inflate(BRect rect, double by) => new(rect.X - by, rect.Y - by, rect.Width + (2 * by), rect.Height + (2 * by));
}

/// <summary>Modifier checks that accept the generic, left, or right flag, as the shell's shortcuts do.</summary>
internal static class PreviewModifiers
{
    public static bool Shift(KeyboardModifierState state) =>
        (state & (KeyboardModifierState.Shift | KeyboardModifierState.LeftShift | KeyboardModifierState.RightShift)) != 0;

    public static bool Control(KeyboardModifierState state) =>
        (state & (KeyboardModifierState.Control | KeyboardModifierState.LeftControl | KeyboardModifierState.RightControl)) != 0;

    public static bool Alt(KeyboardModifierState state) =>
        (state & (KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt | KeyboardModifierState.RightAlt)) != 0;

    public static bool ControlOrAlt(KeyboardModifierState state) => Control(state) || Alt(state);

    public static bool Any(KeyboardModifierState state) => Shift(state) || ControlOrAlt(state);
}

/// <summary>The visible text of each link in sanitized preview HTML, in document order, for accessible names.</summary>
internal static partial class HtmlLinkNames
{
    [GeneratedRegex("""<a\b[^>]*?\bhref\s*=\s*(?:"(?<href>[^"]*)"|'(?<href>[^']*)'|(?<href>[^\s>]+))[^>]*>(?<text>.*?)</a\s*>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Anchor();

    [GeneratedRegex("<[^>]*>", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Space();

    public static IReadOnlyList<(string Href, string Text)> From(string html)
    {
        var names = new List<(string, string)>();
        try
        {
            foreach (Match match in Anchor().Matches(html))
            {
                string text = Space().Replace(WebUtility.HtmlDecode(Tag().Replace(match.Groups["text"].Value, " ")), " ").Trim();
                names.Add((WebUtility.HtmlDecode(match.Groups["href"].Value).Trim(), text));
            }
        }
        // Names are a convenience; a pathological document falls back to the link addresses.
        catch (RegexMatchTimeoutException) { names.Clear(); }
        return names;
    }

    /// <summary>The name for <paramref name="href"/>: the next unused text for that address, else the address.</summary>
    public static string NameFor(string href, IReadOnlyList<(string Href, string Text)> names, ISet<int> used)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (used.Contains(index) || !string.Equals(names[index].Href, href, StringComparison.Ordinal)) continue;
            used.Add(index);
            return names[index].Text.Length > 0 ? names[index].Text : href;
        }
        return href;
    }
}
