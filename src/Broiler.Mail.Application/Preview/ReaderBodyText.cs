// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   8
// Annotated:        0/8
// Exempt:           0
// Human-reviewed:   0/8
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;
using System.Text;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Application.Preview;

/// <summary>
/// The text the reader shows for one decoded body: the bounded 32,000-character preview, or, once the reader
/// asks for it, the complete retained text the cache or the receiver already holds in memory. Find searches
/// exactly the text shown, without the HTML-fallback prefix.
/// </summary>
public static class ReaderBodyText
{
    public const string HtmlFallbackPrefix = "Text extracted from HTML (formatting omitted).\n\n";
    public const string TruncationNote = "\n\n[Preview limited to 32,000 characters.]";

    /// <summary>True when the preview was cut and complete retained text longer than it is available.</summary>
    public static bool CanExpand(MailMessageBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.IsTruncated && body.RetainedBodyText is { Length: > 0 } retained &&
            retained.Length > body.PlainText.Length && retained.Length <= MailMessageBody.MaximumRetainedTextCharacters;
    }

    /// <summary>The reader string; <paramref name="full"/> applies only where <see cref="CanExpand"/> holds.</summary>
    public static string Display(MailMessageBody body, bool full)
    {
        ArgumentNullException.ThrowIfNull(body);
        bool expanded = full && CanExpand(body);
        return (body.IsHtmlFallback ? HtmlFallbackPrefix : "") + (expanded ? body.RetainedBodyText : body.PlainText) +
            (!expanded && body.IsTruncated ? TruncationNote : "");
    }

    /// <summary>The searched text: the shown body text with line breaks normalized as the reader shows them.</summary>
    public static string FindText(MailMessageBody body, bool full)
    {
        ArgumentNullException.ThrowIfNull(body);
        string value = full && CanExpand(body) ? body.RetainedBodyText! : body.PlainText;
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>Where the body text starts in the reader string.</summary>
    public static int FindOffset(MailMessageBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.IsHtmlFallback ? HtmlFallbackPrefix.Length : 0;
    }

    /// <summary>"Show full message text (N KB)", N being the retained text's UTF-8 size rounded up.</summary>
    public static string ExpandLabel(MailMessageBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        string retained = body.RetainedBodyText ?? "";
        double kilobytes = Math.Ceiling(Encoding.UTF8.GetByteCount(retained) / 1024.0);
        return "Show full message text (" + kilobytes.ToString("N0", CultureInfo.InvariantCulture) + " KB)";
    }
}
