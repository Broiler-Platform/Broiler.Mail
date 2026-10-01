// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           5
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Medium
// Criteria:         0/0
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=9D9C42
// Broiler-Human:        PENDING
public sealed record MailMessageSummary
{
    public required MailMessageKey Key { get; init; }
    public required string Sender { get; init; }
    public required string Subject { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public bool IsRead { get; init; }
}
