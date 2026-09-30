using System.Text;
using Broiler.Mail.Core.Messages;
using MimeKit;
using MimeKit.Text;
using MimeKit.Utils;
using Broiler.Mail.Infrastructure.Preview;

namespace Broiler.Mail.Infrastructure.Mail;

/// <summary>Decodes bounded text and untrusted HTML; no renderer or resource loader is involved.</summary>
internal static class MessageTextDecoder
{
    internal const int MaximumTextCharacters = 32_000;

    public static async Task<MailMessageBody> DecodeAsync(MailMessageKey key, Stream stream, CancellationToken token)
    {
        using var message = await MimeMessage.LoadAsync(new ParserOptions { MaxMimeDepth = 32 }, stream, token).ConfigureAwait(false);
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
        var embeddedImages = ExtractEmbeddedImages(message);
        return new(key, string.IsNullOrWhiteSpace(text) ? "(No readable text in this message.)" : text,
            htmlBody is { Length: <= HtmlPreviewPolicy.MaximumHtmlCharacters } ? htmlBody : null)
        {
            HtmlUnavailableReason = htmlBody?.Length > HtmlPreviewPolicy.MaximumHtmlCharacters ? "HTML exceeds the 128,000-character preview limit. The text preview remains available." : null,
            IsHtmlFallback = fallback, IsTruncated = truncated,
            Composition = composition, CompositionUnavailableReason = compositionError,
            EmbeddedImages = embeddedImages,
        };
    }

    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp"
    };

    private static Dictionary<string, MailEmbeddedImage> ExtractEmbeddedImages(MimeMessage message)
    {
        var result = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase);
        int totalBytes = 0;
        foreach (var entity in message.BodyParts)
        {
            if (result.Count >= MailEmbeddedImage.MaximumImageCount) break;
            if (entity is not MimePart part) continue;
            string? contentId = part.ContentId;
            if (string.IsNullOrWhiteSpace(contentId)) continue;
            string mediaType = part.ContentType.MimeType;
            if (!SupportedImageTypes.Contains(mediaType)) continue;

            string normalizedCid = contentId.Trim().Trim('<', '>');
            if (normalizedCid.Length == 0 || normalizedCid.Length > 256 || normalizedCid.Any(char.IsControl)) continue;
            if (result.ContainsKey(normalizedCid)) continue;

            if (part.Content is null) continue;
            using var memory = new MemoryStream();
            part.Content.DecodeTo(memory);
            byte[] bytes = memory.ToArray();
            if (bytes.Length == 0 || bytes.Length > MailEmbeddedImage.MaximumImageBytes) continue;
            if (totalBytes + bytes.Length > MailEmbeddedImage.MaximumTotalBytes) break;

            totalBytes += bytes.Length;
            result[normalizedCid] = new MailEmbeddedImage(normalizedCid, mediaType.ToLowerInvariant(), bytes);
        }
        return result;
    }

    private static MailCompositionSource CompositionHeaders(MimeMessage message)
    {
        static string Safe(string value, int maximum)
        {
            if (value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException();
            return value;
        }
        static string[] Addresses(InternetAddressList list)
        {
            var addresses = list.Mailboxes.Take(MailComposition.MaximumRecipients + 1)
                .Select(mailbox => Safe(mailbox.Address, 320)).ToArray();
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
            InReplyTo = Ids(message.Headers.Where(header => header.Id == HeaderId.InReplyTo)
                .SelectMany(header => MimeUtils.EnumerateReferences(header.Value))),
        };
    }

    internal static string Header(string? value, string missing)
    {
        string result = Clean(value ?? "", 512, multiline: false).Trim();
        return result.Length == 0 ? missing : result;
    }

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
