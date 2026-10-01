// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=17AB36
// Broiler-Human:        PENDING
public sealed record MailInboxPage(IReadOnlyList<MailMessageSummary> Messages, MailInboxCursor? Older);

/// <summary>A session-only continuation. Mailbox membership changes require a fresh first page.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=Medium; Resources=0; Fingerprint=076EE8
// Broiler-Falsified-If: on a server that reports no UIDNEXT, an expunge plus an arrival between two pages keeps the cursor matching and the next page repeats or skips a message instead of requiring a fresh first page
// Broiler-Human:        PENDING
public sealed record MailInboxCursor(AccountId AccountId, uint UidValidity, uint? UidNext, int MessageCount, int NextIndex);
