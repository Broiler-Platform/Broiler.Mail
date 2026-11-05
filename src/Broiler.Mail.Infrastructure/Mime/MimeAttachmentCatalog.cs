using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime.Encodings;

namespace Broiler.Mail.Infrastructure.Mime;

public static class MimeAttachmentCatalog
{
    public const int MaximumAttachments=32;
    public static IReadOnlyList<ReceivedAttachment> Read(MailMessageSource source)
    { return Array.AsReadOnly(Parse(source).Select(row=>row.Info).ToArray()); }
    internal static (ReceivedAttachment Info,byte[] Data)[] Parse(MailMessageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(source.Bytes);
        if(source.Bytes.Length is <1 or >IStreamingMessageSourceExports.MaximumBytes)throw new ArgumentException("Attachment source must fit the 12 MiB export limit.");
        var parsed=MimeParser.ParseForAttachmentExport(source.Bytes);
        if(parsed.HasIncompleteParts)throw new InvalidDataException("MIME structure is incomplete or exceeds the 32-level/256-part limit; no attachment inventory was accepted.");
        string sourceHash=Hash(source.Bytes);var result=new List<(ReceivedAttachment,byte[])>();
        for(int index=0;index<parsed.Parts.Count;index++)
        {
            var part=parsed.Parts[index];if(!part.IsAttachment)continue;
            if(result.Count==MaximumAttachments)throw new InvalidDataException("At most 32 attachment parts can be extracted from one source.");
            if(part.MediaType.Length>127 || part.MediaType.Count(c=>c=='/')!=1 || part.MediaType.Any(c=>!char.IsAsciiLetterOrDigit(c) && !"/!#$&^_.+-".Contains(c)))
                throw new InvalidDataException("Invalid attachment media type.");
            if(part.Data.Length>IStreamingMessageSourceExports.MaximumBytes)throw new InvalidDataException("Decoded attachment exceeds its byte limit.");
            string name=SafeFileName(part.FileName??"attachment.bin");
            result.Add((new(source.Key,sourceHash,index+1,name,part.MediaType,part.Data.Length,Hash(part.Data)),part.Data));
        }
        return result.ToArray();
    }
    public static string SafeFileName(string value)
    {
        var name=new StringBuilder();
        foreach(var rune in value.EnumerateRunes())
        {
            if(name.Length+rune.Utf16SequenceLength>120)break;
            if(Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune)==UnicodeCategory.Format || rune.IsAscii && "<>:\"/\\|?*".Contains((char)rune.Value))name.Append('_');
            else name.Append(rune.ToString());
        }
        string result=name.ToString().Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(result) || result is "." or ".." ? "attachment.bin" : result;
    }
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
