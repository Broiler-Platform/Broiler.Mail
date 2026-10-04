// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           0
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Low
// Criteria:         1/0
// Resource impact:  0/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Services;

/// <summary>A safe user-facing connection failure. Must not include remote response text or secrets.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=EB3577
// Broiler-Falsified-If: a MailConnectionException is constructed with text taken from a server response or a saved secret
// Broiler-Human:        PENDING
public sealed class MailConnectionException(string message, MailConnectionFailure failure = MailConnectionFailure.Unspecified) : Exception(message)
{
    /// <summary>What kind of problem this is; <see cref="MailConnectionFailure.Unspecified"/> where the caller does not classify it.</summary>
    public MailConnectionFailure Failure { get; } = failure;
}
