using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class MessageAttachmentReaderTests
{
    private static readonly byte[] Payload = MakePayload(300_000);

    private static byte[] MakePayload(int length)
    {
        var bytes = new byte[length];
        new Random(17).NextBytes(bytes);
        return bytes;
    }

    /// <summary>A text part, an inline cid: image that is not an attachment, and one attachment with an RFC 2231 name.</summary>
    private static byte[] Original(byte[]? payload = null) => Encoding.ASCII.GetBytes(
        "From: sender@example.test\r\nTo: test@example.test\r\nSubject: Report\r\nMIME-Version: 1.0\r\n" +
        "Content-Type: multipart/mixed; boundary=outer\r\n\r\n" +
        "--outer\r\nContent-Type: multipart/related; boundary=inner\r\n\r\n" +
        "--inner\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>See <img src=\"cid:logo@example.test\"></p>\r\n" +
        "--inner\r\nContent-Type: image/png; name=logo.png\r\nContent-ID: <logo@example.test>\r\nContent-Disposition: inline; filename=logo.png\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(new byte[] { 137, 80, 78, 71 }) + "\r\n" +
        "--inner--\r\n" +
        "--outer\r\nContent-Type: application/pdf\r\nContent-Disposition: attachment; filename*=utf-8''caf%C3%A9.pdf\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(payload ?? Payload, Base64FormattingOptions.InsertLineBreaks) + "\r\n" +
        "--outer--\r\n");

    private static MailMessageKey Key(AccountProfile account, uint uid = 1) => new(account.Id, "INBOX", 7, uid);

    [Fact]
    public async Task PrefersVerifiedSavedExportOverNetwork()
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = Key(account);
        var exports = new FileMessageSourceExports(directory.Root);
        await exports.SaveAsync(new MailMessageSource(key, Original()));
        var receiver = TestSourceReceiver.Returning(Original());
        var reader = new MessageAttachmentReader(receiver, receiver, exports, exports);

        using var source = await reader.OpenAsync(account, key);

        Assert.True(source.FromSavedOriginal);
        Assert.Equal(key, source.Key);
        Assert.Equal(0, receiver.CopySourceCalls + receiver.SourceCalls);
        var attachment = Assert.Single(source.Attachments);
        Assert.Equal("café.pdf", attachment.FileName);
        Assert.Equal(Payload, source.Read(attachment).ToArray());
    }

    [Fact]
    public async Task FallsBackToStreamingWhenReceiptFailsVerification()
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = Key(account);
        var exports = new FileMessageSourceExports(directory.Root);
        var missing = await exports.SaveAsync(new MailMessageSource(key, Original()));
        await Task.Delay(20);
        var changed = await exports.SaveAsync(new MailMessageSource(key, Original()));
        File.Delete(missing.Path);
        byte[] tampered = await File.ReadAllBytesAsync(changed.Path); tampered[^5] ^= 1;
        await File.WriteAllBytesAsync(changed.Path, tampered);
        var receiver = TestSourceReceiver.Returning(Original());
        var progress = new List<(int, int)>();

        using var source = await new MessageAttachmentReader(receiver, receiver, exports, exports).OpenAsync(account, key, (done, total) => progress.Add((done, total)));

        Assert.False(source.FromSavedOriginal);
        Assert.Equal(1, receiver.CopySourceCalls);
        Assert.Equal(0, receiver.SourceCalls);
        Assert.NotEmpty(progress);
        Assert.Equal(Payload, source.Read(Assert.Single(source.Attachments)).ToArray());
    }

    [Fact]
    public async Task UsesTheBufferedReceiverOnlyWithoutAStreamingOne()
    {
        var account = TestDirectory.Profile(); var key = Key(account);
        var receiver = TestSourceReceiver.Returning(Original());
        using (var source = await new MessageAttachmentReader(null, receiver).OpenAsync(account, key))
        {
            Assert.False(source.FromSavedOriginal);
            Assert.Single(source.Attachments);
        }
        Assert.Equal((0, 1), (receiver.CopySourceCalls, receiver.SourceCalls));

        var foreign = new TestSourceReceiver { Source = (_, _) => Task.FromResult(new MailMessageSource(Key(account, 2), Original())) };
        await Assert.ThrowsAsync<InvalidDataException>(() => new MessageAttachmentReader(null, foreign).OpenAsync(account, key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MessageAttachmentReader(null, null).OpenAsync(account, key));
        await Assert.ThrowsAsync<ArgumentException>(() => new MessageAttachmentReader(null, receiver).OpenAsync(TestDirectory.Profile(), key));
    }

    [Fact]
    public async Task AStreamedOriginalIsBoundedAtTheExportLimit()
    {
        var account = TestDirectory.Profile(); var key = Key(account);
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, destination, maximum, _, token) =>
            {
                Assert.Equal(IStreamingMessageSourceExports.MaximumBytes, maximum);
                // A server that ignores the limit is stopped by the buffer itself.
                await destination.WriteAsync(new byte[maximum], token);
                await destination.WriteAsync(new byte[1], token);
                return maximum + 1;
            },
        };
        await Assert.ThrowsAsync<IOException>(() => new MessageAttachmentReader(receiver, null).OpenAsync(account, key));

        var shortCopy = new TestSourceReceiver { CopySource = (_, _, _, _, _) => Task.FromResult(10) };
        await Assert.ThrowsAsync<InvalidDataException>(() => new MessageAttachmentReader(shortCopy, null).OpenAsync(account, key));
    }

    [Fact]
    public async Task StreamsOnceOverLoopbackImapWithBodyPeekOnly()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, Encoding.ASCII.GetString(Original())));
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(10));

        using var source = await new MessageAttachmentReader(receiver, receiver).OpenAsync(account, Key(account));

        var attachment = Assert.Single(source.Attachments);
        Assert.Equal("café.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal(Payload.Length, attachment.Bytes);
        Assert.Equal(Payload, source.Read(attachment).ToArray());
        Assert.Single(server.MailCommands, command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("BODY[", StringComparison.Ordinal) || command.Contains("STORE", StringComparison.Ordinal));
        Assert.Contains("EXAMINE", server.Commands);
        Assert.DoesNotContain(server.Commands, command => command is "SELECT" or "STORE" or "EXPUNGE");
        Assert.False(Assert.Single(server.Messages).IsRead);
    }

    [Fact]
    public async Task CopyToWritesExactDecodedOctetsAndHonoursCancellationMidWrite()
    {
        var account = TestDirectory.Profile();
        using var source = await new MessageAttachmentReader(TestSourceReceiver.Returning(Original()), null).OpenAsync(account, Key(account));
        var attachment = Assert.Single(source.Attachments);

        using (var copy = new MemoryStream())
        {
            await source.CopyToAsync(attachment, copy);
            Assert.Equal(Payload, copy.ToArray());
        }

        using var cancellation = new CancellationTokenSource();
        var slow = new CancelAfterFirstWrite(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.CopyToAsync(attachment, slow, cancellation.Token));
        Assert.Equal(1, slow.Writes);
        Assert.Equal(MessageAttachmentReader.SliceBytes, slow.Length);

        // The source stays usable after a canceled save.
        using var again = new MemoryStream();
        await source.CopyToAsync(attachment, again);
        Assert.Equal(Payload.Length, again.Length);
    }

    [Fact]
    public async Task CopyToRejectsForeignKeyOrChangedRecord()
    {
        var account = TestDirectory.Profile();
        var source = await new MessageAttachmentReader(TestSourceReceiver.Returning(Original()), null).OpenAsync(account, Key(account));
        var attachment = Assert.Single(source.Attachments);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => source.CopyToAsync(attachment with { Key = Key(account, 2) }, destination));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.CopyToAsync(attachment with { Sha256 = new string('0', 64) }, destination));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.CopyToAsync(attachment with { PartNumber = attachment.PartNumber + 1 }, destination));
        Assert.Throws<InvalidDataException>(() => source.Read(attachment with { Bytes = attachment.Bytes - 1 }));
        Assert.Equal(0, destination.Length);

        source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.CopyToAsync(attachment, destination));
        Assert.Throws<ObjectDisposedException>(() => source.Read(attachment));
    }

    [Fact]
    public async Task NeverCreatesAttachmentOrSourceExportFiles()
    {
        using var directory = new TestDirectory(); directory.Create();
        var exports = new FileMessageSourceExports(directory.Root);
        foreach (var folder in new[] { exports.ExportDirectory, exports.AttachmentDirectory })
        {
            Directory.CreateDirectory(folder);
            for (int index = 0; index < FileMessageSourceExports.MaximumFiles; index++)
                await File.WriteAllBytesAsync(Path.Combine(folder, $"existing-{index:00}.bin"), [1, 2, 3]);
        }
        string[] Snapshot() => Directory.EnumerateFiles(directory.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".lock", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        var before = Snapshot();
        var account = TestDirectory.Profile();
        var receiver = TestSourceReceiver.Returning(Original());

        // Both folders are at their 64-file quota, so any export there would fail; the interactive read still works.
        using (var source = await new MessageAttachmentReader(receiver, receiver, exports, exports).OpenAsync(account, Key(account)))
        {
            var attachment = Assert.Single(source.Attachments);
            using var destination = new MemoryStream();
            await source.CopyToAsync(attachment, destination);
            Assert.Equal(Payload, destination.ToArray());
        }

        Assert.Equal(before, Snapshot());
        Assert.Equal(1, receiver.CopySourceCalls);
    }

    private sealed class CancelAfterFirstWrite(CancellationTokenSource cancellation) : MemoryStream
    {
        public int Writes { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            Writes++;
            Write(buffer.Span);
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
