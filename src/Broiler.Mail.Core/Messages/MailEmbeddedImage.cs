// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          None
// Security risk:    High
// Criteria:         4/4
// Resource impact:  1/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

/// <summary>Bounded inline image parsed from a MIME multipart/related message.</summary>
// Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=FE137F
// Hairs-Falsified-If: the embedded images kept for one message add up to more than 2 MiB of decoded data
// Hairs-Human:        PENDING
public sealed record MailEmbeddedImage(string ContentId, string ContentType, byte[] Data)
{
    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=0; Fingerprint=025FBE
    // Hairs-Falsified-If: an inline image part that decodes to more than 1 MiB is kept among the message's embedded images
    // Hairs-Human:        PENDING
    public const int MaximumImageBytes = 1_048_576;
    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=0; Fingerprint=C03557
    // Hairs-Falsified-If: a message whose inline images decode to more than 2 MiB in total keeps every one of them
    // Hairs-Human:        PENDING
    public const int MaximumTotalBytes = 2_097_152;
    // Hairs-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=0; Fingerprint=872EE4
    // Hairs-Falsified-If: a message with 17 distinct cid: image parts yields 17 embedded images
    // Hairs-Human:        PENDING
    public const int MaximumImageCount = 16;
}
