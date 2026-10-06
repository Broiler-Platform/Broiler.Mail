// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   9
// Annotated:        9/9
// Exempt:           0
// Human-reviewed:   0/9
// IP risk:          Low
// Security risk:    High
// Criteria:         8/7
// Resource impact:  8/10 max
// Unverified:       9
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Net;
using System.Text;
using Broiler.Mail.Core.Diagnostics;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Html;

namespace Broiler.Mail.Infrastructure.Preview;

/// <summary>Reduces mail to passive markup. The native sandbox and deny-all resource policy remain required.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=824183
// Broiler-Falsified-If: an attribute taken from the message other than a validated http or https link or image source, such as style, background or an on* handler, appears in the preview markup
// Broiler-Human:        PENDING
public static class HtmlPreviewPolicy
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=F7A59A
    // Broiler-Falsified-If: HTML between 128,001 and 1,280,000 characters is previewed although the component tells the user its preview limit is 128,000 characters
    // Broiler-Human:        PENDING
    public const int MaximumHtmlCharacters = 128_000 * 10;
    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=88BD38
    // Broiler-Falsified-If: the set contains a tag that loads a resource, submits a form or embeds active content, such as form, input, link, base, meta, video or object
    // Broiler-Human:        PENDING
    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
    { "p", "div", "span", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "b", "em", "i", "u", "s", "small", "sub", "sup", "blockquote", "pre", "code", "ul", "ol", "li", "dl", "dt", "dd", "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "a" };
    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=1; Fingerprint=5B8650
    // Broiler-Falsified-If: an <embed> element, which HTML never closes, suppresses every later token so the rest of the message is missing from the preview
    // Broiler-Human:        PENDING
    private static readonly HashSet<string> SuppressedTags = new(StringComparer.Ordinal)
    { "head", "script", "style", "iframe", "object", "embed", "svg", "math", "template", "noscript", "textarea", "xmp", "plaintext" };

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=8; Fingerprint=B34788
    // Broiler-Falsified-If: the one-argument overload emits an http or https img element instead of the blocked-image placeholder
    // Broiler-Human:        PENDING
    public static HtmlPreviewDocument Create(string html) =>
        Create(html, embeddedImages: null, allowRemoteImages: false);

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=39183E
    // Broiler-Falsified-If: one cid: image referenced by many img tags is inlined as a full base64 copy each time, so a message within the 2 MiB and 20,000-token limits produces gigabytes of markup
    // Broiler-Human:        PENDING
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
                    var width = tag.Attributes.FirstOrDefault(a => a.Name.Equals("width", StringComparison.OrdinalIgnoreCase))?.Value;
                    var height = tag.Attributes.FirstOrDefault(a => a.Name.Equals("height", StringComparison.OrdinalIgnoreCase))?.Value;
                    string? safeAlt = alt is { Length: > 0 and <= 256 } && !alt.Any(char.IsControl) ? WebUtility.HtmlEncode(alt) : null;
                    string? safeWidth = GetSafeDimension(width);
                    string? safeHeight = GetSafeDimension(height);
                    if (src is not null && src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                    {
                        string cid = src[4..].Trim().Trim('<', '>', '"', '\'');
                        string unescapedCid = Uri.UnescapeDataString(cid);
                        MailEmbeddedImage? image = null;
                        if (embeddedImages is not null &&
                            (embeddedImages.TryGetValue(cid, out image) ||
                             embeddedImages.TryGetValue(unescapedCid, out image)))
                        {
                            hasEmbeddedImages = true;
                            AppendImageTag(output, $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Data)}", safeAlt, safeWidth, safeHeight);
                        }
                    }
                    else if (src is not null && TryValidateSafeDataImageUri(src, out string? safeDataUri))
                    {
                        hasEmbeddedImages = true;
                        AppendImageTag(output, safeDataUri!, safeAlt, safeWidth, safeHeight);
                    }
                    else if (TryExternalLink(src, out var uri))
                    {
                        remoteImages.Add(uri!.AbsoluteUri);
                        if (allowRemoteImages)
                        {
                            MailEmbeddedImage? downloaded = null;
                            if (embeddedImages is not null &&
                                (embeddedImages.TryGetValue(uri.AbsoluteUri, out downloaded) ||
                                 (src is not null && embeddedImages.TryGetValue(src, out downloaded))))
                            {
                                hasEmbeddedImages = true;
                                AppendImageTag(output, $"data:{downloaded.ContentType};base64,{Convert.ToBase64String(downloaded.Data)}", safeAlt, safeWidth, safeHeight);
                            }
                            else
                            {
                                AppendImageTag(output, WebUtility.HtmlEncode(uri.AbsoluteUri), safeAlt, safeWidth, safeHeight);
                            }
                        }
                        else
                        {
                            output.Append("<span style=\"color:#5f6368;font-size:0.9em;border:1px dashed #ccc;padding:2px 6px;border-radius:3px;\">[Remote image blocked")
                                .Append(safeAlt is not null ? $": {safeAlt}" : "")
                                .Append("]</span>");
                        }
                    }
                    else if (src is not null && embeddedImages is not null &&
                             (embeddedImages.TryGetValue(src, out var relImage) ||
                              embeddedImages.TryGetValue(src.TrimStart('.', '/'), out relImage)))
                    {
                        hasEmbeddedImages = true;
                        AppendImageTag(output, $"data:{relImage.ContentType};base64,{Convert.ToBase64String(relImage.Data)}", safeAlt, safeWidth, safeHeight);
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
        MailLogger.Debug("HtmlPolicy", $"Sanitized preview: Links={links.Count}, RemoteImages={remoteImages.Count}, HasEmbeddedImages={hasEmbeddedImages}, OutputLength={output.Length}");
        return new(output.ToString(), links, remoteImages, hasEmbeddedImages);
    }

    private static readonly HashSet<string> AllowedDataImageSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpeg", "jpg", "gif", "webp", "bmp", "x-png", "pjpeg", "x-ms-bmp"
    };

    /// <summary>
    /// Validates and normalizes an inline data: URI image. Ensures safe raster formats only (no SVG or active content),
    /// strictly prevents attribute breakout by rejecting quotes and tags, and canonicalizes the base64 content.
    /// </summary>
    public static bool TryValidateSafeDataImageUri(string? dataUri, out string? safeUri)
    {
        safeUri = null;
        if (string.IsNullOrWhiteSpace(dataUri)) return false;
        if (dataUri.Length > 10_000_000) return false;

        if (!dataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return false;

        int semicolonIndex = dataUri.IndexOf(';');
        if (semicolonIndex <= 11) return false;

        string subtype = dataUri.Substring(11, semicolonIndex - 11).Trim().ToLowerInvariant();
        if (!AllowedDataImageSubtypes.Contains(subtype)) return false;

        int commaIndex = dataUri.IndexOf(',', semicolonIndex);
        if (commaIndex < 0) return false;

        string encodingPart = dataUri.Substring(semicolonIndex + 1, commaIndex - semicolonIndex - 1).Trim();
        if (!encodingPart.Equals("base64", StringComparison.OrdinalIgnoreCase)) return false;

        string base64Data = dataUri.Substring(commaIndex + 1).Trim();
        if (base64Data.Length == 0) return false;

        foreach (char c in base64Data)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '=')
            {
                continue;
            }
            return false;
        }

        if (subtype is "jpg" or "pjpeg") subtype = "jpeg";
        else if (subtype is "x-png") subtype = "png";
        else if (subtype is "x-ms-bmp") subtype = "bmp";

        string cleanBase64 = base64Data.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "");
        safeUri = $"data:image/{subtype};base64,{cleanBase64}";
        return true;
    }

    private static string? GetSafeDimension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16) return null;
        value = value.Trim();
        bool allDigits = true;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsDigit(c)) continue;
            if ((c == 'p' || c == 'P') && i + 1 < value.Length && (value[i + 1] == 'x' || value[i + 1] == 'X') && i + 2 == value.Length)
            {
                return value;
            }
            if (c == '%' && i + 1 == value.Length)
            {
                return value;
            }
            allDigits = false;
            break;
        }
        return allDigits ? value : null;
    }

    private static void AppendImageTag(StringBuilder output, string src, string? safeAlt, string? safeWidth, string? safeHeight)
    {
        output.Append("<img src=\"").Append(src).Append('\"');
        if (safeAlt is not null) output.Append(" alt=\"").Append(safeAlt).Append('\"');
        if (safeWidth is not null) output.Append(" width=\"").Append(safeWidth).Append('\"');
        if (safeHeight is not null) output.Append(" height=\"").Append(safeHeight).Append('\"');
        output.Append(" style=\"max-width:100%;height:auto\">");
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=2; Fingerprint=6D2504
    // Broiler-Falsified-If: a value whose scheme is not http or https, or that carries userinfo or a control character, returns true
    // Broiler-Human:        PENDING
    public static bool TryExternalLink(string? value, out Uri? uri)
    {
        uri = null;
        return value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl)
            && Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https"
            && uri.UserInfo.Length == 0 && !string.IsNullOrEmpty(uri.Host);
    }
}

// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=30D8CA
// Broiler-Human:        PENDING
public sealed record HtmlPreviewDocument(
    string Html,
    IReadOnlySet<string> ExternalLinks,
    IReadOnlySet<string> RemoteImageUrls,
    bool HasEmbeddedImages = false)
{
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=7F3689
    // Broiler-Falsified-If: a document built with the two-argument constructor reports remote image URLs or embedded images
    // Broiler-Human:        PENDING
    public HtmlPreviewDocument(string html, IReadOnlySet<string> externalLinks)
        : this(html, externalLinks, new HashSet<string>(StringComparer.Ordinal), false) { }
}
