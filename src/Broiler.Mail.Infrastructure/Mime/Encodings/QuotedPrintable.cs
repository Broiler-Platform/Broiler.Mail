// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Broiler.Mail.Infrastructure.Mime.Encodings;

/// <summary>
/// Quoted-Printable transfer encoding and RFC 2047 'Q' encoding decoder and encoder.
/// Standalone, zero-allocation-focused, and trimmer-safe.
/// </summary>
public static class QuotedPrintable
{
    /// <summary>
    /// Decodes Quoted-Printable bytes into raw unencoded bytes (RFC 2045).
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> input)
    {
        var output = new MemoryStream(input.Length);
        int index = 0;
        while (index < input.Length)
        {
            byte b = input[index];
            if (b == (byte)'=')
            {
                // Soft line break: =\r\n, =\n, or =\r
                if (index + 2 < input.Length && input[index + 1] == (byte)'\r' && input[index + 2] == (byte)'\n')
                {
                    index += 3;
                    continue;
                }
                if (index + 1 < input.Length && (input[index + 1] == (byte)'\n' || input[index + 1] == (byte)'\r'))
                {
                    index += 2;
                    continue;
                }

                // Hex byte: =HH
                if (index + 2 < input.Length &&
                    IsHex(input[index + 1], out int h1) &&
                    IsHex(input[index + 2], out int h2))
                {
                    output.WriteByte((byte)((h1 << 4) | h2));
                    index += 3;
                    continue;
                }

                // Invalid escape: pass through '='
                output.WriteByte(b);
                index++;
            }
            else
            {
                output.WriteByte(b);
                index++;
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// Decodes an RFC 2047 'Q' encoded-word string into raw bytes.
    /// In 'Q' encoding, '_' represents space (0x20).
    /// </summary>
    public static byte[] DecodeQ(ReadOnlySpan<char> input)
    {
        var output = new MemoryStream(input.Length);
        int index = 0;
        while (index < input.Length)
        {
            char c = input[index];
            if (c == '_')
            {
                output.WriteByte(0x20); // space
                index++;
            }
            else if (c == '=' && index + 2 < input.Length &&
                     IsHexChar(input[index + 1], out int h1) &&
                     IsHexChar(input[index + 2], out int h2))
            {
                output.WriteByte((byte)((h1 << 4) | h2));
                index += 3;
            }
            else
            {
                output.WriteByte((byte)c);
                index++;
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// Encodes a string as RFC 2047 'Q' encoding if necessary, or returns the original string if all ASCII.
    /// </summary>
    public static string EncodeQWord(string text, string charset = "utf-8")
    {
        if (string.IsNullOrEmpty(text)) return text;
        bool hasNonAscii = false;
        foreach (char c in text)
        {
            if (c is < ' ' or > '~' or '?' or '=' or '_')
            {
                hasNonAscii = true;
                break;
            }
        }
        if (!hasNonAscii) return text;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var sb = new StringBuilder();
        sb.Append($"=?{charset}?Q?");
        foreach (byte b in bytes)
        {
            if (b == 0x20)
            {
                sb.Append('_');
            }
            else if (b is (>= (byte)'A' and <= (byte)'Z') or
                         (>= (byte)'a' and <= (byte)'z') or
                         (>= (byte)'0' and <= (byte)'9') or
                         (byte)'!' or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'/')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('=');
                sb.Append(b.ToString("X2"));
            }
        }
        sb.Append("?=");
        return sb.ToString();
    }

    private static bool IsHex(byte b, out int value)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            value = b - '0';
            return true;
        }
        if (b is >= (byte)'A' and <= (byte)'F')
        {
            value = b - 'A' + 10;
            return true;
        }
        if (b is >= (byte)'a' and <= (byte)'f')
        {
            value = b - 'a' + 10;
            return true;
        }
        value = 0;
        return false;
    }

    private static bool IsHexChar(char c, out int value)
    {
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
            return true;
        }
        if (c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }
        if (c is >= 'a' and <= 'f')
        {
            value = c - 'a' + 10;
            return true;
        }
        value = 0;
        return false;
    }
}
