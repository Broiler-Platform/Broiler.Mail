// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        0/5
// Exempt:           0
// Human-reviewed:   0/5
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public sealed record MailFlagChangeResult(MailMessageKey Key, bool IsSet, MailFlagChangeStatus Status, string Explanation);

public interface IMailStarStateWriter
{
    Task<MailFlagChangeResult> SetStarStateAsync(AccountProfile account, MailMessageKey key, bool isStarred,
        CancellationToken cancellationToken = default);
}

public interface IMailStarStateCache
{
    Task UpdateStarStateAsync(MailMessageKey key, bool isStarred, CancellationToken cancellationToken = default);
}
