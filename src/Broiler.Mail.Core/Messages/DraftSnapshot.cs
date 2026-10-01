// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           18
// Human-reviewed:   0/4
// IP risk:          None
// Security risk:    Medium
// Criteria:         2/0
// Resource impact:  3/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Messages;

// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=None; Security=Low; Resources=0; Fingerprint=E3B6CD
// Broiler-Human:        PENDING
public enum DraftSubmissionState { Editing, Sending, Accepted, Failed, Unknown }
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=DBCEC3
// Broiler-Human:        PENDING
public enum SentCopyState { NotRequested, ProviderManaged, Pending, Saved, Failed, Unknown }

/// <summary>Raw recipient edits may be incomplete or invalid. Saving never requires send validation.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=3; Fingerprint=F2955D
// Broiler-Falsified-If: a stored draft without a State property loads as Editing instead of being refused, so Send is enabled again for a message the server may already have accepted
// Broiler-Human:        PENDING
public sealed record DraftSnapshot
{
    public required MailDraft Draft { get; init; }
    public required string ToText { get; init; }
    public required string CcText { get; init; }
    public required string BccText { get; init; }
    [JsonRequired] public DraftSubmissionState State { get; init; }
    public SentCopyState SentCopy { get; init; }
    public string? SentCopyFolder { get; init; }
}

/// <summary>A null draft is a revisioned tombstone, preventing stale writers from recreating a discarded draft.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=3; Fingerprint=5EF0C8
// Broiler-Falsified-If: a stored state without Revision loads as revision 0 instead of being refused, letting a stale writer recreate a discarded draft
// Broiler-Human:        PENDING
public sealed record DraftStoreState([property: JsonRequired] long Revision, [property: JsonRequired] DraftSnapshot? Draft);
