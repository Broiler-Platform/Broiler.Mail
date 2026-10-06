// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Mime.Encodings;
using Xunit;

namespace Broiler.Mail.Tests;

public sealed class NativeMimeTests
{
    [Fact]
    public void QuotedPrintable_DecodesHexAndSoftLineBreaks()
    {
        string raw = "Hello=20World=21=\r\nNext=20Line=3D";
        byte[] bytes = QuotedPrintable.Decode(Encoding.ASCII.GetBytes(raw));
        string result = Encoding.UTF8.GetString(bytes);
        Assert.Equal("Hello World!Next Line=", result);
    }

    [Fact]
    public void Base64Decoder_ToleratesWhitespaceAndMissingPadding()
    {
        string raw = "  SGVs bG8=\r\n  ";
        byte[] bytes = Base64Decoder.Decode(raw);
        Assert.Equal("Hello", Encoding.UTF8.GetString(bytes));

        // Missing padding test
        string unpadded = "SGVsbG8"; // "Hello" without '='
        byte[] unpaddedBytes = Base64Decoder.Decode(unpadded);
        Assert.Equal("Hello", Encoding.UTF8.GetString(unpaddedBytes));
    }

    [Fact]
    public void Rfc2047Decoder_DecodesAdjacentEncodedWords()
    {
        // Two adjacent encoded words separated by whitespace: RFC 2047 §6.2 says whitespace must be stripped
        string header = "=?utf-8?B?U3ViamVjdA==?= =?utf-8?B?VGVzdA==?=";
        string decoded = Rfc2047Decoder.Decode(header);
        Assert.Equal("SubjectTest", decoded);

        // Q-word with underscore as space
        string qHeader = "=?iso-8859-1?Q?Gr=FC=DFe_friends?=";
        string qDecoded = Rfc2047Decoder.Decode(qHeader);
        Assert.Equal("Grüße friends", qDecoded);
    }

    [Fact]
    public async Task MimeParser_ParsesMultipartAlternativeAndRelated()
    {
        string raw =
            "From: Alice <alice@example.test>\r\n" +
            "To: Bob <bob@example.test>\r\n" +
            "Subject: =?utf-8?Q?Test_=E2=98=BA?=\r\n" +
            "Message-ID: <msg123@example.test>\r\n" +
            "References: <ref1@example.test> <ref2@example.test>\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/related; boundary=\"rel_bound\"\r\n\r\n" +
            "--rel_bound\r\n" +
            "Content-Type: multipart/alternative; boundary=\"alt_bound\"\r\n\r\n" +
            "--alt_bound\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "Content-Transfer-Encoding: 7bit\r\n\r\n" +
            "Plain text content\r\n" +
            "--alt_bound\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Transfer-Encoding: 7bit\r\n\r\n" +
            "<p>HTML content <img src=\"cid:logo@example.test\"></p>\r\n" +
            "--alt_bound--\r\n" +
            "--rel_bound\r\n" +
            "Content-Type: image/png\r\n" +
            "Content-ID: <logo@example.test>\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" +
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\r\n" +
            "--rel_bound--\r\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var message = await MimeParser.ParseAsync(stream);

        Assert.Equal("Test ☺", message.Subject);
        Assert.Equal("msg123@example.test", message.MessageId);
        Assert.Equal(["alice@example.test"], message.From);
        Assert.Equal(["bob@example.test"], message.To);
        Assert.Equal(["ref1@example.test", "ref2@example.test"], message.References);
        Assert.Equal("Plain text content", message.TextBody?.Trim());
        Assert.Contains("<p>HTML content", message.HtmlBody);
        Assert.True(message.EmbeddedImages.ContainsKey("logo@example.test"));
        Assert.Equal("image/png", message.EmbeddedImages["logo@example.test"].ContentType);
    }

    [Fact]
    public void MimeSerializer_ProducesValidRfc5322Message()
    {
        var profile = TestDirectory.Profile();
        var draft = new MailDraft
        {
            AccountId = profile.Id,
            Id = Guid.NewGuid(),
            FromAddress = profile.EmailAddress,
            To = ["recipient@example.test"],
            Cc = ["cc@example.test"],
            Bcc = ["secret@example.test"],
            Subject = "Grüße & café",
            PlainText = "Line 1\nLine 2",
            SubmissionDate = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.FromHours(2)),
            InReplyTo = "parent@example.test",
            References = ["root@example.test", "parent@example.test"],
        };

        // Without Bcc (for SMTP)
        byte[] smtpBytes = MimeSerializer.Serialize(profile, draft, includeBcc: false);
        string smtpString = Encoding.UTF8.GetString(smtpBytes);
        Assert.Contains($"Message-ID: <{draft.Id:N}@broiler.mail>", smtpString);
        string subjectLine = smtpString.Split("\r\n").First(line => line.StartsWith("Subject: "));
        Assert.Equal("Grüße & café", Rfc2047Decoder.Decode(subjectLine[9..]));
        Assert.Contains("To: recipient@example.test", smtpString);
        Assert.Contains("Cc: cc@example.test", smtpString);
        Assert.DoesNotContain("secret@example.test", smtpString);
        Assert.DoesNotContain("Bcc:", smtpString);
        Assert.Contains("In-Reply-To: <parent@example.test>", smtpString);
        Assert.Contains("References: <root@example.test> <parent@example.test>", smtpString);
        Assert.Contains("Line 1\r\nLine 2", smtpString);

        // With Bcc (for IMAP Sent copy)
        byte[] imapBytes = MimeSerializer.Serialize(profile, draft, includeBcc: true);
        string imapString = Encoding.UTF8.GetString(imapBytes);
        Assert.Contains("Bcc: secret@example.test", imapString);
    }
}
