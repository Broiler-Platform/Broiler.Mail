// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   10
// Annotated:        0/10
// Exempt:           4
// Human-reviewed:   0/10
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       10
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Services;

/// <summary>Local profile maintenance only; never changes server mail or exports OS credentials.</summary>
public interface IProfileStorage
{
    string ProfileDirectory { get; }
    ProfileStoragePolicy Policy { get; }
    Task<ProfileStoragePolicy> SetPolicyAsync(ProfileStoragePolicy policy, CancellationToken cancellationToken = default);
    Task<ProfileStorageInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default);
    Task<string> RestoreBackupAsync(string backupDirectory, string newProfileDirectory, CancellationToken cancellationToken = default);
    /// <summary>Back up, then clear local downloaded mail. The caller must quiesce profile receive jobs;
    /// the Windows shell coordinates this with the inbox and draft journal before invoking the service.</summary>
    Task<ProfileCleanupResult> ClearDownloadedMailAsync(string recoveryDirectory, CancellationToken cancellationToken = default);
}

public sealed record ProfileStorageInfo(long DatabaseBytes, long LimitBytes, long HeaderCount, long DownloadedBodyCount, long BackupBytes);
public sealed record ProfileCleanupResult(string BackupDirectory, bool Compacted);
// Zero preserves save-on-open for older manifests that do not contain this field.
public enum OfflineDownloadPolicy { OnDemand, HeadersOnly }
public sealed record ProfileStoragePolicy(long MaximumBytes, OfflineDownloadPolicy Downloads);
