// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           11
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Messages;

/// <summary>A plain-text composition snapshot with a pinned sender and optional reply thread.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=1104C4
// Broiler-Falsified-If: a saved draft whose JSON omits the id deserializes with a fresh Guid, so its Message-ID differs from the one already submitted
// Broiler-Human:        PENDING
public sealed record MailDraft
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    public required AccountId AccountId { get; init; }
    [JsonRequired] public string FromAddress { get; init; } = string.Empty;
    public DateTimeOffset? SubmissionDate { get; init; }
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string PlainText { get; init; } = string.Empty;
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
}
