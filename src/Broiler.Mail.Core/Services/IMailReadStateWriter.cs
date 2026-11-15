// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   6
// Annotated:        0/6
// Exempt:           3
// Human-reviewed:   0/6
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public enum MailFlagChangeStatus { Confirmed, NotApplied, Unknown }
public sealed record ReadStateChangeResult(MailMessageKey Key, bool IsRead, MailFlagChangeStatus Status, string Explanation);

/// <summary>Explicit per-message changes only. Unknown outcomes must never be replayed automatically.</summary>
public interface IMailReadStateWriter
{
    Task<ReadStateChangeResult> SetReadStateAsync(AccountProfile account, MailMessageKey key, bool isRead,
        CancellationToken cancellationToken = default);
}

/// <summary>Update an existing cached row without changing receive timestamps or creating headers.</summary>
public interface IMailReadStateCache
{
    Task UpdateReadStateAsync(MailMessageKey key, bool isRead, CancellationToken cancellationToken = default);
}
