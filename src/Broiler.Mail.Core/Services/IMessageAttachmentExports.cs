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

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

public sealed record ReceivedAttachment(MailMessageKey Key,string SourceSha256,int PartNumber,string FileName,string MediaType,int Bytes,string Sha256);
public sealed record SavedAttachment(ReceivedAttachment Attachment,string Path);
/// <summary>Explicit local extraction from a verified original-source export. Never fetches or opens files.</summary>
public interface IMessageAttachmentExports
{
    string AttachmentDirectory { get; }
    Task<IReadOnlyList<ReceivedAttachment>> ListAttachmentsAsync(MessageSourceExport source,CancellationToken token=default);
    Task<SavedAttachment> SaveAttachmentAsync(MessageSourceExport source,ReceivedAttachment attachment,CancellationToken token=default);
}
