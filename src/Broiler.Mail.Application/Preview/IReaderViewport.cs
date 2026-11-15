// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           1
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Application.Preview;

/// <summary>UI-thread, message-local viewport. Position is a fraction of the vertical scroll range, not a text offset or exact paragraph mapping.</summary>
public interface IReaderViewport
{
    double ReadingPosition { get; }
    /// <summary>Clamp finite values to 0..1 and apply after the next nonempty layout, including when currently hidden. Reject NaN/infinity.</summary>
    void RestoreReadingPosition(double fraction);
}
