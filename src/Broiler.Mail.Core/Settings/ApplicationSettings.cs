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
    public int WindowWidth { get; init; } = 1100;
    public int WindowHeight { get; init; } = 720;
}

// Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=6E41ED
// Broiler-Human:        PENDING
public enum AppTheme { System, Light, Dark }
