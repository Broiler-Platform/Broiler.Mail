// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   14
// Annotated:        0/14
// Exempt:           1
// Human-reviewed:   0/14
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       14
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;

namespace Broiler.Mail.Windows.Preview;

/// <summary>
/// The HTML preview's zoom levels: browser-like steps from 50 to 300 %, plus the default (the system
/// text size) when it is not one of them, so stepping in or out always passes through the default.
/// </summary>
internal static class PreviewZoom
{
    public const double Minimum = 0.5;
    // Above the largest system text size (225 %), so a reader at that size can still zoom in.
    public const double Maximum = 3.0;

    private static readonly double[] Steps = [0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];
    // Levels closer than this are one level; it also absorbs rounding in a stored zoom.
    private const double Tolerance = 0.005;

    /// <summary>A usable zoom: a value that is not a finite number is 1, any other is held within the range.</summary>
    public static double Clamp(double zoom) => double.IsFinite(zoom) ? Math.Clamp(zoom, Minimum, Maximum) : 1;

    /// <summary>The same level, within rounding.</summary>
    public static bool AreSame(double a, double b) => Math.Abs(a - b) < Tolerance;

    /// <summary>Every level, smallest first: the steps and the default.</summary>
    public static IReadOnlyList<double> Levels(double defaultZoom)
    {
        double fallback = Clamp(defaultZoom);
        var levels = new List<double>(Steps.Length + 1);
        levels.AddRange(Steps);
        if (!levels.Any(level => AreSame(level, fallback))) levels.Add(fallback);
        levels.Sort();
        return levels;
    }

    /// <summary>
    /// The next level above <paramref name="current"/> (a positive <paramref name="direction"/>) or below
    /// it; <paramref name="current"/> itself, held within the range, when there is none.
    /// </summary>
    public static double Next(double current, int direction, double defaultZoom)
    {
        double from = Clamp(current);
        var levels = Levels(defaultZoom);
        if (direction > 0)
        {
            foreach (double level in levels)
                if (level > from + Tolerance) return level;
        }
        else if (direction < 0)
        {
            for (int index = levels.Count - 1; index >= 0; index--)
                if (levels[index] < from - Tolerance) return levels[index];
        }
        return from;
    }

    public static bool IsAtMinimum(double zoom) => zoom <= Minimum + Tolerance;

    public static bool IsAtMaximum(double zoom) => zoom >= Maximum - Tolerance;

    /// <summary>The zoom as a whole percentage, as the reset button shows it: "150 %".</summary>
    public static string Format(double zoom) =>
        ((int)Math.Round(zoom * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + " %";
}

/// <summary>
/// Turns Ctrl+wheel notches into whole zoom steps. A precision touchpad reports fractions of a notch;
/// they add up until they make one, and turning the other way starts again from nothing.
/// </summary>
internal sealed class PreviewZoomWheel
{
    private double _pending;

    /// <summary>Adds a wheel movement and returns the whole steps it completes, positive to zoom in.</summary>
    public int Add(double notches)
    {
        if (!double.IsFinite(notches) || notches == 0) return 0;
        if (Math.Sign(notches) != Math.Sign(_pending)) _pending = 0;
        _pending += notches;
        // A little slack, so ten tenths of a notch make one step despite floating-point rounding.
        int steps = (int)Math.Truncate(_pending + (Math.Sign(_pending) * 1e-9));
        _pending -= steps;
        return steps;
    }
}
