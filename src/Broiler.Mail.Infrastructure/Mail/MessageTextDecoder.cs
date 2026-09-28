using System.Text;
using Broiler.Mail.Core.Messages;
using MimeKit;
using MimeKit.Text;

namespace Broiler.Mail.Infrastructure.Mail;

/// <summary>Produces bounded display text only; no HTML renderer or resource loader is involved.</summary>
internal static class MessageTextDecoder
{
    internal const int MaximumTextCharacters = 32_000;

    public static async Task<MailMessageBody> DecodeAsync(MailMessageKey key, Stream stream, CancellationToken token)
    {
        using var message = await MimeMessage.LoadAsync(new ParserOptions { MaxMimeDepth = 32 }, stream, token).ConfigureAwait(false);
        string? text = message.TextBody;
        bool fallback = false;
        if (text is null && message.HtmlBody is { } html)
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
        return new(key, string.IsNullOrWhiteSpace(text) ? "(No readable text in this message.)" : text)
        { IsHtmlFallback = fallback, IsTruncated = truncated };
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
