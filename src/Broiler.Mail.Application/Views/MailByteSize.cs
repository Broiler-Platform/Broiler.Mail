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

using System.Globalization;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// The one byte-size formatter for attachments and quotas, in binary units and invariant culture: "N bytes" below 1 KiB,
/// "N KiB" (rounded up) below 1 MiB, otherwise "x.y MiB" (rounded up to a tenth). Rounding up means a size never reads
/// smaller than it is, so a total just over a limit never reads as equal to it.
/// </summary>
internal static class MailByteSize
{
    private const long KiB = 1024, MiB = 1024 * 1024;

    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes < KiB) return bytes == 1 ? "1 byte" : bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        long kibibytes = (bytes + KiB - 1) / KiB;
        if (kibibytes < KiB) return kibibytes.ToString(CultureInfo.InvariantCulture) + " KiB";
        return Mebibytes(bytes) + " MiB";
    }

    /// <summary>The size in MiB with one decimal, rounded up to a tenth ("8.0" for exactly 8 MiB).</summary>
    public static string Mebibytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        long tenths = bytes / MiB * 10 + ((bytes % MiB) * 10 + MiB - 1) / MiB;
        return (tenths / 10).ToString(CultureInfo.InvariantCulture) + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture);
    }
}
