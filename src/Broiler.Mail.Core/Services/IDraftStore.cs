// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
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

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D7A595
// Broiler-Falsified-If: two app instances that save against the same expected revision both succeed, so the later write overwrites the newer draft
// Broiler-Human:        PENDING
public interface IDraftStore
{
    bool IsPersistent { get; }
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=D25BF2
    // Broiler-Falsified-If: a saved draft file whose revision is negative is returned as a valid state instead of reported invalid
    // Broiler-Human:        PENDING
    Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default);
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=FDDDAB
    // Broiler-Falsified-If: a save whose expectedRevision differs from the stored revision replaces the stored draft instead of raising DraftConflictException
    // Broiler-Human:        PENDING
    Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default);
}

// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=6BB99E
// Broiler-Human:        PENDING
public sealed class DraftConflictException() : InvalidOperationException("Another app instance changed the saved draft. Copy your edits before restarting; the newer saved draft was not overwritten.");
