// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        0/4
// Exempt:           0
// Human-reviewed:   0/4
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Infrastructure.Mime.Encodings;

/// <summary>Exact transfer-decoded length of a MIME body without decoding or allocating it.
/// Mirrors <see cref="Base64Decoder.Decode(ReadOnlySpan{byte})"/> and <see cref="QuotedPrintable.Decode"/>.</summary>
internal static class MimeTransferLength
{
    internal static int Decoded(ReadOnlySpan<byte> bytes, string? transferEncoding) =>
        transferEncoding switch
        {
            "base64" => Base64Length(bytes),
            "quoted-printable" => QuotedPrintableLength(bytes),
            _ => bytes.Length,
        };

    private static int Base64Length(ReadOnlySpan<byte> bytes)
    {
        // Base64Decoder keeps only alphabet bytes and '=', pads to a multiple of four and decodes;
        // any invalid layout (data after '=', too much padding, one dangling character) yields no bytes.
        long data = 0, padding = 0;
        foreach (byte value in bytes)
        {
            if (value == (byte)'=') { padding++; continue; }
            if (value is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'+' or (byte)'/')
            {
                if (padding > 0) return 0;
                data++;
            }
        }
        long clean = data + padding;
        if (clean == 0) return 0;
        long padded = (clean + 3) / 4 * 4;
        long totalPadding = padded - data;
        if (totalPadding > 2) return 0;
        long length = data / 4 * 3 + (data % 4) switch { 2 => 1, 3 => 2, _ => 0 };
        return length > int.MaxValue ? int.MaxValue : (int)length;
    }

    private static int QuotedPrintableLength(ReadOnlySpan<byte> input)
    {
        int length = 0, index = 0;
        while (index < input.Length)
        {
            if (input[index] == (byte)'=')
            {
                if (index + 2 < input.Length && input[index + 1] == (byte)'\r' && input[index + 2] == (byte)'\n') { index += 3; continue; }
                if (index + 1 < input.Length && (input[index + 1] == (byte)'\n' || input[index + 1] == (byte)'\r')) { index += 2; continue; }
                if (index + 2 < input.Length && Uri.IsHexDigit((char)input[index + 1]) && Uri.IsHexDigit((char)input[index + 2]))
                {
                    length++; index += 3; continue;
                }
            }
            length++; index++;
        }
        return length;
    }
}
