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
    /// <summary>Pinned for newly created drafts; null preserves legacy account-name behavior.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FromDisplayName { get; init; }
    public DateTimeOffset? SubmissionDate { get; init; }
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string PlainText { get; init; } = string.Empty;
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DraftAttachment>? Attachments { get; init; }
}

public sealed record DraftAttachment(string FileName, ReadOnlyMemory<byte> Content)
{
    public const int MaximumCount = 4;
    public const int MaximumBytes = 8 * 1024 * 1024;
    public const int MaximumProfileBytes = 16 * 1024 * 1024;

    public static void Validate(IReadOnlyList<DraftAttachment>? attachments)
    {
        if (attachments is null) return;
        if (attachments.Count > MaximumCount) throw new ArgumentException("Use at most four attachments per draft.");
        int bytes = 0;
        foreach (var attachment in attachments)
        {
            if (attachment?.FileName is not { Length: > 0 and <= 120 } name ||
                name is "." or ".." || name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' || char.IsSurrogate(c)) ||
                System.Text.Encoding.UTF8.GetByteCount(name) > 240 ||
                attachment.Content.Length == 0 || attachment.Content.Length > MaximumBytes - bytes)
                throw new ArgumentException("Attachments require safe file names and at most 8 MiB total per draft.");
            bytes += attachment.Content.Length;
        }
    }
}
