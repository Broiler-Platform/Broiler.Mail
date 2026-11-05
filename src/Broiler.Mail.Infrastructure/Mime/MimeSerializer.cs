// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Mime.Encodings;

namespace Broiler.Mail.Infrastructure.Mime;

/// <summary>
/// RFC 5322 / MIME plain-text message serializer for SMTP submission and IMAP Sent copies.
/// Trimmer-safe, zero external dependencies.
/// </summary>
public static class MimeSerializer
{
    /// <summary>
    /// Serializes an outgoing draft into raw RFC 5322 UTF-8 bytes.
    /// </summary>
    public static byte[] Serialize(AccountProfile account, MailDraft draft, bool includeBcc = false)
    {
        using var result = new MemoryStream();
        foreach (var chunk in CreateChunks(account, draft, includeBcc)) result.Write(chunk.Span);
        return result.ToArray();
    }

    /// <summary>Prepares validated headers and produces MIME incrementally for SMTP DATA.</summary>
    internal static IEnumerable<ReadOnlyMemory<byte>> CreateChunks(AccountProfile account, MailDraft draft, bool includeBcc = false)
    {
        DraftAttachment.Validate(draft.Attachments);
        Broiler.Mail.Core.Validation.ConfigurationValidator.ValidateSenderDisplayName(draft.FromDisplayName ?? account.DisplayName);
        var sb = new StringBuilder();
        var date = draft.SubmissionDate ?? DateTimeOffset.UtcNow;
        string offset = date.Offset >= TimeSpan.Zero ? $"+{date.Offset:hhmm}" : $"-{date.Offset.Negate():hhmm}";
        string dateStr = date.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture) + offset;

        sb.Append("Message-ID: <").Append(draft.Id.ToString("N")).Append("@broiler.mail>\r\n");
        sb.Append("Date: ").Append(dateStr).Append("\r\n");
        sb.Append("Subject: ").Append(QuotedPrintable.EncodeQWord(draft.Subject ?? "")).Append("\r\n");
        sb.Append("From: ").Append(FormatMailbox(draft.FromDisplayName ?? account.DisplayName, draft.FromAddress)).Append("\r\n");

        if (draft.To.Count > 0)
        {
            sb.Append("To: ").Append(string.Join(", ", draft.To.Select(FormatAddress))).Append("\r\n");
        }

        if (draft.Cc.Count > 0)
        {
            sb.Append("Cc: ").Append(string.Join(", ", draft.Cc.Select(FormatAddress))).Append("\r\n");
        }

        if (includeBcc && draft.Bcc.Count > 0)
        {
            sb.Append("Bcc: ").Append(string.Join(", ", draft.Bcc.Select(FormatAddress))).Append("\r\n");
        }

        if (!string.IsNullOrWhiteSpace(draft.InReplyTo))
        {
            string inReply = draft.InReplyTo.Trim();
            if (!inReply.StartsWith('<')) inReply = $"<{inReply}>";
            sb.Append("In-Reply-To: ").Append(inReply).Append("\r\n");
        }

        if (draft.References.Count > 0)
        {
            sb.Append("References: ");
            for (int i = 0; i < draft.References.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                string r = draft.References[i].Trim();
                if (!r.StartsWith('<')) r = $"<{r}>";
                sb.Append(r);
            }
            sb.Append("\r\n");
        }

        sb.Append("MIME-Version: 1.0\r\n");
        string body = (draft.PlainText ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        if (draft.Attachments is { Count: > 0 } attachments)
        {
            string boundary = "broiler-" + draft.Id.ToString("N");
            sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
            sb.Append("--").Append(boundary).Append("\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
            return Multipart(Encoding.UTF8.GetBytes(sb.ToString()), Encoding.UTF8.GetBytes(body), boundary, attachments);
        }
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: 8bit\r\n");
        sb.Append("\r\n");

        // Normalize body to CRLF
        return [Encoding.UTF8.GetBytes(sb.ToString()), Encoding.UTF8.GetBytes(body)];
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Multipart(byte[] headers, byte[] body, string boundary, IReadOnlyList<DraftAttachment> attachments)
    {
        yield return headers;
        foreach (var chunk in EncodeBase64(body)) yield return chunk;
        foreach (var attachment in attachments)
        {
            string name = Uri.EscapeDataString(attachment.FileName);
            yield return Encoding.ASCII.GetBytes($"--{boundary}\r\nContent-Type: application/octet-stream\r\n" +
                $"Content-Disposition: attachment;\r\n filename*=utf-8''{name}\r\nContent-Transfer-Encoding: base64\r\n\r\n");
            foreach (var chunk in EncodeBase64(attachment.Content)) yield return chunk;
        }
        yield return Encoding.ASCII.GetBytes($"--{boundary}--\r\n");
    }

    private static IEnumerable<ReadOnlyMemory<byte>> EncodeBase64(ReadOnlyMemory<byte> bytes)
    {
        const int inputChunkSize = 57 * 100;
        for (int offset = 0; offset < bytes.Length; offset += inputChunkSize)
        {
            int count = Math.Min(inputChunkSize, bytes.Length - offset);
            string encoded = Convert.ToBase64String(bytes.Span.Slice(offset, count), Base64FormattingOptions.InsertLineBreaks);
            yield return Encoding.ASCII.GetBytes(encoded + "\r\n");
        }
    }

    private static string FormatMailbox(string? displayName, string address)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return FormatAddress(address);
        }
        string encodedName = QuotedPrintable.EncodeQWord(displayName.Trim());
        if (!string.Equals(encodedName, displayName.Trim(), StringComparison.Ordinal))
        {
            // At most 20 UTF-8 bytes => at most 72 characters per Q word, without splitting a Unicode scalar.
            var words = new List<string>(); var chunk = new StringBuilder(); int bytes = 0;
            foreach (var rune in displayName.Trim().EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 20)
                { words.Add(QuotedPrintable.EncodeQWord(chunk.ToString(), force: true)); chunk.Clear(); bytes = 0; }
                chunk.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
            }
            if (chunk.Length > 0) words.Add(QuotedPrintable.EncodeQWord(chunk.ToString(), force: true));
            encodedName = string.Join("\r\n ", words);
        }
        if (string.Equals(encodedName, displayName.Trim(), StringComparison.Ordinal) && encodedName.Any(c => "()<>[]:;@\\,\"".Contains(c)))
            encodedName = "\"" + encodedName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return $"{encodedName} <{address.Trim()}>";
    }

    private static string FormatAddress(string address)
    {
        return address.Trim();
    }
}
