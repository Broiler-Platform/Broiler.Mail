// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           3
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  1/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.Persistence;

/// <summary>Explicit demo/headless storage; the production Windows composition uses JsonDraftStore.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=827863
// Broiler-Falsified-If: two SaveAsync calls made with the same expected revision both succeed, so the second replaces the first draft without a DraftConflictException
// Broiler-Human:        PENDING
public sealed class MemoryDraftStore : IDraftStore
{
    private readonly object _gate = new();
    private DraftStoreState _state = new(0, null);
    public bool IsPersistent => false;
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=759050
    // Broiler-Falsified-If: LoadAsync returns a revision number paired with a draft that belongs to a different revision while a SaveAsync runs concurrently
    // Broiler-Human:        PENDING
    public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_state);
    }
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=D29B73
    // Broiler-Falsified-If: two SaveAsync calls made with the same expected revision both succeed, so the second replaces the first draft without a DraftConflictException
    // Broiler-Human:        PENDING
    public Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state.Revision != expectedRevision) throw new DraftConflictException();
            return Task.FromResult(_state = new(_state.Revision + 1, draft));
        }
    }
}
