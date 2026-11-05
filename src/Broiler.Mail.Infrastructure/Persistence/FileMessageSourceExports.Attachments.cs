using System.Diagnostics;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class FileMessageSourceExports
{
    public string AttachmentDirectory=>Path.Combine(_profile,"attachment-exports");
    public async Task<IReadOnlyList<ReceivedAttachment>> ListAttachmentsAsync(MessageSourceExport source,CancellationToken token=default)
    {
        var original=await ReadVerifiedAsync(source,token).ConfigureAwait(false);
        var rows=await Task.Run(()=>MimeAttachmentCatalog.Read(original),token).ConfigureAwait(false);token.ThrowIfCancellationRequested();return rows;
    }
    public async Task<SavedAttachment> SaveAttachmentAsync(MessageSourceExport source,ReceivedAttachment attachment,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(attachment);
        if(attachment.Key!=source.Key || !string.Equals(attachment.SourceSha256,source.Sha256,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Attachment belongs to another source/message.");
        var original=await ReadVerifiedAsync(source,token).ConfigureAwait(false);
        var parts=await Task.Run(()=>MimeAttachmentCatalog.Parse(original),token).ConfigureAwait(false);
        var selected=parts.SingleOrDefault(row=>row.Info.PartNumber==attachment.PartNumber);
        if(selected.Info is null || selected.Info!=attachment)throw new InvalidDataException("Attachment inventory changed. Read the saved source inventory again.");
        token.ThrowIfCancellationRequested();CheckDirectory(_profile);Directory.CreateDirectory(AttachmentDirectory);CheckDirectory(AttachmentDirectory);
        await using var lease=await AttachmentLockAsync(token).ConfigureAwait(false);
        var files=Directory.EnumerateFiles(AttachmentDirectory).Take(MaximumFiles+1).ToArray();
        if(files.Length>=MaximumFiles || files.Sum(path=>new FileInfo(path).Length)>MaximumDirectoryBytes-selected.Data.Length)
            throw new IOException("Attachment exports reached the 64-file/128 MiB limit. Move/remove exports explicitly; nothing was evicted.");
        string id=Guid.NewGuid().ToString("N"),temporary=Path.Combine(AttachmentDirectory,id+".part"),final=Path.Combine(AttachmentDirectory,"attachment-"+id+"-"+attachment.FileName);
        try
        {
            await using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.Asynchronous))
            {await stream.WriteAsync(selected.Data,token).ConfigureAwait(false);await stream.FlushAsync(token).ConfigureAwait(false);stream.Flush(flushToDisk:true);}
            token.ThrowIfCancellationRequested();CheckDirectory(AttachmentDirectory);File.Move(temporary,final);return new(attachment,final);
        }
        finally{try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private async Task<FileStream> AttachmentLockAsync(CancellationToken token)
    {
        var elapsed=Stopwatch.StartNew();
        while(true)
        {
            token.ThrowIfCancellationRequested();
            try{return new FileStream(Path.Combine(_profile,"attachment-exports.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException)when(elapsed.Elapsed<TimeSpan.FromSeconds(3)){await Task.Delay(50,token).ConfigureAwait(false);}
        }
    }
}
