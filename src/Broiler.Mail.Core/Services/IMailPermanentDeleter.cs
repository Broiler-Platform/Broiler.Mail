// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
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

public enum MailDeleteStatus { Confirmed, NotApplied, Unknown }
public sealed record MailDeleteResult(MailMessageKey Key, MailDeleteStatus Status, string Explanation);

/// <summary>Call only for an explicitly confirmed single-message permanent deletion. Never replay Unknown automatically.</summary>
public interface IMailPermanentDeleter
{
    Task<MailDeleteResult> PermanentlyDeleteAsync(AccountProfile account, MailMessageKey key, CancellationToken cancellationToken = default);
}

/// <summary>Remove only the confirmed deleted identity from the active cache, preserving mailbox timestamps and all other rows.</summary>
public interface IMailDeleteCache
{
    Task ReconcilePermanentDeleteAsync(MailMessageKey key, CancellationToken cancellationToken = default);
}
