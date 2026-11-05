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
