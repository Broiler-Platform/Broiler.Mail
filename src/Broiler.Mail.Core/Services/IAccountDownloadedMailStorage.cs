// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        0/5
// Exempt:           1
// Human-reviewed:   0/5
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Services;

/// <summary>Local downloaded-mail maintenance only. Caller must quiesce receive and flush drafts before cleanup.</summary>
public interface IAccountDownloadedMailStorage
{
    string ProfileDirectory { get; }
    Task<AccountDownloadedMailInfo> GetAccountDownloadedMailInfoAsync(AccountId account, CancellationToken token = default);
    Task<AccountMailCleanupResult> ClearAccountDownloadedMailAsync(AccountId account, string recoveryDirectory, Action<string>? backupCreated = null, CancellationToken token = default);
}
public sealed record AccountDownloadedMailInfo(AccountId AccountId, long HeaderCount, long DownloadedBodyCount, long MailboxCount);
public sealed record AccountMailCleanupResult(AccountId AccountId, string BackupDirectory, bool Compacted, long RemovedHeaderCount, long RemovedBodyCount);
