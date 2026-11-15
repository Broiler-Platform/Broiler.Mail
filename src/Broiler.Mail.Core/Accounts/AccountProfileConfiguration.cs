// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

/// <summary>Configuration equality across persistence boundaries, including recreated sender-override collections.</summary>
public static class AccountProfileConfiguration
{
    public static bool Matches(AccountProfile? expected, AccountProfile? actual) => expected == actual ||
        expected is not null && actual is not null &&
        (expected with { SenderIdentities = null }) == (actual with { SenderIdentities = null }) &&
        (expected.SenderIdentities ?? []).SequenceEqual(actual.SenderIdentities ?? []);
}
