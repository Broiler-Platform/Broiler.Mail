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

using System.Text;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.Preview;

public sealed record OriginalSourceTextPreview(string Text, int BytesShown, int TotalBytes, bool IsTruncated);

/// <summary>Bounded ASCII byte view, not a MIME/HTML decoder. The exported octets remain unchanged.</summary>
public static class OriginalSourceText
{
    public const int MaximumPreviewBytes = 64 * 1024;
    public static OriginalSourceTextPreview Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > IStreamingMessageSourceExports.MaximumBytes) throw new ArgumentException("Source exceeds the 12 MiB inspection limit.");
        int count = Math.Min(bytes.Length, MaximumPreviewBytes); var text = new StringBuilder(count);
        for (int index = 0; index < count; index++)
        {
            byte value = bytes[index];
            if (value == 13) { text.Append('\n'); if (index + 1 < count && bytes[index + 1] == 10) index++; }
            else if (value == 10 || value == 9 || value is >= 32 and <= 126 && value != 92) text.Append((char)value);
            else if (value == 92) text.Append("\\\\");
            else text.Append("\\x").Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return new(text.ToString(), count, bytes.Length, bytes.Length > count);
    }
}
