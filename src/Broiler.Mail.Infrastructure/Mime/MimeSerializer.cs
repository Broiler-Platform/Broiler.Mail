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
        var sb = new StringBuilder();
        var date = draft.SubmissionDate ?? DateTimeOffset.UtcNow;
        string offset = date.Offset >= TimeSpan.Zero ? $"+{date.Offset:hhmm}" : $"-{date.Offset.Negate():hhmm}";
        string dateStr = date.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture) + offset;

        sb.Append("Message-ID: <").Append(draft.Id.ToString("N")).Append("@broiler.mail>\r\n");
        sb.Append("Date: ").Append(dateStr).Append("\r\n");
        sb.Append("Subject: ").Append(QuotedPrintable.EncodeQWord(draft.Subject ?? "")).Append("\r\n");
        sb.Append("From: ").Append(FormatMailbox(account.DisplayName, draft.FromAddress)).Append("\r\n");

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
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: 8bit\r\n");
        sb.Append("\r\n");

        // Normalize body to CRLF
        string body = (draft.PlainText ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        sb.Append(body);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string FormatMailbox(string? displayName, string address)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return FormatAddress(address);
        }
        string encodedName = QuotedPrintable.EncodeQWord(displayName.Trim());
        return $"{encodedName} <{address.Trim()}>";
    }

    private static string FormatAddress(string address)
    {
        return address.Trim();
    }
}
