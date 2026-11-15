// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        0/3
// Exempt:           0
// Human-reviewed:   0/3
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Read-only access to explicitly identified server mailboxes.</summary>
public interface IMailFolderReceiver : IMailReceiver
{
    Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken cancellationToken = default);

    Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailboxId, int maximumCount,
        MailInboxCursor? older = null, CancellationToken cancellationToken = default);
}
