// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Broiler.Mail.Infrastructure.Mime.Encodings;

/// <summary>
/// Decodes RFC 2047 encoded-word headers (=?charset?encoding?text?=).
/// Handles B (Base64) and Q (Quoted-Printable) encodings and strips linear whitespace between adjacent words.
/// </summary>
public static class Rfc2047Decoder
{
    static Rfc2047Decoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Unfolds multiline headers and decodes all RFC 2047 encoded words in the header value.
    /// </summary>
    public static string Decode(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue)) return "";

        // Unfold multiline headers: replace CRLF followed by whitespace with a single space
        string unfolded = Unfold(headerValue);
        if (!unfolded.Contains("=?")) return unfolded;

        var sb = new StringBuilder();
        int index = 0;
        bool lastWasEncodedWord = false;

        while (index < unfolded.Length)
        {
            int start = unfolded.IndexOf("=?", index, StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(unfolded.AsSpan(index));
                break;
            }

            // Check if there was only whitespace between the previous encoded word and this one
            if (lastWasEncodedWord)
            {
                ReadOnlySpan<char> between = unfolded.AsSpan(index, start - index);
                if (!IsAllWhitespace(between))
                {
                    sb.Append(between);
                }
            }
            else
            {
                sb.Append(unfolded.AsSpan(index, start - index));
            }

            // Find closing '?='
            int firstQuestion = unfolded.IndexOf('?', start + 2);
            if (firstQuestion < 0)
            {
                sb.Append(unfolded.AsSpan(start));
                break;
            }

            int secondQuestion = unfolded.IndexOf('?', firstQuestion + 1);
            if (secondQuestion < 0)
            {
                sb.Append(unfolded.AsSpan(start));
                break;
            }

            int end = unfolded.IndexOf("?=", secondQuestion + 1, StringComparison.Ordinal);
            if (end < 0)
            {
                sb.Append(unfolded.AsSpan(start));
                break;
            }

            string charset = unfolded[(start + 2)..firstQuestion];
            string encType = unfolded[(firstQuestion + 1)..secondQuestion];
            string encodedText = unfolded[(secondQuestion + 1)..end];

            if (TryDecodeWord(charset, encType, encodedText, out string? decoded))
            {
                sb.Append(decoded);
                lastWasEncodedWord = true;
                index = end + 2;
            }
            else
            {
                // Failed to decode as RFC 2047 word: pass original through
                sb.Append(unfolded.AsSpan(start, end + 2 - start));
                lastWasEncodedWord = false;
                index = end + 2;
            }
        }

        return sb.ToString();
    }

    private static bool TryDecodeWord(string charset, string encType, string text, out string? result)
    {
        result = null;
        Encoding encoding = ResolveEncoding(charset);

        byte[] bytes;
        if (encType.Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            bytes = Base64Decoder.Decode(text);
        }
        else if (encType.Equals("Q", StringComparison.OrdinalIgnoreCase))
        {
            bytes = QuotedPrintable.DecodeQ(text);
        }
        else
        {
            return false;
        }

        try
        {
            result = encoding.GetString(bytes);
            return true;
        }
        catch
        {
            result = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }

    public static Encoding ResolveEncoding(string charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        string clean = charset.Trim().Trim('\'', '"');

        try
        {
            return Encoding.GetEncoding(clean);
        }
        catch
        {
            // Common aliases
            string lower = clean.ToLowerInvariant();
            if (lower is "utf8" or "utf-8") return Encoding.UTF8;
            if (lower is "iso-8859-1" or "latin1" or "latin-1") return Encoding.Latin1;
            if (lower is "windows-1252" or "cp1252")
            {
                try { return Encoding.GetEncoding(1252); } catch { return Encoding.Latin1; }
            }
            return Encoding.UTF8;
        }
    }

    private static string Unfold(string header)
    {
        var sb = new StringBuilder(header.Length);
        for (int i = 0; i < header.Length; i++)
        {
            char c = header[i];
            if (c == '\r' && i + 1 < header.Length && header[i + 1] == '\n')
            {
                if (i + 2 < header.Length && (header[i + 2] == ' ' || header[i + 2] == '\t'))
                {
                    // Unfold: skip \r\n and next whitespace becomes single space
                    i += 1;
                    continue;
                }
            }
            else if (c == '\n' && i + 1 < header.Length && (header[i + 1] == ' ' || header[i + 1] == '\t'))
            {
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsAllWhitespace(ReadOnlySpan<char> span)
    {
        foreach (char c in span)
        {
            if (!char.IsWhiteSpace(c)) return false;
        }
        return true;
    }
}
