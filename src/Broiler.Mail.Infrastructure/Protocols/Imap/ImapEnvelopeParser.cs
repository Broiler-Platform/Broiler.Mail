// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using Broiler.Mail.Infrastructure.Mime.Encodings;

namespace Broiler.Mail.Infrastructure.Protocols.Imap;

/// <summary>
/// Parsed message summary from an IMAP FETCH response.
/// </summary>
public sealed class ImapMessageSummaryInfo
{
    public int SequenceNumber { get; set; }
    public uint Uid { get; set; }
    public bool IsSeen { get; set; }
    public DateTimeOffset? InternalDate { get; set; }
    public DateTimeOffset? EnvelopeDate { get; set; }
    public string? Subject { get; set; }
    public string? From { get; set; }
    public string? Sender { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
}

public sealed class ImapSExp
{
    public string? Value { get; set; }
    public List<ImapSExp>? Children { get; set; }
    public bool IsList => Children is not null;
    public bool IsNil => Value == null && Children == null;

    public string? AsString() => Value;
}

/// <summary>
/// Parser for IMAP S-expressions and FETCH responses (RFC 3501 §7.4.2).
/// </summary>
public static class ImapEnvelopeParser
{
    public static ImapSExp Parse(string text)
    {
        int index = 0;
        return ParseSExp(text, ref index);
    }

    public static ImapSExp ParseSExp(string text, ref int index)
    {
        SkipWhitespace(text, ref index);
        if (index >= text.Length) return new ImapSExp();

        if (text[index] == '(')
        {
            index++;
            var list = new List<ImapSExp>();
            while (index < text.Length)
            {
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ')')
                {
                    index++;
                    break;
                }
                list.Add(ParseSExp(text, ref index));
            }
            return new ImapSExp { Children = list };
        }

        if (text[index] == '"')
        {
            index++;
            var sb = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') break;
                if (c == '\\' && index < text.Length) c = text[index++];
                sb.Append(c);
            }
            return new ImapSExp { Value = sb.ToString() };
        }

        int start = index;
        while (index < text.Length && text[index] != ' ' && text[index] != '\t' && text[index] != ')' && text[index] != '(')
        {
            index++;
        }
        string atom = text[start..index];
        if (atom.Equals("NIL", StringComparison.OrdinalIgnoreCase)) return new ImapSExp();
        return new ImapSExp { Value = atom };
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n'))
        {
            index++;
        }
    }

    /// <summary>
    /// Parses an untagged line like `* 1 FETCH (UID 42 FLAGS (\Seen) ...)`
    /// </summary>
    public static ImapMessageSummaryInfo? ParseFetchLine(string line)
    {
        if (!line.StartsWith("* ")) return null;
        int fetchIndex = line.IndexOf(" FETCH ", StringComparison.OrdinalIgnoreCase);
        if (fetchIndex < 0) return null;

        string seqStr = line[2..fetchIndex].Trim();
        if (!int.TryParse(seqStr, out int seq)) return null;

        int parenStart = line.IndexOf('(', fetchIndex);
        if (parenStart < 0) return null;

        var exp = Parse(line[parenStart..]);
        if (!exp.IsList || exp.Children is null) return null;

        var summary = new ImapMessageSummaryInfo { SequenceNumber = seq };
        for (int i = 0; i < exp.Children.Count; i++)
        {
            var item = exp.Children[i];
            string? name = item.Value?.ToUpperInvariant();
            if (name == "UID" && i + 1 < exp.Children.Count)
            {
                if (uint.TryParse(exp.Children[++i].Value, out uint uid)) summary.Uid = uid;
            }
            else if (name == "FLAGS" && i + 1 < exp.Children.Count)
            {
                var flagsExp = exp.Children[++i];
                if (flagsExp.IsList && flagsExp.Children is not null)
                {
                    summary.IsSeen = flagsExp.Children.Any(f => f.Value?.Equals("\\Seen", StringComparison.OrdinalIgnoreCase) == true);
                }
            }
            else if (name == "INTERNALDATE" && i + 1 < exp.Children.Count)
            {
                string? dateStr = exp.Children[++i].Value;
                if (dateStr is not null && DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var idate))
                {
                    summary.InternalDate = idate;
                }
            }
            else if (name == "ENVELOPE" && i + 1 < exp.Children.Count)
            {
                var envExp = exp.Children[++i];
                ParseEnvelope(envExp, summary);
            }
        }

        return summary;
    }

    private static void ParseEnvelope(ImapSExp envExp, ImapMessageSummaryInfo summary)
    {
        if (!envExp.IsList || envExp.Children is null || envExp.Children.Count < 4) return;
        // ENVELOPE (date subject from sender reply-to to cc bcc in-reply-to message-id)
        var children = envExp.Children;
        string? dateStr = children[0].Value;
        if (dateStr is not null && DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var edate))
        {
            summary.EnvelopeDate = edate;
        }

        summary.Subject = Rfc2047Decoder.Decode(children[1].Value);
        summary.From = ParseAddressList(children[2]);
        if (children.Count > 3) summary.Sender = ParseAddressList(children[3]);
        if (children.Count > 5) summary.To = ParseAddressList(children[5]);
        if (children.Count > 6) summary.Cc = ParseAddressList(children[6]);
        if (children.Count > 8) summary.InReplyTo = children[8].Value;
        if (children.Count > 9) summary.MessageId = children[9].Value;
    }

    private static string? ParseAddressList(ImapSExp addrListExp)
    {
        if (!addrListExp.IsList || addrListExp.Children is null || addrListExp.Children.Count == 0) return null;
        var addresses = new List<string>();
        foreach (var addr in addrListExp.Children)
        {
            if (!addr.IsList || addr.Children is null || addr.Children.Count < 4) continue;
            // (name route mailbox host)
            string? name = addr.Children[0].Value;
            string? mailbox = addr.Children[2].Value;
            string? host = addr.Children[3].Value;

            if (string.IsNullOrEmpty(mailbox) || string.IsNullOrEmpty(host)) continue;
            string email = $"{mailbox}@{host}";

            if (!string.IsNullOrWhiteSpace(name))
            {
                string decodedName = Rfc2047Decoder.Decode(name);
                addresses.Add($"{decodedName} <{email}>");
            }
            else
            {
                addresses.Add(email);
            }
        }

        return addresses.Count > 0 ? string.Join(", ", addresses) : null;
    }
}
