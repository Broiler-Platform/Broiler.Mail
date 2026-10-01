// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           1
// Human-reviewed:   0/2
// IP risk:          None
// Security risk:    Medium
// Criteria:         2/0
// Resource impact:  1/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

/// <summary>Stable local identity; independent of an address or display name.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=F24606
// Broiler-Falsified-If: two accounts created with New share one id, so they share one credential target and one message-key scope
// Broiler-Human:        PENDING
public readonly record struct AccountId(Guid Value)
{
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=F0D8C5
    // Broiler-Falsified-If: two calls to New return equal ids
    // Broiler-Human:        PENDING
    public static AccountId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
