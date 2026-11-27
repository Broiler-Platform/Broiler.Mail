// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

/// <summary>One attachment part listed by the reader decoder, without its payload.
/// PartNumber has the same meaning as ReceivedAttachment.PartNumber: the 1-based index over the parsed MIME parts.
/// FileName is already sanitized; DecodedBytes is the exact transfer-decoded size.</summary>
public sealed record MailAttachmentDescriptor(int PartNumber, string FileName, string MediaType, int DecodedBytes)
{
    public const int MaximumCount = 32;
    public const int MaximumFileNameLength = 120;

    /// <summary>A multipart attachment (for example a forwarded multipart/mixed container), saved only as part of the original.</summary>
    public bool IsContainer => MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase);
}
