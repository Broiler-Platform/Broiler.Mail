// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           1
// Human-reviewed:   0/4
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  3/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

// Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D7A595
// Hairs-Falsified-If: two app instances that save against the same expected revision both succeed, so the later write overwrites the newer draft
// Hairs-Human:        PENDING
public interface IDraftStore
{
    bool IsPersistent { get; }
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D25BF2
    // Hairs-Falsified-If: a saved draft file whose revision is negative is returned as a valid state instead of reported invalid
    // Hairs-Human:        PENDING
    Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default);
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=FDDDAB
    // Hairs-Falsified-If: a save whose expectedRevision differs from the stored revision replaces the stored draft instead of raising DraftConflictException
    // Hairs-Human:        PENDING
    Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default);
}

// Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=6BB99E
// Hairs-Human:        PENDING
public sealed class DraftConflictException() : InvalidOperationException("Another app instance changed the saved draft. Copy your edits before restarting; the newer saved draft was not overwritten.");
