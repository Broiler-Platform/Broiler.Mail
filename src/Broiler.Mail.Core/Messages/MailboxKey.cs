// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   3
// Annotated:        0/3
// Exempt:           3
// Human-reviewed:   0/3
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

/// <summary>A mailbox epoch within one account. MailboxId is a protocol identity, never a file path.</summary>
public sealed record MailboxKey
{
    public MailboxKey(AccountId accountId, string mailboxId, uint uidValidity)
    {
        if (accountId.Value == Guid.Empty) throw new ArgumentException("An account identity is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(mailboxId)) throw new ArgumentException("A mailbox identity is required.", nameof(mailboxId));
        if (uidValidity == 0) throw new ArgumentOutOfRangeException(nameof(uidValidity));
        AccountId = accountId;
        MailboxId = mailboxId;
        UidValidity = uidValidity;
    }

    public AccountId AccountId { get; }
    public string MailboxId { get; }
    public uint UidValidity { get; }

    public bool Contains(MailMessageKey message) => message.AccountId == AccountId &&
        string.Equals(message.MailboxId, MailboxId, StringComparison.Ordinal) &&
        message.UidValidity == UidValidity && message.Uid != 0;
}
