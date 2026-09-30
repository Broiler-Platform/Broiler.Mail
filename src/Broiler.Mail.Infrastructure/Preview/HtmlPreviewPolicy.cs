using System.Net;
using System.Text;
using Broiler.Mail.Core.Messages;
using MimeKit.Text;

namespace Broiler.Mail.Infrastructure.Preview;

/// <summary>Reduces mail to passive markup. The native sandbox and deny-all resource policy remain required.</summary>
public static class HtmlPreviewPolicy
{
    public const int MaximumHtmlCharacters = 128_000 * 10;
    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
    { "p", "div", "span", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "b", "em", "i", "u", "s", "small", "sub", "sup", "blockquote", "pre", "code", "ul", "ol", "li", "dl", "dt", "dd", "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "a" };
    private static readonly HashSet<string> SuppressedTags = new(StringComparer.Ordinal)
    { "head", "script", "style", "iframe", "object", "embed", "svg", "math", "template", "noscript", "textarea", "xmp", "plaintext" };

    public static HtmlPreviewDocument Create(string html) =>
        Create(html, embeddedImages: null, allowRemoteImages: false);

    public static HtmlPreviewDocument Create(
        string html,
        IReadOnlyDictionary<string, MailEmbeddedImage>? embeddedImages,
        bool allowRemoteImages = false)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Length > MaximumHtmlCharacters) throw new ArgumentException("HTML exceeds the preview limit.");
        string imageCsp = allowRemoteImages ? "img-src 'self' data: https: http:;" : "img-src 'self' data:;";
        var output = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; ")
            .Append(imageCsp)
            .Append(" font-src 'none'; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; child-src 'none'; worker-src 'none'; form-action 'none'; base-uri 'none'\"><meta name=\"referrer\" content=\"no-referrer\"><style>body{font:16px system-ui,sans-serif;line-height:1.5;margin:24px;overflow-wrap:anywhere;color:#202124;background:white}img{max-width:100%;height:auto;vertical-align:middle}table{border-collapse:collapse;max-width:100%}td,th{border:1px solid #ccc;padding:6px}blockquote{border-left:3px solid #aaa;padding-left:12px}pre{white-space:pre-wrap}a{color:#185abc}</style></head><body>");
        var links = new HashSet<string>(StringComparer.Ordinal);
        var remoteImages = new HashSet<string>(StringComparer.Ordinal);
        bool hasEmbeddedImages = false;
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        string? suppressed = null;
        int tokens = 0;
        while (tokenizer.ReadNextToken(out var token))
        {
            if (++tokens > 20_000) throw new ArgumentException("HTML is too complex to preview.");
            if (token is HtmlTagToken tag)
            {
                string name = tag.Name.ToLowerInvariant();
                if (suppressed is not null)
                {
                    if (tag.IsEndTag && name == suppressed) suppressed = null;
                    continue;
                }
                if (!tag.IsEndTag && SuppressedTags.Contains(name)) { suppressed = name; continue; }
                if (name == "img" && !tag.IsEndTag)
                {
                    var src = tag.Attributes.FirstOrDefault(a => a.Name.Equals("src", StringComparison.OrdinalIgnoreCase))?.Value;
                    var alt = tag.Attributes.FirstOrDefault(a => a.Name.Equals("alt", StringComparison.OrdinalIgnoreCase))?.Value;
                    string? safeAlt = alt is { Length: > 0 and <= 256 } && !alt.Any(char.IsControl) ? WebUtility.HtmlEncode(alt) : null;
                    if (src is not null && src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                    {
                        string cid = src[4..].Trim().Trim('<', '>');
                        if (embeddedImages is not null && embeddedImages.TryGetValue(cid, out var image))
                        {
                            hasEmbeddedImages = true;
                            output.Append("<img src=\"data:").Append(image.ContentType).Append(";base64,")
                                .Append(Convert.ToBase64String(image.Data)).Append('\"');
                            if (safeAlt is not null) output.Append(" alt=\"").Append(safeAlt).Append('\"');
                            output.Append(" style=\"max-width:100%;height:auto\">");
                        }
                    }
                    else if (TryExternalLink(src, out var uri))
                    {
                        remoteImages.Add(uri!.AbsoluteUri);
                        if (allowRemoteImages)
                        {
                            output.Append("<img src=\"").Append(WebUtility.HtmlEncode(uri.AbsoluteUri)).Append('\"');
                            if (safeAlt is not null) output.Append(" alt=\"").Append(safeAlt).Append('\"');
                            output.Append(" style=\"max-width:100%;height:auto\">");
                        }
                        else
                        {
                            output.Append("<span style=\"color:#5f6368;font-size:0.9em;border:1px dashed #ccc;padding:2px 6px;border-radius:3px;\">[Remote image blocked")
                                .Append(safeAlt is not null ? $": {safeAlt}" : "")
                                .Append("]</span>");
                        }
                    }
                    continue;
                }

                if (!AllowedTags.Contains(name)) continue;
                output.Append('<');
                if (tag.IsEndTag) output.Append('/');
                output.Append(name);
                if (!tag.IsEndTag && name == "a")
                {
                    var href = tag.Attributes.FirstOrDefault(attribute => attribute.Name.Equals("href", StringComparison.OrdinalIgnoreCase))?.Value;
                    if (TryExternalLink(href, out var uri))
                    {
                        links.Add(uri!.AbsoluteUri);
                        output.Append(" href=\"").Append(WebUtility.HtmlEncode(uri.AbsoluteUri)).Append("\" rel=\"noreferrer noopener\"");
                    }
                }
                output.Append('>');
            }
            else if (suppressed is null && token is HtmlDataToken data) output.Append(WebUtility.HtmlEncode(data.Data));
        }
        output.Append("</body></html>");
        return new(output.ToString(), links, remoteImages, hasEmbeddedImages);
    }

    public static bool TryExternalLink(string? value, out Uri? uri)
    {
        uri = null;
        return value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl)
            && Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https"
            && uri.UserInfo.Length == 0 && !string.IsNullOrEmpty(uri.Host);
    }
}

public sealed record HtmlPreviewDocument(
    string Html,
    IReadOnlySet<string> ExternalLinks,
    IReadOnlySet<string> RemoteImageUrls,
    bool HasEmbeddedImages = false)
{
    public HtmlPreviewDocument(string html, IReadOnlySet<string> externalLinks)
        : this(html, externalLinks, new HashSet<string>(StringComparer.Ordinal), false) { }
}
