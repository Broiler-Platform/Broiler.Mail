// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Text;

namespace Broiler.Mail.Infrastructure.Mime.Encodings;

/// <summary>
/// Robust Base64 decoder for MIME payloads (RFC 2045) and RFC 2047 'B' words.
/// Tolerates internal line breaks, arbitrary whitespace, and missing padding.
/// </summary>
public static class Base64Decoder
{
    public static byte[] Decode(ReadOnlySpan<byte> utf8Input)
    {
        // Strip whitespace into a clean buffer
        int maxLen = utf8Input.Length;
        byte[] clean = new byte[maxLen];
        int cleanLen = 0;
        foreach (byte b in utf8Input)
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or
                     (>= (byte)'a' and <= (byte)'z') or
                     (>= (byte)'0' and <= (byte)'9') or
                     (byte)'+' or (byte)'/' or (byte)'=')
            {
                clean[cleanLen++] = b;
            }
        }

        if (cleanLen == 0) return [];

        // Pad if needed
        int remainder = cleanLen % 4;
        if (remainder != 0)
        {
            int needed = 4 - remainder;
            Array.Resize(ref clean, cleanLen + needed);
            for (int i = 0; i < needed; i++)
            {
                clean[cleanLen++] = (byte)'=';
            }
        }

        byte[] dest = new byte[cleanLen * 3 / 4];
        if (Base64.DecodeFromUtf8(clean.AsSpan(0, cleanLen), dest, out _, out int bytesWritten) == System.Buffers.OperationStatus.Done)
        {
            if (bytesWritten == dest.Length) return dest;
            return dest[..bytesWritten];
        }

        // Fallback via Convert
        try
        {
            string s = Encoding.ASCII.GetString(clean, 0, cleanLen);
            return Convert.FromBase64String(s);
        }
        catch
        {
            return [];
        }
    }

    public static byte[] Decode(string base64String)
    {
        if (string.IsNullOrWhiteSpace(base64String)) return [];
        var sb = new StringBuilder(base64String.Length);
        foreach (char c in base64String)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '=')
            {
                sb.Append(c);
            }
        }

        int remainder = sb.Length % 4;
        if (remainder != 0)
        {
            sb.Append('=', 4 - remainder);
        }

        try
        {
            return Convert.FromBase64String(sb.ToString());
        }
        catch
        {
            return [];
        }
    }
}
