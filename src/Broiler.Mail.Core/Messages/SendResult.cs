// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           3
// Human-reviewed:   0/2
// IP risk:          None
// Security risk:    Low
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

/// <summary>SMTP acceptance is not a guarantee of delivery to the recipient.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=E3037B
// Broiler-Human:        PENDING
public sealed record SendResult(SubmissionStatus Status, string? StatusMessage = null);

/// <summary>Unknown requires user review; it must never trigger an automatic resend.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=None; Security=Low; Resources=0; Fingerprint=F9D9D0
// Broiler-Falsified-If: the zero value is Accepted or Rejected, so an unset submission status reads as a definite outcome instead of Unknown
// Broiler-Human:        PENDING
public enum SubmissionStatus { Unknown, Accepted, Rejected }
