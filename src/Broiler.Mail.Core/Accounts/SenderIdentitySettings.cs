// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   1
// Annotated:        0/1
// Exempt:           4
// Human-reviewed:   0/1
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

/// <summary>Optional presentation defaults for an already configured sender. Null inherits; empty suppresses.</summary>
public sealed record SenderIdentitySettings
{
    public required string Address { get; init; }
    public string? DisplayName { get; init; }
    public string? Signature { get; init; }
    public string? ReplySignature { get; init; }
}
