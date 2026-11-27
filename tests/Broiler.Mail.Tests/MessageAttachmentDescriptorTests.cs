using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;

namespace Broiler.Mail.Tests;

public sealed class MessageAttachmentDescriptorTests
{
    private static readonly MailMessageKey Key = new(AccountId.New(), "INBOX", 7, 1);

    private static byte[] Single(string headers, string payload) => Encoding.Latin1.GetBytes(
        "From: sender@example.test\r\nSubject: One\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=x\r\n\r\n" +
        "--x\r\nContent-Type: text/plain\r\n\r\nReader body\r\n" +
        "--x\r\n" + headers + "\r\n\r\n" + payload + "\r\n--x--\r\n");

    private static MailMessageBody Received(byte[] bytes) => MessageTextDecoder.DecodeReceived(Key, bytes, CancellationToken.None);

    private static async Task<MailMessageBody> Decoded(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await MessageTextDecoder.DecodeAsync(Key, stream, CancellationToken.None);
    }

    [Fact]
    public void ReaderBodyListsRegularAttachmentsWithNameTypeAndDecodedSize()
    {
        byte[] pdf = Enumerable.Range(0, 300).Select(value => (byte)value).ToArray();
        byte[] bytes = Single("Content-Type: application/pdf\r\nContent-Disposition: attachment; filename*=utf-8''Bericht%20%C3%BCber.pdf\r\nContent-Transfer-Encoding: base64",
            Convert.ToBase64String(pdf, Base64FormattingOptions.InsertLineBreaks));
        var body = Received(bytes);
        var attachment = Assert.Single(body.Attachments!);
        Assert.Equal("Bericht über.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal(pdf.Length, attachment.DecodedBytes);
        Assert.False(attachment.IsContainer);
        Assert.False(body.IsAttachmentListIncomplete);
        Assert.Equal(MimeAttachmentCatalog.Read(new(Key, bytes))[0].PartNumber, attachment.PartNumber);
        Assert.Equal("Reader body", body.PlainText);
    }

    [Fact]
    public void InlineCidImagesAreNotAttachments()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(
            "Content-Type: multipart/related; boundary=x\r\n\r\n" +
            "--x\r\nContent-Type: text/html\r\n\r\n<p><img src=\"cid:logo\"></p>\r\n" +
            "--x\r\nContent-Type: image/png; name=logo.png\r\nContent-ID: <logo>\r\nContent-Transfer-Encoding: base64\r\n\r\nAQID\r\n" +
            "--x\r\nContent-Type: image/png; name=banner.png\r\nContent-Location: banner.png\r\nContent-Transfer-Encoding: base64\r\n\r\nAQID\r\n--x--\r\n");
        var body = Received(bytes);
        Assert.NotNull(body.Attachments);
        Assert.Empty(body.Attachments);
        Assert.False(body.IsAttachmentListIncomplete);
        Assert.Contains("logo", body.EmbeddedImages.Keys);
    }

    [Fact]
    public void DispositionAttachmentAndLegacyNameOnlyPartsAreCounted()
    {
        var fixture = Received(ReceivedAttachmentTests.Original());
        Assert.Equal(MimeAttachmentCatalog.Read(new(Key, ReceivedAttachmentTests.Original())).Select(row => (row.PartNumber, row.FileName, row.MediaType, row.Bytes)),
            fixture.Attachments!.Select(row => (row.PartNumber, row.FileName, row.MediaType, row.DecodedBytes)));

        byte[] explicitParts = Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/html\r\nContent-Disposition: attachment; filename=page.html\r\n\r\n<script>raw</script>\r\n--x\r\nContent-Type: image/png\r\nContent-ID: <picture>\r\nContent-Disposition: attachment; filename=picture.png\r\n\r\nnot-an-image\r\n--x--\r\n");
        Assert.Equal(new[] { "page.html", "picture.png" }, Received(explicitParts).Attachments!.Select(row => row.FileName));

        byte[] legacy = Single("Content-Type: application/pdf; name=legacy.pdf\r\nContent-Transfer-Encoding: base64", "QUJD");
        var listed = Assert.Single(Received(legacy).Attachments!);
        Assert.Equal(("legacy.pdf", 3), (listed.FileName, listed.DecodedBytes));
    }

    public static TheoryData<string, string, bool> Encodings() => new()
    {
        { "base64", Convert.ToBase64String(Enumerable.Range(0, 200).Select(value => (byte)value).ToArray(), Base64FormattingOptions.InsertLineBreaks), true },
        { "base64", "QUJDRA", false },
        { "quoted-printable", "Hello=\r\n World=3D=\r\nend=0D=0A", true },
        { "7bit", "plain text\r\nsecond line", true },
        { "binary", "ÿ\u0000\u0080 raw", true },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task DecodedSizeMatchesTheCatalog(string encoding, string payload, bool catalogAccepts)
    {
        byte[] bytes = Single("Content-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=data.bin\r\nContent-Transfer-Encoding: " + encoding, payload);
        var received = Assert.Single(Received(bytes).Attachments!);
        var decoded = Assert.Single((await Decoded(bytes)).Attachments!);
        int expected = MimeParser.Parse(bytes).Parts[1].Data.Length;
        Assert.True(expected > 0);
        Assert.Equal(expected, received.DecodedBytes);
        Assert.Equal(received, decoded);
        if (catalogAccepts) Assert.Equal(MimeAttachmentCatalog.Read(new(Key, bytes))[0].Bytes, received.DecodedBytes);
    }

    [Fact]
    public void ReaderParseKeepsAttachmentPayloadsUnallocated()
    {
        var parsed = MimeParser.ParseForReader(ReceivedAttachmentTests.Original());
        var part = parsed.Parts[1];
        Assert.True(part.IsAttachment);
        Assert.Empty(part.Data);
        Assert.Equal(ReceivedAttachmentTests.Binary.Length, part.DecodedLength);
        Assert.True(parsed.Parts[2].DecodedLength > 0);
    }

    [Fact]
    public void MoreThan32AttachmentsKeepsFirst32AndMarksIncomplete()
    {
        string Part(int i) => $"--x\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename={i}.bin\r\n\r\nx\r\n";
        byte[] raw = Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n" + string.Concat(Enumerable.Range(1, 33).Select(Part)) + "--x--\r\n");
        var body = Received(raw);
        Assert.Equal(MailAttachmentDescriptor.MaximumCount, body.Attachments!.Count);
        Assert.Equal("32.bin", body.Attachments[^1].FileName);
        Assert.True(body.IsAttachmentListIncomplete);

        byte[] exact = Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n" + string.Concat(Enumerable.Range(1, 32).Select(Part)) + "--x--\r\n");
        Assert.False(Received(exact).IsAttachmentListIncomplete);
        Assert.True(Received(ReceivedAttachmentTests.Original(close: false)).IsAttachmentListIncomplete);
    }

    [Fact]
    public void UnsafeNamesAndTypesAreSanitized()
    {
        var fixture = Received(ReceivedAttachmentTests.Original());
        Assert.Equal(MimeAttachmentCatalog.SafeFileName("../../evil:name.bin"), fixture.Attachments![0].FileName);
        Assert.DoesNotContain('/', fixture.Attachments[0].FileName);

        byte[] badType = Single("Content-Type: not-a-type; name=\"x:y.exe\"\r\nContent-Disposition: attachment", "payload");
        var listed = Assert.Single(Received(badType).Attachments!);
        Assert.Equal("application/octet-stream", listed.MediaType);
        Assert.Equal(MimeAttachmentCatalog.SafeFileName("x:y.exe"), listed.FileName);
        Assert.DoesNotContain(':', listed.FileName);

        byte[] unnamed = Single("Content-Type: application/zip\r\nContent-Disposition: attachment", "zip");
        Assert.Equal("attachment.bin", Assert.Single(Received(unnamed).Attachments!).FileName);
    }

    [Fact]
    public async Task MultipartAttachmentContainerIsListedAsContainer()
    {
        string inner = "--inner\r\nContent-Type: text/plain\r\n\r\nforwarded\r\n--inner--";
        byte[] bytes = Single("Content-Type: multipart/mixed; boundary=inner\r\nContent-Disposition: attachment; filename=forwarded.eml", inner);
        var received = Assert.Single(Received(bytes).Attachments!);
        Assert.True(received.IsContainer);
        Assert.Equal("forwarded.eml", received.FileName);
        Assert.Equal(Encoding.ASCII.GetByteCount(inner), received.DecodedBytes);
        Assert.Equal(received, Assert.Single((await Decoded(bytes)).Attachments!));
    }

    [Fact]
    public void AnAttachedContainerDoesNotBlockExtractingTheAttachmentsBesideIt()
    {
        byte[] pdf = Enumerable.Range(0, 300).Select(value => (byte)value).ToArray();
        byte[] bytes = Encoding.ASCII.GetBytes(
            "Content-Type: multipart/mixed; boundary=x\r\n\r\n--x\r\nContent-Type: text/plain\r\n\r\nReader body\r\n" +
            "--x\r\nContent-Type: application/pdf\r\nContent-Disposition: attachment; filename=report.pdf\r\nContent-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(pdf) + "\r\n" +
            "--x\r\nContent-Type: multipart/mixed; boundary=inner\r\nContent-Disposition: attachment; filename=fwd.eml\r\n\r\n--inner\r\nContent-Type: text/plain\r\n\r\nforwarded\r\n--inner--\r\n" +
            "--x\r\nContent-Type: text/plain\r\nContent-Disposition: attachment; filename=notes.txt\r\n\r\nnotes\r\n--x--\r\n");
        var listed = Received(bytes).Attachments!;
        Assert.Equal(new[] { false, true, false }, listed.Select(row => row.IsContainer));
        // The container is skipped, not fatal: both leaves come out under the part numbers the reader listed.
        var rows = MimeAttachmentCatalog.Read(new(Key, bytes));
        Assert.Equal(new[] { (listed[0].PartNumber, "report.pdf", 300), (listed[2].PartNumber, "notes.txt", 5) },
            rows.Select(row => (row.PartNumber, row.FileName, row.Bytes)));
    }

    [Fact]
    public void AContainerCountsTowardTheAttachmentLimitAsTheReaderListsIt()
    {
        string Part(int i) => $"--x\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename={i}.bin\r\n\r\nx\r\n";
        const string container = "--x\r\nContent-Type: multipart/mixed; boundary=inner\r\nContent-Disposition: attachment; filename=fwd.eml\r\n\r\n--inner\r\nContent-Type: text/plain\r\n\r\nf\r\n--inner--\r\n";
        byte[] raw = Encoding.ASCII.GetBytes("Content-Type: multipart/mixed; boundary=x\r\n\r\n" + container + string.Concat(Enumerable.Range(1, 32).Select(Part)) + "--x--\r\n");
        Assert.True(Received(raw).IsAttachmentListIncomplete);
        Assert.Throws<InvalidDataException>(() => MimeAttachmentCatalog.Read(new(Key, raw)));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("Nul.tar.gz", "_Nul.tar.gz")]
    [InlineData("aux .pdf", "_aux .pdf")]
    [InlineData("PRN...", "_PRN")]
    [InlineData("com1.log", "_com1.log")]
    [InlineData("LPT9", "_LPT9")]
    [InlineData("COM10.txt", "COM10.txt")]
    [InlineData("console.txt", "console.txt")]
    [InlineData("my con.txt", "my con.txt")]
    public void WindowsDeviceNamesArePrefixed(string value, string expected)
    {
        Assert.Equal(expected, MimeAttachmentCatalog.SafeFileName(value));
        Assert.Equal(expected, MimeAttachmentCatalog.SafeFileName(expected));
        var listed = Assert.Single(Received(Single($"Content-Type: text/plain\r\nContent-Disposition: attachment; filename=\"{value}\"", "x")).Attachments!);
        Assert.Equal(expected, listed.FileName);
    }

    [Fact]
    public void APrefixedDeviceNameStaysWithinTheLengthLimit()
    {
        string name = MimeAttachmentCatalog.SafeFileName("CON." + new string('a', 200));
        Assert.Equal(120, name.Length);
        Assert.StartsWith("_CON.", name, StringComparison.Ordinal);
        Assert.Equal(name, MimeAttachmentCatalog.SafeFileName(name));
    }

    [Fact]
    public async Task LocalOriginalPreviewListsTheSameAttachments()
    {
        using var stream = new MemoryStream(ReceivedAttachmentTests.Original());
        var preview = await MessageTextDecoder.DecodeLocalAsync(stream, CancellationToken.None);
        Assert.Equal(Received(ReceivedAttachmentTests.Original()).Attachments, preview.Attachments);
        Assert.Equal(2, preview.Attachments.Count);
        Assert.False(preview.IsAttachmentListIncomplete);
    }
}
