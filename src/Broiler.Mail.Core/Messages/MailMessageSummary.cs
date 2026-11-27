// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           8
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Medium
// Criteria:         0/0
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=9D9C42
// Hairs-Human:        PENDING
public sealed record MailMessageSummary
{
    public required MailMessageKey Key { get; init; }
    public required string Sender { get; init; }
    public required string Subject { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public bool IsStarred { get; init; }
    public bool IsRead { get; init; }
    public bool IsBodyCached { get; init; }
    /// <summary>True when a decoded or downloaded body (or a later structure hint) recorded at least one regular attachment; false means none or unknown.</summary>
    public bool HasAttachments { get; init; }
}
