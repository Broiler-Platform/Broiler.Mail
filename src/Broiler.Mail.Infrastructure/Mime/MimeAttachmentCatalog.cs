// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   9
// Annotated:        0/9
// Exempt:           0
// Human-reviewed:   0/9
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

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
        string sourceHash=Hash(source.Bytes);var result=new List<(ReceivedAttachment,byte[])>();int listed=0;
        for(int index=0;index<parsed.Parts.Count;index++)
        {
            var part=parsed.Parts[index];if(!part.IsAttachment)continue;
            if(listed++==MaximumAttachments)throw new InvalidDataException("At most 32 attachment parts can be extracted from one source.");
            // An attached multipart container counts toward the limit, as the reader lists it, but is never extracted:
            // it is skipped so the leaf attachments beside it stay available.
            if(part.MediaType.StartsWith("multipart/",StringComparison.OrdinalIgnoreCase))continue;
            if(part.MediaType.Length>127 || part.MediaType.Count(c=>c=='/')!=1 || part.MediaType.Any(c=>!char.IsAsciiLetterOrDigit(c) && !"/!#$&^_.+-".Contains(c)))
                throw new InvalidDataException("Invalid attachment media type.");
            if(part.Data.Length>IStreamingMessageSourceExports.MaximumBytes)throw new InvalidDataException("Decoded attachment exceeds its byte limit.");
            string name=SafeFileName(part.FileName??"attachment.bin");
            result.Add((new(source.Key,sourceHash,index+1,name,part.MediaType,part.Data.Length,Hash(part.Data)),part.Data));
        }
        return result.ToArray();
    }
    /// <summary>A file name safe to create on Windows, macOS, Linux and Android: at most 120 UTF-16 units, the characters Windows
    /// forbids and every control or format character replaced with '_', no trailing dots or spaces, and a Windows device
    /// name (CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9, with any extension) prefixed with '_'.</summary>
    public static string SafeFileName(string value)=>SafeFileName(value,prefixReserved:true);
    // Without the device-name prefix: what names stored before that rule existed were checked against.
    internal static string SafeFileName(string value,bool prefixReserved)
    {
        var name=new StringBuilder();
        foreach(var rune in value.EnumerateRunes())
        {
            if(name.Length+rune.Utf16SequenceLength>120)break;
            if(Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune)==UnicodeCategory.Format || rune.IsAscii && "<>:\"/\\|?*".Contains((char)rune.Value))name.Append('_');
            else name.Append(rune.ToString());
        }
        string result=name.ToString().Trim().TrimEnd('.', ' ');
        if(string.IsNullOrWhiteSpace(result) || result is "." or "..")return "attachment.bin";
        if(!prefixReserved || !IsReservedDeviceName(result))return result;
        result="_"+result;
        if(result.Length>120)result=result[..(char.IsLowSurrogate(result[120]) && char.IsHighSurrogate(result[119]) ? 119 : 120)].TrimEnd('.', ' ');
        return result;
    }
    private static readonly string[] ReservedDeviceNames=
        ["CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"];
    // Windows maps the part before the first dot, with trailing spaces and dots dropped, onto a device.
    private static bool IsReservedDeviceName(string name)=>ReservedDeviceNames.Contains(name.Split('.')[0].TrimEnd(' '),StringComparer.OrdinalIgnoreCase);
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
