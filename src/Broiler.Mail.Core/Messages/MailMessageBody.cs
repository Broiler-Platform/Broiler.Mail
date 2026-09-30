// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           6
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Medium
// Criteria:         0/0
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

/// <summary>Decoded body data. HtmlText remains untrusted and must not be rendered directly.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=4B8D46
// Broiler-Human:        PENDING
public sealed record MailMessageBody(MailMessageKey Key, string PlainText, string? HtmlText = null)
{
    public bool IsHtmlFallback { get; init; }
    public bool IsTruncated { get; init; }
    public string? HtmlUnavailableReason { get; init; }
    public MailCompositionSource? Composition { get; init; }
    public string? CompositionUnavailableReason { get; init; }
    public IReadOnlyDictionary<string, MailEmbeddedImage> EmbeddedImages { get; init; } =
        new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase);
}
