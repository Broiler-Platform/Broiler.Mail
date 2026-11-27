using System.Text;
using System.Text.Json.Nodes;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class LocalOriginalTests
{
    private static byte[] Bytes => Encoding.ASCII.GetBytes("Subject: Local\r\n\r\n<script>source only</script>").Concat(new byte[] { 0, 128, 255 }).ToArray();
    [Fact]
    public async Task OpenIsReadOnlyAndImportSnapshotsExactBytesWithIndependentLocalIdentity()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        string path = directory.File("local.EML"); await File.WriteAllBytesAsync(path, Bytes);
        var opened = await store.OpenFileAsync(path); Assert.Equal(Bytes, opened.Bytes); Assert.False(File.Exists(directory.File("local-originals.json")));
        var receipt = await store.ImportAsync(account, opened); opened.Bytes[0] ^= 1;
        Assert.Equal(Bytes, (await store.ReadImportAsync(receipt)).Bytes); Assert.Equal(Bytes, await File.ReadAllBytesAsync(path)); Assert.NotEqual(Guid.Empty, receipt.Id);
        Assert.Equal(receipt, Assert.Single(await new JsonLocalMessageOriginals(directory.Root).ListImportsAsync(account))); Assert.Empty(await store.ListImportsAsync(AccountId.New()));
        var read = await store.ReadImportAsync(receipt); read.Bytes[0] ^= 1; Assert.Equal(Bytes, (await store.ReadImportAsync(receipt)).Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadImportAsync(receipt with { AccountId = AccountId.New() }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadImportAsync(receipt with { Sha256 = new string('0', 64) }));
        Assert.DoesNotContain(directory.Root.Replace("\\", "\\\\"), await File.ReadAllTextAsync(directory.File("local-originals.json")));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(IStreamingMessageSourceExports.MaximumBytes + 1)]
    public async Task OversizedOrEmptyFilesAreRejectedBeforeReading(int length)
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("fixture.eml");
        using (var file = File.Create(path)) file.SetLength(length);
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonLocalMessageOriginals(directory.Root).OpenFileAsync(path));
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task InvalidInputsAndCancellationDoNotWrite()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenFileAsync("relative.eml"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenFileAsync(directory.File("file.exe")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(default, new("file.eml", Bytes)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("../file.eml", Bytes)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("file.eml", [])));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("file.eml", new byte[MailMessageSource.MaximumBytes + 1])));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(account, new("file.eml", Bytes), new(true)));
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task QuotasNeverEvictOrPartiallyReplaceTheExistingCollection()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        for (int i = 0; i < JsonLocalMessageOriginals.MaximumFiles; i++) await store.ImportAsync(account, new("file.eml", Bytes));
        byte[] before = await File.ReadAllBytesAsync(directory.File("local-originals.json"));
        await Assert.ThrowsAsync<IOException>(() => store.ImportAsync(account, new("file.eml", Bytes)));
        Assert.Equal(before, await File.ReadAllBytesAsync(directory.File("local-originals.json")));
        using var second = new TestDirectory(); second.Create(); var large = new JsonLocalMessageOriginals(second.Root);
        await large.ImportAsync(account, new("first.eml", new byte[MailMessageSource.MaximumBytes])); await large.ImportAsync(account, new("second.eml", new byte[MailMessageSource.MaximumBytes]));
        await Assert.ThrowsAsync<IOException>(() => large.ImportAsync(account, new("third.eml", Bytes))); Assert.Equal(2, (await large.ListImportsAsync(account)).Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrFutureStorageFailsClosedWithoutOverwriting(bool future)
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        var receipt = await store.ImportAsync(account, new("file.eml", Bytes)); string path = directory.File("local-originals.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (future) json["schemaVersion"] = 99; else json["data"]!["entries"]![0]!["bytes"] = Convert.ToBase64String(new byte[Bytes.Length]);
        await File.WriteAllTextAsync(path, json.ToJsonString()); byte[] before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListImportsAsync(account));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadImportAsync(receipt));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(account, new("file.eml", Bytes))); Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task OpenFileAcceptsTwelveMiBAndRejectsOneByteMore()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root);
        Assert.Equal(12 * 1024 * 1024, LocalOriginalLimits.OpenBytes); Assert.Equal(2 * 1024 * 1024, MailMessageSource.MaximumBytes); Assert.Equal(MailMessageSource.MaximumBytes, LocalOriginalLimits.ImportBytes);
        string path = directory.File("large.eml"); byte[] bytes = new byte[LocalOriginalLimits.OpenBytes]; bytes[0] = 83; bytes[^1] = 90; await File.WriteAllBytesAsync(path, bytes);
        var opened = await store.OpenFileAsync(path); Assert.Equal("large.eml", opened.FileName); Assert.Equal(bytes, opened.Bytes);
        await File.WriteAllBytesAsync(path, new byte[LocalOriginalLimits.OpenBytes + 1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenFileAsync(path)); Assert.Equal("Local .eml files must contain 1 byte to 12 MiB.", error.Message);
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task OpenStreamReadsBoundedBytesWithoutTrustingLengthAndSanitizesName()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root);
        // The stream claims 10 bytes but never ends: reading stops one byte past the limit.
        var endless = new LyingStream(long.MaxValue, reportedLength: 10);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenStreamAsync("big.eml", endless));
        Assert.Equal(JsonLocalMessageOriginals.OpenSizeMessage, error.Message); Assert.Equal(LocalOriginalLimits.OpenBytes + 1L, endless.Produced);
        // The stream claims 1 byte but holds exactly the limit.
        var exact = new LyingStream(LocalOriginalLimits.OpenBytes, reportedLength: 1);
        var opened = await store.OpenStreamAsync("folder/report?<1>.eml", exact);
        Assert.Equal(LocalOriginalLimits.OpenBytes, opened.Bytes.Length); Assert.Equal("report__1_.eml", opened.FileName); Assert.Equal(MimeAttachmentCatalog.SafeFileName("report?<1>.eml"), opened.FileName);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenStreamAsync("empty.eml", new MemoryStream()));
        // Document providers may show names without an extension; the forwarder opens them too.
        var small = await new FileMessageSourceExports(directory.Root).OpenStreamAsync("no-extension", new MemoryStream([65, 66]));
        Assert.Equal(new byte[] { 65, 66 }, small.Bytes); Assert.Equal("no-extension", small.FileName);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.OpenStreamAsync("x.eml", new MemoryStream([65]), new(true)));
        Assert.False(File.Exists(directory.File("local-originals.json")));
    }
    [Fact]
    public async Task ImportStaysLimitedToTwoMiBWithKeepFileGuidance()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root); var account = AccountId.New();
        Assert.Equal(LocalOriginalLimits.ImportFiles, JsonLocalMessageOriginals.MaximumFiles); Assert.Equal(LocalOriginalLimits.ImportTotalBytes, JsonLocalMessageOriginals.MaximumTotalBytes);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => store.ImportAsync(account, new("large.eml", new byte[LocalOriginalLimits.ImportBytes + 1])));
        Assert.Equal("Keeping a copy in the profile is limited to 2 MiB per file (16 files, 4 MiB total). Keep the original file where you saved it; it can still be opened and viewed.", error.Message);
        Assert.False(File.Exists(directory.File("local-originals.json")));
        var kept = await store.ImportAsync(account, new("edge.eml", new byte[LocalOriginalLimits.ImportBytes])); Assert.Equal(LocalOriginalLimits.ImportBytes, kept.Bytes);
        Assert.Equal(LocalOriginalLimits.ImportBytes, (await store.ReadImportAsync(kept)).Bytes.Length);
    }
    [Fact]
    public async Task DecodeAcceptsTwelveMiBAndReportsToDateAndAttachments()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new JsonLocalMessageOriginals(directory.Root);
        byte[] bytes = LargeMessage(LocalOriginalLimits.OpenBytes); Assert.Equal(LocalOriginalLimits.OpenBytes, bytes.Length);
        var preview = await store.DecodeAsync(new("large.eml", bytes));
        Assert.Equal("Large local", preview.Subject); Assert.Equal("one@example.test, two@example.test", preview.To);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(1)), preview.Date);
        // The inline cid: image is part of the body, not an attachment.
        var attachment = Assert.Single(preview.Attachments); Assert.Equal("data.bin", attachment.FileName); Assert.Equal("application/octet-stream", attachment.MediaType);
        Assert.Equal(LargeAttachmentBytes, attachment.DecodedBytes); Assert.False(preview.IsAttachmentListIncomplete); Assert.Contains("Body text", preview.PlainText);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => store.DecodeAsync(new("large.eml", new byte[LocalOriginalLimits.OpenBytes + 1])));
        Assert.Equal(JsonLocalMessageOriginals.OpenSizeMessage, error.Message);
        var plain = await store.DecodeAsync(new("plain.eml", Encoding.ASCII.GetBytes("Subject: None\r\n\r\nText")));
        Assert.Null(plain.To); Assert.Null(plain.Date); Assert.Empty(plain.Attachments);
    }
    internal const int LargeAttachmentBytes = 6 * 1024 * 1024;
    /// <summary>A message of exactly <paramref name="length"/> bytes: text, an inline cid: image and a 6 MiB attachment, padded in the epilogue.</summary>
    internal static byte[] LargeMessage(int length)
    {
        var data = new byte[LargeAttachmentBytes]; for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
        var text = new StringBuilder();
        text.Append("Subject: Large local\r\nFrom: sender@example.test\r\nTo: one@example.test, two@example.test\r\nDate: 04 Mar 2026 05:06:07 +01:00\r\n");
        text.Append("MIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"b\"\r\n\r\n--b\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nBody text\r\n");
        text.Append("--b\r\nContent-Type: image/png; name=\"logo.png\"\r\nContent-Disposition: inline; filename=\"logo.png\"\r\nContent-ID: <logo@example.test>\r\nContent-Transfer-Encoding: base64\r\n\r\niVBORw0KGgo=\r\n");
        text.Append("--b\r\nContent-Type: application/octet-stream; name=\"data.bin\"\r\nContent-Disposition: attachment; filename=\"data.bin\"\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        text.Append(Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n--b--\r\n");
        while (text.Length + 72 <= length) text.Append('x', 70).Append("\r\n");
        text.Append('x', length - text.Length);
        return Encoding.ASCII.GetBytes(text.ToString());
    }
    private sealed class LyingStream(long total, long reportedLength) : Stream
    {
        public long Produced { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => reportedLength;
        public override long Position { get => Produced; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int read = (int)Math.Min(buffer.Length, total - Produced); buffer[..read].Fill(65); Produced += read; return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => ValueTask.FromResult(Read(buffer.Span));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Fact]
    public async Task ConcurrentImportsSerializeWithoutLosingAnotherAccount()
    {
        using var directory = new TestDirectory(); directory.Create(); var first = AccountId.New(); var second = AccountId.New();
        var store = new JsonLocalMessageOriginals(directory.Root);
        await Task.WhenAll(store.ImportAsync(first, new("one.eml", Bytes)), new JsonLocalMessageOriginals(directory.Root).ImportAsync(second, new("two.eml", Bytes)));
        Assert.Single(await store.ListImportsAsync(first)); Assert.Single(await store.ListImportsAsync(second));
    }
}
