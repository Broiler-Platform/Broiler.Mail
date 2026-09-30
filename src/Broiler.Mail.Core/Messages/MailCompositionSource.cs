// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           8
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

/// <summary>Parsed source headers for composition, independent of the abbreviated inbox display.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=F1E5E2
// Broiler-Falsified-If: a source built without a Reply-To or References header leaves that list null, so reply composition throws instead of falling back to From
// Broiler-Human:        PENDING
public sealed record MailCompositionSource
{
    public string Subject { get; init; } = string.Empty;
    public IReadOnlyList<string> From { get; init; } = [];
    public IReadOnlyList<string> ReplyTo { get; init; } = [];
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public string? MessageId { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    public IReadOnlyList<string> InReplyTo { get; init; } = [];
}
