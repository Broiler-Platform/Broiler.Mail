// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   6
// Annotated:        0/6
// Exempt:           1
// Human-reviewed:   0/6
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Services;

public interface IMailSearchIndex
{
    Task<MailSearchIndexInfo> GetSearchIndexInfoAsync(CancellationToken cancellationToken = default);
    Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default);
    Task DisableSearchIndexAsync(CancellationToken cancellationToken = default);
}
public sealed record MailSearchIndexInfo(bool Ready, long Documents, long Bytes, string Status)
{
    public long RetainedBodyDocuments { get; init; }
}
public sealed class MailSearchIndexUnavailableException(string message) : InvalidOperationException(message);
