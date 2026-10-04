// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           6
// Human-reviewed:   0/2
// IP risk:          None
// Security risk:    Low
// Criteria:         1/0
// Resource impact:  0/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Settings;

// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=373834
// Broiler-Falsified-If: the default window size falls outside the range Validate(ApplicationSettings) accepts, so a first save of unchanged settings is refused
// Broiler-Human:        PENDING
public sealed record ApplicationSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;
    public InboxDensity InboxDensity { get; init; } = InboxDensity.Comfortable;
    public int WindowWidth { get; init; } = 1100;
    public int WindowHeight { get; init; } = 720;
    public double InboxSplitterFraction { get; init; } = 0.35;
    /// <summary>The last normal (not maximized) window bounds; null in files written before it existed.</summary>
    public WindowPlacement? Window { get; init; }
}

/// <summary>
/// Remembered main-window geometry. The outer rectangle is in physical screen pixels, which is what
/// monitor work areas are compared against; the client size is in device-independent pixels, which
/// is what a new window is created with. Bounds are those of the normal window, also while maximized.
/// </summary>
public sealed record WindowPlacement
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ClientWidth { get; init; }
    public int ClientHeight { get; init; }
    public bool Maximized { get; init; }
}

// Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=6E41ED
// Broiler-Human:        PENDING
public enum AppTheme { System, Light, Dark }

/// <summary>Spacing of the two-line inbox rows, independent of text size.</summary>
public enum InboxDensity { Comfortable, Compact }
