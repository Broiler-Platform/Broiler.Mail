// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        0/1
// Exempt:           0
// Human-reviewed:   0/1
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Messages;

/// <summary>Server mailbox identity and hierarchy metadata; display names are not paths.</summary>
public sealed record MailFolder(string MailboxId, string? HierarchyDelimiter, bool IsSelectable,
    IReadOnlyList<string> Attributes);
