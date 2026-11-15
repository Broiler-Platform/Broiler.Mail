// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   1
// Annotated:        0/1
// Exempt:           5
// Human-reviewed:   0/1
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

/// <summary>Exact mailbox identities for navigation roles. Null means server discovery.</summary>
public sealed record AccountFolderOverrides
{
    public string? Sent { get; init; }
    public string? Drafts { get; init; }
    public string? Archive { get; init; }
    public string? Trash { get; init; }
    public string? Junk { get; init; }
}
