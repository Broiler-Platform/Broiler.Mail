// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  7/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Version 1 read-only IMAP operations. Implementations must not mark messages read.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=5A7A72
// Broiler-Falsified-If: an implementation sets the Seen flag on a message while listing the inbox or reading its body
// Broiler-Human:        PENDING
public interface IMailReceiver
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=D97FB4
    // Broiler-Falsified-If: a connection test authenticates over a plaintext connection when the server offers neither implicit TLS nor STARTTLS
    // Broiler-Human:        PENDING
    Task TestConnectionAsync(AccountProfile account, CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=8A157C
    // Broiler-Falsified-If: a continuation cursor issued for one account yields messages when passed with another account's profile
    // Broiler-Human:        PENDING
    Task<MailInboxPage> GetInboxAsync(
        AccountProfile account, int maximumCount, MailInboxCursor? older = null, CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=D9E021
    // Broiler-Falsified-If: a body is returned for a key whose UIDVALIDITY no longer matches the mailbox's current UIDVALIDITY
    // Broiler-Human:        PENDING
    Task<MailMessageBody> GetBodyAsync(
        AccountProfile account, MailMessageKey message, CancellationToken cancellationToken = default);
}
