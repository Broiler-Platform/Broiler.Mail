// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Atomic operations over the active draft and a bounded library. Never sends or retries mail.</summary>
public interface IDraftLibraryStore : IDraftStore
{
    Task<DraftStoreState> ParkAsync(long expectedRevision, CancellationToken token = default);
    Task<DraftStoreState> ActivateAsync(long expectedRevision, Guid id, CancellationToken token = default);
    Task<DraftStoreState> RemoveSavedAsync(long expectedRevision, Guid id, CancellationToken token = default);
}
