// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   10
// Annotated:        9/10
// Exempt:           0
// Human-reviewed:   0/10
// IP risk:          Low
// Security risk:    High
// Criteria:         9/9
// Resource impact:  7/10 max
// Unverified:       10
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Text;
using Broiler.Mail.Core.Diagnostics;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Html;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Preview;

namespace Broiler.Mail.Infrastructure.Mail;

/// <summary>Decodes bounded text and untrusted HTML; no renderer or resource loader is involved.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=High; Resources=7; Fingerprint=B447E4
// Broiler-Falsified-If: a decoded body exceeds one of its caps: more than 32,000 text characters, more than 16 images, an image over 1 MiB or more than 2 MiB of images in total
// Broiler-Human:        PENDING
internal static class MessageTextDecoder
{
    static MessageTextDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=High; Resources=0; Fingerprint=0ECAA8
    // Broiler-Falsified-If: a text body longer than 32,000 characters reaches the reading pane in full instead of being cut at this limit and marked truncated
    // Broiler-Human:        PENDING
    internal const int MaximumTextCharacters = 32_000;

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=High; Resources=7; Fingerprint=5A61BB
    // Broiler-Falsified-If: a message whose multiparts nest more than 32 levels deep is parsed into entities below that depth instead of stopping at the parser's depth limit
    // Broiler-Human:        PENDING
    public static async Task<MailMessageBody> DecodeAsync(MailMessageKey key, Stream stream, CancellationToken token)
    {
        var message = await MimeParser.ParseAsync(stream, token).ConfigureAwait(false);
        string? text = message.TextBody;
        string? htmlBody = message.HtmlBody;
        bool fallback = false;
        if (text is null && htmlBody is { } html)
        {
            fallback = true;
            text = ExtractHtmlText(html, token);
        }
        token.ThrowIfCancellationRequested();
        text = Clean(text ?? "", MaximumTextCharacters + 1, multiline: true);
        bool truncated = text.Length > MaximumTextCharacters;
        if (truncated)
        {
            int length = MaximumTextCharacters;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            text = text[..length];
        }
        MailCompositionSource? composition = null;
        string? compositionError = null;
        try { composition = CompositionHeaders(message); }
        catch (ArgumentException) { compositionError = "Reply headers are invalid or exceed the composition limits. Start a new message instead."; }
        return new(key, string.IsNullOrWhiteSpace(text) ? "(No readable text in this message.)" : text,
            htmlBody is { Length: <= HtmlPreviewPolicy.MaximumHtmlCharacters } ? htmlBody : null)
        {
            HtmlUnavailableReason = htmlBody?.Length > HtmlPreviewPolicy.MaximumHtmlCharacters ? "HTML exceeds the 128,000-character preview limit. The text preview remains available." : null,
            IsHtmlFallback = fallback, IsTruncated = truncated,
            Composition = composition, CompositionUnavailableReason = compositionError,
            EmbeddedImages = message.EmbeddedImages,
        };
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=5BC1DA
    // Broiler-Falsified-If: a received Message-ID, References or In-Reply-To value containing CR or LF is copied into the reply composition source
    // Broiler-Human:        PENDING
    private static MailCompositionSource CompositionHeaders(ParsedMimeMessage message)
    {
        static string Safe(string value, int maximum)
        {
            if (value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException();
            return value;
        }
        static string[] Addresses(IEnumerable<string> list)
        {
            var addresses = list.Take(MailComposition.MaximumRecipients + 1)
                .Select(address => Safe(address, 320)).ToArray();
            if (addresses.Length > MailComposition.MaximumRecipients) throw new ArgumentException();
            // The composer uses canonical mailbox addresses, not a truncated display-name string.
            foreach (string address in addresses)
                if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
                    throw new ArgumentException();
            return addresses;
        }
        static string[] Ids(IEnumerable<string> values)
        {
            var ids = values.Take(MailComposition.MaximumReferences + 1).Select(value => Safe(value, 998)).ToArray();
            if (ids.Length > MailComposition.MaximumReferences) throw new ArgumentException();
            return ids;
        }
        return new()
        {
            Subject = Safe(message.Subject ?? "", MailComposition.MaximumSubjectLength),
            From = Addresses(message.From), ReplyTo = Addresses(message.ReplyTo), To = Addresses(message.To), Cc = Addresses(message.Cc),
            MessageId = message.MessageId is { } id ? Safe(id, 998) : null,
            References = Ids(message.References),
            InReplyTo = Ids(message.InReplyTo),
        };
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=666D03
    // Broiler-Falsified-If: an envelope subject or sender whose 512-character cut falls inside a surrogate pair is returned ending in a lone high surrogate
    // Broiler-Human:        PENDING
    internal static string Header(string? value, string missing)
    {
        string result = Clean(value ?? "", 512, multiline: false).Trim();
        return result.Length == 0 ? missing : result;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=50B579
    // Broiler-Falsified-If: in single-line mode a CR, LF, TAB or NUL from the input survives into the returned string
    // Broiler-Human:        PENDING
    private static string Clean(string text, int limit, bool multiline)
    {
        var result = new StringBuilder(Math.Min(text.Length, limit));
        for (int index = 0; index < text.Length; index++)
        {
            if (result.Length == limit) break;
            char c = text[index];
            if (multiline && c == '\r')
            {
                result.Append('\n');
                if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                continue;
            }
            result.Append(char.IsControl(c) && !(multiline && c is '\n' or '\t') ? ' ' : c);
        }
        return result.ToString();
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=High; Resources=4; Fingerprint=6153AD
    // Broiler-Falsified-If: an HTML-only message that omits the optional </head> end tag yields no readable text because everything after <head> stays suppressed
    // Broiler-Human:        PENDING
    private static string ExtractHtmlText(string html, CancellationToken token)
    {
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        var result = new StringBuilder();
        string? suppressed = null;
        while (result.Length <= MaximumTextCharacters && tokenizer.ReadNextToken(out var part))
        {
            token.ThrowIfCancellationRequested();
            if (part is HtmlTagToken tag)
            {
                string name = tag.Name.ToLowerInvariant();
                if (suppressed is not null)
                {
                    if (tag.IsEndTag && name == suppressed) suppressed = null;
                    continue;
                }
                if (!tag.IsEndTag && name is "head" or "script" or "style" or "iframe" or "object" or "template")
                {
                    suppressed = name;
                    continue;
                }
                if (name is "br" or "p" or "div" or "li" or "tr" or "table" or "blockquote" or "pre" or "h1" or "h2" or "h3" or "hr")
                {
                    if (result.Length > 0 && result[^1] != '\n') result.Append('\n');
                }
                else if (name is "td" or "th") result.Append(' ');
            }
            else if (suppressed is null && part is HtmlDataToken data)
            {
                foreach (char c in data.Data)
                {
                    if (result.Length > MaximumTextCharacters) break;
                    if (char.IsWhiteSpace(c))
                    {
                        if (result.Length > 0 && !char.IsWhiteSpace(result[^1])) result.Append(' ');
                    }
                    else result.Append(c);
                }
            }
        }
        return result.ToString().Trim();
    }
}
