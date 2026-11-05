using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class ReceivedAttachmentTests
{
    internal static readonly byte[] Binary=[0,255,128,13,10,42];
    internal static byte[] Original(string encoding="base64",string? payload=null,bool close=true)=>Encoding.ASCII.GetBytes(
        "From: sender@example.test\r\nSubject: Attachments\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=fixture\r\n\r\n"+
        "--fixture\r\nContent-Type: text/plain\r\n\r\nReader body\r\n"+
        "--fixture\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"../../evil:name.bin\"\r\nContent-Transfer-Encoding: "+encoding+"\r\n\r\n"+(payload??Convert.ToBase64String(Binary))+"\r\n"+
        "--fixture\r\nContent-Type: text/plain; name=notes.txt\r\nContent-Disposition: attachment; filename=notes.txt\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\nA=00=FF=0D=0AB\r\n"+
        "--fixture\r\nContent-Type: image/png; name=inline.png\r\nContent-Disposition: inline; filename=inline.png\r\nContent-ID: <inline>\r\nContent-Transfer-Encoding: base64\r\n\r\nAQID\r\n"+(close?"--fixture--\r\n":""));
    [Fact]
    public void InventoryPreservesDecodedOctetsAndExcludesInlineResourcesAndMainBody()
    {
        var key=Key();byte[] bytes=Original();var rows=MimeAttachmentCatalog.Read(new(key,bytes));Assert.Equal(2,rows.Count);Assert.All(rows,row=>Assert.Equal(key,row.Key));
        Assert.Equal(2,rows[0].PartNumber);Assert.Equal(Binary.Length,rows[0].Bytes);Assert.Equal(Convert.ToHexString(SHA256.HashData(Binary)).ToLowerInvariant(),rows[0].Sha256);
        Assert.Equal("notes.txt",rows[1].FileName);Assert.DoesNotContain('/',rows[0].FileName);Assert.DoesNotContain(':',rows[0].FileName);
        var parsed=MimeParser.Parse(bytes);Assert.Equal("Reader body",parsed.TextBody);Assert.Single(parsed.EmbeddedImages.Values.Distinct());Assert.Equal(4,parsed.Parts.Count);
        Assert.Equal(new byte[]{65,0,255,13,10,66},parsed.Parts[2].Data);
    }
    [Theory]
    [InlineData("base64","!not base64!")]
    [InlineData("quoted-printable","bad=QZ")]
    [InlineData("quoted-printable","bad=")]
    [InlineData("x-unsupported","payload")]
    public void InvalidAttachmentTransferEncodingCannotProduceSaveableBytes(string encoding,string payload)
    {
        Assert.Throws<InvalidDataException>(()=>MimeAttachmentCatalog.Read(new(Key(),Original(encoding,payload))));
        Assert.Equal("Reader body",MimeParser.Parse(Original(encoding,payload)).TextBody);
    }
    [Fact]
    public void BinaryPayloadBoundarySubstringsDoNotTruncateAttachmentAndIncompleteMultipartIsRejected()
    {
        string payload="prefix--fixture\r\n--fixture-not-a-boundary\r\nlast";var source=Original("binary",payload);var rows=MimeAttachmentCatalog.Read(new(Key(),source));
        Assert.Equal(Encoding.ASCII.GetByteCount(payload),rows[0].Bytes);Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(payload))).ToLowerInvariant(),rows[0].Sha256);
        Assert.Throws<InvalidDataException>(()=>MimeAttachmentCatalog.Read(new(Key(),Original(close:false))));
    }
    [Fact]
    public void ExplicitTextHtmlAndImagesAreAttachmentsRatherThanMainReaderContent()
    {
        byte[] raw=Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/html\r\nContent-Disposition: attachment; filename=page.html\r\n\r\n<script>raw</script>\r\n--x\r\nContent-Type: image/png\r\nContent-ID: <picture>\r\nContent-Disposition: attachment; filename=picture.png\r\n\r\nnot-an-image\r\n--x--\r\n");
        var parsed=MimeParser.Parse(raw);Assert.Null(parsed.HtmlBody);Assert.Null(parsed.TextBody);Assert.Empty(parsed.EmbeddedImages);Assert.Equal(2,MimeAttachmentCatalog.Read(new(Key(),raw)).Count);
    }
    [Fact]
    public void InventoryCountAndMimePartLimitsRefuseSilentPartialCoverage()
    {
        string Part(int i)=>$"--x\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename={i}.bin\r\n\r\nx\r\n";
        byte[] raw=Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n"+string.Concat(Enumerable.Range(1,33).Select(Part))+"--x--\r\n");
        Assert.Throws<InvalidDataException>(()=>MimeAttachmentCatalog.Read(new(Key(),raw)));
        byte[] many=Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n"+string.Concat(Enumerable.Range(1,257).Select(i=>"--x\r\nContent-Type: text/plain\r\n\r\nbody\r\n"))+"--x--\r\n");
        Assert.True(MimeParser.Parse(many).HasIncompleteParts);Assert.Throws<InvalidDataException>(()=>MimeAttachmentCatalog.Read(new(Key(),many)));
    }
    [Fact]
    public async Task SaveReverifiesSourceAndPartIdentityUsesUniqueSafeNamesAndRetainsExportsAcrossCleanup()
    {
        using var directory=new TestDirectory();using var profile=MailProfile.Open(directory.File("profile"));var store=new FileMessageSourceExports(profile.DirectoryPath);
        var original=await store.SaveAsync(new(Key(),Original()));var row=(await store.ListAttachmentsAsync(original))[0];
        var one=await store.SaveAttachmentAsync(original,row);var two=await store.SaveAttachmentAsync(original,row);Assert.NotEqual(one.Path,two.Path);Assert.Equal(Binary,File.ReadAllBytes(one.Path));Assert.Equal(store.AttachmentDirectory,Path.GetDirectoryName(one.Path));
        Assert.StartsWith("attachment-",Path.GetFileName(one.Path));Assert.DoesNotContain(':',Path.GetFileName(one.Path));Assert.Empty(Directory.GetFiles(store.AttachmentDirectory,"*.part"));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.SaveAttachmentAsync(original,row with{Key=Key()}));
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAttachmentAsync(original,row with{Sha256=new string('a',64)}));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.SaveAttachmentAsync(original,row,new(true)));
        byte[] changed=File.ReadAllBytes(original.Path);changed[0]^=1;File.WriteAllBytes(original.Path,changed);await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAttachmentAsync(original,row));File.WriteAllBytes(original.Path,Original());
        var result=await new ProfileStorageService(profile,new SqliteMailCache(profile.CachePath)).ClearDownloadedMailAsync(directory.File("backup"));Assert.Equal(Binary,File.ReadAllBytes(one.Path));Assert.False(Directory.Exists(Path.Combine(result.BackupDirectory,"attachment-exports")));
    }
    [Fact]
    public async Task ConcurrentAttachmentSavesRespectQuotaAndDoNotOverwriteOldFiles()
    {
        using var directory=new TestDirectory();directory.Create();var store=new FileMessageSourceExports(directory.Root);var original=await store.SaveAsync(new(Key(),Original()));var part=(await store.ListAttachmentsAsync(original))[0];
        Directory.CreateDirectory(store.AttachmentDirectory);for(int index=0;index<63;index++)File.WriteAllText(Path.Combine(store.AttachmentDirectory,index+".bin"),"kept");
        var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{try{await store.SaveAttachmentAsync(original,part);return true;}catch(IOException){return false;}}));
        Assert.Single(outcomes,value=>value);Assert.Equal(64,Directory.GetFiles(store.AttachmentDirectory).Length);Assert.Equal("kept",File.ReadAllText(Path.Combine(store.AttachmentDirectory,"0.bin")));
    }
    [Theory]
    [InlineData("../CON.txt")]
    [InlineData("C:\\folder\\file?.bin")]
    [InlineData("evil\u202e.exe")]
    public void SuggestedNamesArePortableAndNeverContainPathsOrFormattingControls(string value)
    {
        string safe=MimeAttachmentCatalog.SafeFileName(value);Assert.DoesNotContain('/',safe);Assert.DoesNotContain('\\',safe);Assert.DoesNotContain(':',safe);Assert.DoesNotContain('?',safe);Assert.DoesNotContain('\u202e',safe);
    }
    [Fact]
    public void LegacyFilenameImageResourcesRemainAvailableUnlessExplicitlyAttachments()
    {
        byte[] raw=Encoding.ASCII.GetBytes("Content-Type: image/png; name=image.png\r\nContent-Transfer-Encoding: base64\r\n\r\nAQID");
        Assert.Contains("image.png",MimeParser.Parse(raw).EmbeddedImages.Keys);
    }
    [Fact]
    public void AttachmentParsingBoundsHeaderBytesAndHeaderCount()
    {
        byte[] longHeader=Encoding.ASCII.GetBytes("X: "+new string('a',65536)+"\r\n\r\nbody");Assert.Throws<InvalidDataException>(()=>MimeAttachmentCatalog.Read(new(Key(),longHeader)));
        byte[] many=Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("X: value\r\n",257))+"\r\nbody");Assert.Throws<InvalidDataException>(()=>MimeAttachmentCatalog.Read(new(Key(),many)));
    }
    private static MailMessageKey Key()=>new(AccountId.New(),"INBOX",7,1);
}
