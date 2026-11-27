// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   6
// Annotated:        0/6
// Exempt:           0
// Human-reviewed:   0/6
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Reads the saved "has attachments" marks of messages in one mailbox epoch without loading their bodies.</summary>
public interface IMailAttachmentStateCache
{
    /// <summary>The largest number of UIDs one request may name.</summary>
    public const int MaximumRequestedUids = 501;

    /// <summary>Returns the subset of <paramref name="uids"/> whose saved row is marked as having attachments.
    /// Returns an empty set for an empty request or when the cache format cannot record the mark yet.
    /// Rejects more than <see cref="MaximumRequestedUids"/> UIDs and UID 0.</summary>
    Task<IReadOnlyCollection<uint>> ReadUidsWithAttachmentsAsync(MailboxKey mailbox, IReadOnlyCollection<uint> uids,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the recorded mark of each of <paramref name="uids"/> that has one: true when the saved row is
    /// marked as having attachments, false when a decoded body established that it has none. UIDs whose mark was never
    /// recorded are absent. Same limits and empty results as <see cref="ReadUidsWithAttachmentsAsync"/>.</summary>
    Task<IReadOnlyDictionary<uint, bool>> ReadAttachmentMarksAsync(MailboxKey mailbox, IReadOnlyCollection<uint> uids,
        CancellationToken cancellationToken = default);

    /// <summary>Returns, in one read, the saved state of each of <paramref name="uids"/> that has a saved row in this
    /// mailbox epoch: its recorded attachment mark (null when never recorded, or when the cache format cannot record it
    /// yet) and whether its body is saved for offline reading. UIDs without a saved row are absent. Rejects more than
    /// <see cref="MaximumRequestedUids"/> UIDs and UID 0, and returns an empty result for an empty request.</summary>
    Task<IReadOnlyDictionary<uint, MailSavedRowState>> ReadSavedRowStatesAsync(MailboxKey mailbox, IReadOnlyCollection<uint> uids,
        CancellationToken cancellationToken = default);
}

/// <summary>The saved state of one message row that received headers do not carry.</summary>
/// <param name="HasAttachments">The recorded attachment mark, or null when none was recorded.</param>
/// <param name="IsBodyCached">Whether the body is saved for offline reading.</param>
public readonly record struct MailSavedRowState(bool? HasAttachments, bool IsBodyCached);
