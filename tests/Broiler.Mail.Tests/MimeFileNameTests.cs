using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class MimeFileNameTests
{
    [Theory]
    [InlineData("filename*=utf-8'en'r%C3%A9sum%C3%A9.pdf", "résumé.pdf")]
    [InlineData("filename*=iso-8859-1''caf%E9.txt", "café.txt")]
    [InlineData("filename*=us-ascii''a+b%20c.txt", "a+b c.txt")]
    [InlineData("filename*=''plain.txt", "plain.txt")]
    [InlineData("filename=fallback.txt; filename*=utf-8''real%C3%A9.txt", "realé.txt")]
    [InlineData("FILENAME*=UTF-8''%E6%96%87%E4%BB%B6.txt", "文件.txt")]
    public void ExtendedNamesDecodeOctetsAndPreferExplicitUnicodeForm(string parameters, string expected)
        => Assert.Equal(expected, Read(parameters).FileName);

    [Theory]
    [InlineData("filename*1*=%A9.txt; filename*0*=utf-8''caf%C3", "café.txt")]
    [InlineData("filename*0*=utf-8'en'caf%C3%A9; filename*1=.txt", "café.txt")]
    [InlineData("filename*1=report.txt; filename*0=annual-", "annual-report.txt")]
    [InlineData("filename*0=annual; filename*1*=%20report.txt", "annual report.txt")]
    [InlineData("filename=fallback; filename*0*=utf-8''report; filename*1=.txt", "report.txt")]
    public void ContinuationsUseNumericOrderAndDecodeAfterJoiningOctets(string parameters, string expected)
        => Assert.Equal(expected, Read(parameters).FileName);

    [Theory]
    [InlineData("filename=\"a; b.txt\"", "a; b.txt")]
    [InlineData("filename=\"a\\\"b.txt\"", "a_b.txt")]
    [InlineData("filename=\"a\\\\b.txt\"", "a_b.txt")]
    [InlineData("filename=\"=?utf-8?B?Y2Fmw6kudHh0?=\"", "café.txt")]
    [InlineData("filename*=utf-8''%3D%3Futf-8%3FB%3FY2Fmw6kudHh0%3F%3D", "=_utf-8_B_Y2Fmw6kudHh0_=")]
    public void QuotedParametersAndLegacyNamesDecodeExactlyOnce(string parameters, string expected)
        => Assert.Equal(expected, Read(parameters).FileName);

    [Theory]
    [InlineData("filename*=utf-8''bad%Q0")]
    [InlineData("filename*=utf-8''bad% A")]
    [InlineData("filename*=utf-8''bad%")]
    [InlineData("filename*=utf-8''%FF")]
    [InlineData("filename*=unknown-charset''a")]
    [InlineData("filename*=utf-7''a")]
    [InlineData("filename*=utf-8missing-delimiters")]
    [InlineData("filename*=''caf%E9")]
    [InlineData("filename*0=a; filename*2=b")]
    [InlineData("filename*1=a")]
    [InlineData("filename*00=a")]
    [InlineData("filename*32=a")]
    [InlineData("filename*0=a; filename*0*=utf-8''b")]
    [InlineData("filename*=utf-8''a; filename*0=b")]
    [InlineData("filename=a; FILENAME=b")]
    [InlineData("filename*=utf-8''bad%; filename=fallback.txt")]
    [InlineData("filename=\"unfinished")]
    [InlineData("filename=\"a\"junk")]
    public void MalformedOrAmbiguousNamesRejectEntireExtractionButKeepPreview(string parameters)
    {
        byte[] raw = Source(parameters);
        Assert.Throws<InvalidDataException>(() => MimeAttachmentCatalog.Read(new(Key(), raw)));
        Assert.Equal("Body", MimeParser.Parse(raw).TextBody);
    }

    [Fact]
    public void ExtendedContentTypeNameClassifiesNamedTextWithoutReplacingBody()
    {
        byte[] raw = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(Source("", "text/plain; name*=utf-8''notes%C3%A9.txt")).Replace("Content-Disposition: attachment; \r\n", ""));
        Assert.Equal("notesé.txt", Assert.Single(MimeAttachmentCatalog.Read(new(Key(), raw))).FileName);
        Assert.Equal("Body", MimeParser.Parse(raw).TextBody);
    }

    [Theory]
    [InlineData("Content-Type: text/plain; name*=utf-8''bad%")]
    [InlineData("Content-Type: text/plain\r\nContent-Disposition: attachment; filename=\"unfinished")]
    public void MalformedNamedTextNeverBecomesTheMainBody(string headers)
    {
        byte[] raw = Encoding.ASCII.GetBytes(headers + "\r\n\r\nAttached text");
        Assert.Null(MimeParser.Parse(raw).TextBody);
        Assert.Throws<InvalidDataException>(() => MimeAttachmentCatalog.Read(new(Key(), raw)));
    }

    [Fact]
    public async Task DecodedPathsAreSanitizedAndSavedBytesRemainUnchanged()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new FileMessageSourceExports(directory.Root);
        var source = await store.SaveAsync(new(Key(), Source("filename*=utf-8''..%2F..%5Cevil%3A%00%E2%80%AE.bin")));
        var row = Assert.Single(await store.ListAttachmentsAsync(source));
        Assert.DoesNotContain('/', row.FileName); Assert.DoesNotContain('\\', row.FileName); Assert.DoesNotContain(':', row.FileName);
        Assert.DoesNotContain('\0', row.FileName); Assert.DoesNotContain('\u202e', row.FileName);
        var saved = await store.SaveAttachmentAsync(source, row);
        Assert.Equal(new byte[] {0,255,128,13,10,42}, File.ReadAllBytes(saved.Path));
    }

    [Fact]
    public void DecodedNamesAndContinuationCountsAreBounded()
    {
        Assert.Throws<InvalidDataException>(() => Read("filename*=utf-8''" + new string('a', 16385)));
        string segments = string.Join("; ", Enumerable.Range(0,33).Select(i => $"filename*{i}=a"));
        Assert.Throws<InvalidDataException>(() => Read(segments));
        Assert.Equal(120, Read("filename*=utf-8''" + new string('a', 200)).FileName.Length);
    }
    private static Broiler.Mail.Core.Services.ReceivedAttachment Read(string parameters)
        => Assert.Single(MimeAttachmentCatalog.Read(new(Key(), Source(parameters))));
    private static MailMessageKey Key() => new(AccountId.New(), "INBOX", 7, 1);
    private static byte[] Source(string parameters, string type = "application/octet-stream") => Encoding.ASCII.GetBytes(
        "Content-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/plain\r\n\r\nBody\r\n--x\r\nContent-Type: " + type +
        "\r\nContent-Disposition: attachment; " + parameters + "\r\nContent-Transfer-Encoding: base64\r\n\r\nAP+ADQoq\r\n--x--\r\n");
}
