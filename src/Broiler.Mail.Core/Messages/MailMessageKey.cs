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
// Security risk:    High
// Criteria:         1/1
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

/// <summary>IMAP identity, scoped to an account, mailbox, and UIDVALIDITY epoch.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=High; Resources=1; Fingerprint=4852D3
// Broiler-Falsified-If: two keys that differ only in AccountId or UidValidity compare equal, so a body fetched for another account or UIDVALIDITY epoch is accepted for the selected message
// Broiler-Human:        PENDING
public readonly record struct MailMessageKey(
    AccountId AccountId, string MailboxId, uint UidValidity, uint Uid);
