// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   5
// Annotated:        0/5
// Exempt:           3
// Human-reviewed:   0/5
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>
/// Reads the decoded attachments of one exact message for an interactive save or a forward. It prefers a verified
/// saved original (no network) and otherwise downloads the original once, read-only. It never writes into the
/// profile's attachment-exports or source-exports folders and never counts against their quotas.
/// </summary>
public interface IMessageAttachmentReader
{
    /// <summary>Opens the message's attachment inventory. <paramref name="progress"/> reports (transferred, total) octets of a download.</summary>
    Task<IMailAttachmentSource> OpenAsync(AccountProfile account, MailMessageKey key, Action<int, int>? progress = null, CancellationToken token = default);
}

/// <summary>The parsed attachments of one message, held in memory until disposed so several saves need one download.</summary>
public interface IMailAttachmentSource : IDisposable
{
    MailMessageKey Key { get; }
    /// <summary>True when the bytes came from a verified saved original rather than from the server.</summary>
    bool FromSavedOriginal { get; }
    IReadOnlyList<ReceivedAttachment> Attachments { get; }
    /// <summary>Writes the exact decoded octets of <paramref name="attachment"/>, which must be one of <see cref="Attachments"/>.</summary>
    Task CopyToAsync(ReceivedAttachment attachment, Stream destination, CancellationToken token = default);
    /// <summary>The exact decoded octets of <paramref name="attachment"/>, which must be one of <see cref="Attachments"/>.</summary>
    ReadOnlyMemory<byte> Read(ReceivedAttachment attachment);
}
