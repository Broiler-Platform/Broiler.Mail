// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Broiler.Mail.Core.Diagnostics;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Mime.Encodings;

namespace Broiler.Mail.Infrastructure.Mime;

/// <summary>
/// Streaming, zero-dependency RFC 5322 and MIME (RFC 2045-2047) parser.
/// Enforces bounded depth (max 32) and resource limits (max 16 images / 2 MiB total).
/// </summary>
public static class MimeParser
{
    static MimeParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const int MaxMimeDepth = 32;

    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/webp", "image/bmp", "image/x-png", "image/x-ms-bmp"
    };

    private static string NormalizeImageMediaType(string mediaType) =>
        mediaType.ToLowerInvariant() switch
        {
            "image/jpg" or "image/pjpeg" => "image/jpeg",
            "image/x-png" => "image/png",
            "image/x-ms-bmp" => "image/bmp",
            var other => other,
        };

    /// <summary>
    /// Parses an RFC 5322 / MIME stream into a <see cref="ParsedMimeMessage"/>.
    /// </summary>
    public static async Task<ParsedMimeMessage> ParseAsync(Stream stream, CancellationToken token = default)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, token).ConfigureAwait(false);
        return Parse(memory.ToArray());
    }

    /// <summary>
    /// Parses an RFC 5322 / MIME stream into a <see cref="ParsedMimeMessage"/>.
    /// </summary>
    public static ParsedMimeMessage Parse(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Parse(memory.ToArray());
    }

    /// <summary>
    /// Parses RFC 5322 / MIME bytes into a <see cref="ParsedMimeMessage"/>.
    /// </summary>
    public static ParsedMimeMessage Parse(byte[] bytes)
    {
        var message = new ParsedMimeMessage();
        int headerEnd = FindHeaderBoundary(bytes, 0, out int bodyStart);
        var headers = ParseHeaders(bytes, 0, headerEnd);

        foreach (var header in headers)
        {
            message.RawHeaders.Add(new KeyValuePair<string, string>(header.Key, header.Value));
            string name = header.Key.ToLowerInvariant();
            switch (name)
            {
                case "subject":
                    message.Subject = Rfc2047Decoder.Decode(header.Value);
                    break;
                case "message-id":
                    message.MessageId = CleanMessageId(header.Value);
                    break;
                case "from":
                    message.From.AddRange(ParseAddresses(header.Value));
                    break;
                case "to":
                    message.To.AddRange(ParseAddresses(header.Value));
                    break;
                case "cc":
                    message.Cc.AddRange(ParseAddresses(header.Value));
                    break;
                case "bcc":
                    message.Bcc.AddRange(ParseAddresses(header.Value));
                    break;
                case "reply-to":
                    message.ReplyTo.AddRange(ParseAddresses(header.Value));
                    break;
                case "in-reply-to":
                    message.InReplyTo.AddRange(ParseReferences(header.Value));
                    break;
                case "references":
                    message.References.AddRange(ParseReferences(header.Value));
                    break;
                case "date":
                    if (DateTimeOffset.TryParse(header.Value, out var date))
                    {
                        message.Date = date;
                    }
                    break;
            }
        }

        ReadOnlySpan<byte> bodyBytes = bytes.AsSpan(bodyStart);
        ParseEntity(message, headers, bodyBytes, depth: 1);

        return message;
    }

    private static void ParseEntity(ParsedMimeMessage message, List<KeyValuePair<string, string>> headers, ReadOnlySpan<byte> bodyBytes, int depth)
    {
        if (depth > MaxMimeDepth)
        {
            MailLogger.Warning("MimeParser", $"MIME nesting depth limit ({MaxMimeDepth}) reached. Halting deeper entity parsing.");
            return;
        }

        string contentType = GetHeader(headers, "content-type") ?? "text/plain";
        ParseContentType(contentType, out string mediaType, out Dictionary<string, string> parameters);

        string transferEncoding = (GetHeader(headers, "content-transfer-encoding") ?? "7bit").Trim().ToLowerInvariant();
        string? contentId = GetHeader(headers, "content-id");
        string? contentLocation = GetHeader(headers, "content-location");
        string? contentDisposition = GetHeader(headers, "content-disposition");
        string? filename = null;

        if (contentDisposition is not null)
        {
            ParseParameters(contentDisposition, out _, out var dispParams);
            if (dispParams.TryGetValue("filename", out string? fn)) filename = fn;
        }
        if (filename is null && parameters.TryGetValue("name", out string? nameParam))
        {
            filename = nameParam;
        }

        if (mediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            if (!parameters.TryGetValue("boundary", out string? boundary) || string.IsNullOrEmpty(boundary))
            {
                // Malformed multipart: treat body as text
                AddTextPart(message, mediaType, parameters, transferEncoding, bodyBytes);
                return;
            }

            ParseMultipart(message, mediaType, boundary, bodyBytes, depth + 1);
        }
        else
        {
            // Leaf entity
            byte[] rawBytes = DecodeTransferEncoding(bodyBytes, transferEncoding);

            if (mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                string text = DecodeText(rawBytes, parameters.GetValueOrDefault("charset"));
                if (message.TextBody is null)
                {
                    message.TextBody = text;
                }
            }
            else if (mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            {
                string html = DecodeText(rawBytes, parameters.GetValueOrDefault("charset"));
                if (message.HtmlBody is null)
                {
                    message.HtmlBody = html;
                }
            }
            else if (SupportedImageTypes.Contains(mediaType))
            {
                ExtractEmbeddedImage(message, mediaType, contentId, contentLocation, filename, rawBytes);
            }

            message.Parts.Add(new MimePartInfo
            {
                MediaType = mediaType,
                ContentId = contentId,
                ContentLocation = contentLocation,
                FileName = filename,
                Charset = parameters.GetValueOrDefault("charset"),
                TransferEncoding = transferEncoding,
                Data = rawBytes,
            });
        }
    }

    private static void ParseMultipart(ParsedMimeMessage message, string multipartType, string boundary, ReadOnlySpan<byte> bodyBytes, int depth)
    {
        byte[] delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        byte[] endDelimiter = Encoding.ASCII.GetBytes("--" + boundary + "--");

        int current = IndexOf(bodyBytes, delimiter, 0);
        if (current < 0) return;

        current += delimiter.Length;
        // Skip trailing CRLF or spaces
        current = SkipToNextLine(bodyBytes, current);

        while (current < bodyBytes.Length)
        {
            int next = IndexOf(bodyBytes, delimiter, current);
            if (next < 0) break;

            // Extract part bytes between current and next (excluding preceding CRLF)
            int partLength = next - current;
            if (partLength >= 2 && bodyBytes[current + partLength - 2] == '\r' && bodyBytes[current + partLength - 1] == '\n')
            {
                partLength -= 2;
            }
            else if (partLength >= 1 && (bodyBytes[current + partLength - 1] == '\n' || bodyBytes[current + partLength - 1] == '\r'))
            {
                partLength -= 1;
            }

            if (partLength > 0)
            {
                ReadOnlySpan<byte> partSpan = bodyBytes.Slice(current, partLength);
                int partHeaderEnd = FindHeaderBoundary(partSpan, 0, out int partBodyStart);
                var partHeaders = ParseHeaders(partSpan, 0, partHeaderEnd);
                ReadOnlySpan<byte> partBody = partSpan.Slice(partBodyStart);

                ParseEntity(message, partHeaders, partBody, depth);
            }

            // Check if delimiter was end delimiter
            if (next + endDelimiter.Length <= bodyBytes.Length &&
                bodyBytes.Slice(next, endDelimiter.Length).SequenceEqual(endDelimiter))
            {
                break;
            }

            current = next + delimiter.Length;
            current = SkipToNextLine(bodyBytes, current);
        }
    }

    private static void ExtractEmbeddedImage(
        ParsedMimeMessage message,
        string mediaType,
        string? contentId,
        string? contentLocation,
        string? fileName,
        byte[] bytes)
    {
        if (message.EmbeddedImages.Count >= MailEmbeddedImage.MaximumImageCount) return;

        string? identifier = contentId;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            identifier = contentLocation ?? fileName;
        }
        if (string.IsNullOrWhiteSpace(identifier)) return;

        string normalizedCid = identifier.Trim().Trim('<', '>', '"', '\'');
        if (normalizedCid.Length == 0 || normalizedCid.Length > 256 || normalizedCid.Any(char.IsControl)) return;
        if (message.EmbeddedImages.ContainsKey(normalizedCid)) return;

        if (bytes.Length == 0 || bytes.Length > MailEmbeddedImage.MaximumImageBytes)
        {
            MailLogger.Warning("MimeParser", $"Embedded image '{normalizedCid}' byte length ({bytes.Length:N0}) exceeds maximum allowed ({MailEmbeddedImage.MaximumImageBytes:N0}).");
            return;
        }

        int currentTotal = message.EmbeddedImages.Values.Distinct().Sum(img => img.Data.Length);
        if (currentTotal + bytes.Length > MailEmbeddedImage.MaximumTotalBytes)
        {
            MailLogger.Warning("MimeParser", $"Total embedded image bytes ({currentTotal + bytes.Length:N0}) exceeds maximum allowed ({MailEmbeddedImage.MaximumTotalBytes:N0}).");
            return;
        }

        string normalizedMediaType = NormalizeImageMediaType(mediaType);
        var image = new MailEmbeddedImage(normalizedCid, normalizedMediaType, bytes);
        message.EmbeddedImages[normalizedCid] = image;

        string unescaped = Uri.UnescapeDataString(normalizedCid);
        if (!string.Equals(unescaped, normalizedCid, StringComparison.OrdinalIgnoreCase))
        {
            message.EmbeddedImages.TryAdd(unescaped, image);
        }

        if (contentLocation is not null)
        {
            string loc = contentLocation.Trim().Trim('<', '>', '"', '\'');
            if (loc.Length is > 0 and <= 256 && !loc.Any(char.IsControl))
            {
                message.EmbeddedImages.TryAdd(loc, image);
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            string fn = fileName.Trim().Trim('<', '>', '"', '\'');
            if (fn.Length is > 0 and <= 256 && !fn.Any(char.IsControl))
            {
                message.EmbeddedImages.TryAdd(fn, image);
            }
        }

        MailLogger.Info("MimeParser", $"Extracted embedded image: CID='{normalizedCid}', MediaType='{normalizedMediaType}', Size={bytes.Length:N0} bytes.");
    }

    private static void AddTextPart(ParsedMimeMessage message, string mediaType, Dictionary<string, string> parameters, string transferEncoding, ReadOnlySpan<byte> bodyBytes)
    {
        byte[] raw = DecodeTransferEncoding(bodyBytes, transferEncoding);
        string text = DecodeText(raw, parameters.GetValueOrDefault("charset"));
        if (mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
        {
            message.HtmlBody ??= text;
        }
        else
        {
            message.TextBody ??= text;
        }
    }

    private static byte[] DecodeTransferEncoding(ReadOnlySpan<byte> bytes, string encoding) =>
        encoding switch
        {
            "base64" => Base64Decoder.Decode(bytes),
            "quoted-printable" => QuotedPrintable.Decode(bytes),
            _ => bytes.ToArray(),
        };

    private static string DecodeText(byte[] bytes, string? charset)
    {
        Encoding enc = Rfc2047Decoder.ResolveEncoding(charset ?? "utf-8");
        return enc.GetString(bytes);
    }

    private static List<KeyValuePair<string, string>> ParseHeaders(ReadOnlySpan<byte> bytes, int start, int end)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (end <= start) return result;

        string headerText = Encoding.Latin1.GetString(bytes.Slice(start, end - start));
        var lines = headerText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        string? currentKey = null;
        var currentValue = new StringBuilder();

        foreach (string line in lines)
        {
            if (line.Length == 0) continue;

            if (char.IsWhiteSpace(line[0]))
            {
                // Continuation line
                if (currentKey is not null)
                {
                    currentValue.Append(' ').Append(line.TrimStart());
                }
            }
            else
            {
                if (currentKey is not null)
                {
                    result.Add(new KeyValuePair<string, string>(currentKey, currentValue.ToString().Trim()));
                    currentValue.Clear();
                }

                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    currentKey = line[..colon].Trim();
                    currentValue.Append(line[(colon + 1)..].Trim());
                }
                else
                {
                    currentKey = null;
                }
            }
        }

        if (currentKey is not null)
        {
            result.Add(new KeyValuePair<string, string>(currentKey, currentValue.ToString().Trim()));
        }

        return result;
    }

    private static int FindHeaderBoundary(ReadOnlySpan<byte> bytes, int start, out int bodyStart)
    {
        for (int i = start; i < bytes.Length; i++)
        {
            if (i + 3 < bytes.Length && bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                bodyStart = i + 4;
                return i;
            }
            if (i + 1 < bytes.Length && bytes[i] == '\n' && bytes[i + 1] == '\n')
            {
                bodyStart = i + 2;
                return i;
            }
        }
        bodyStart = bytes.Length;
        return bytes.Length;
    }

    private static void ParseContentType(string contentType, out string mediaType, out Dictionary<string, string> parameters)
    {
        ParseParameters(contentType, out mediaType, out parameters);
        mediaType = mediaType.ToLowerInvariant();
    }

    private static void ParseParameters(string headerValue, out string mainValue, out Dictionary<string, string> parameters)
    {
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int semi = headerValue.IndexOf(';');
        if (semi < 0)
        {
            mainValue = headerValue.Trim();
            return;
        }

        mainValue = headerValue[..semi].Trim();
        string rest = headerValue[(semi + 1)..];

        int i = 0;
        while (i < rest.Length)
        {
            while (i < rest.Length && (rest[i] == ' ' || rest[i] == '\t' || rest[i] == ';')) i++;
            if (i >= rest.Length) break;

            int eq = rest.IndexOf('=', i);
            if (eq < 0) break;

            string paramName = rest[i..eq].Trim();
            i = eq + 1;
            while (i < rest.Length && (rest[i] == ' ' || rest[i] == '\t')) i++;

            string paramValue;
            if (i < rest.Length && rest[i] == '"')
            {
                i++;
                int closeQuote = rest.IndexOf('"', i);
                if (closeQuote >= 0)
                {
                    paramValue = rest[i..closeQuote];
                    i = closeQuote + 1;
                }
                else
                {
                    paramValue = rest[i..];
                    i = rest.Length;
                }
            }
            else
            {
                int nextSemi = rest.IndexOf(';', i);
                if (nextSemi >= 0)
                {
                    paramValue = rest[i..nextSemi].Trim();
                    i = nextSemi + 1;
                }
                else
                {
                    paramValue = rest[i..].Trim();
                    i = rest.Length;
                }
            }

            parameters[paramName] = paramValue;
        }
    }

    private static string? GetHeader(List<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var h in headers)
        {
            if (h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return h.Value;
        }
        return null;
    }

    public static List<string> ParseAddresses(string? header)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(header)) return result;

        string decoded = Rfc2047Decoder.Decode(header);
        var parts = decoded.Split(',');
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0) continue;

            int angleStart = trimmed.IndexOf('<');
            int angleEnd = trimmed.IndexOf('>', angleStart + 1);
            if (angleStart >= 0 && angleEnd > angleStart)
            {
                string addr = trimmed[(angleStart + 1)..angleEnd].Trim();
                if (addr.Length > 0) result.Add(addr);
            }
            else
            {
                result.Add(trimmed);
            }
        }
        return result;
    }

    public static List<string> ParseReferences(string? header)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(header)) return result;

        int i = 0;
        while (i < header.Length)
        {
            int start = header.IndexOf('<', i);
            if (start < 0) break;
            int end = header.IndexOf('>', start + 1);
            if (end < 0) break;

            string id = header[(start + 1)..end].Trim();
            if (id.Length > 0) result.Add(id);
            i = end + 1;
        }

        if (result.Count == 0)
        {
            // Fallback for space-delimited non-bracketed references
            foreach (string part in header.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                string clean = part.Trim('<', '>', '"', ' ', '\t');
                if (clean.Length > 0) result.Add(clean);
            }
        }

        return result;
    }

    private static string? CleanMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = value.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
        {
            return trimmed[1..^1].Trim();
        }
        return trimmed;
    }

    private static int IndexOf(ReadOnlySpan<byte> span, ReadOnlySpan<byte> value, int startIndex)
    {
        if (startIndex >= span.Length || value.Length == 0) return -1;
        int index = span.Slice(startIndex).IndexOf(value);
        return index >= 0 ? startIndex + index : -1;
    }

    private static int SkipToNextLine(ReadOnlySpan<byte> bytes, int start)
    {
        while (start < bytes.Length && bytes[start] != '\n')
        {
            start++;
        }
        if (start < bytes.Length && bytes[start] == '\n')
        {
            start++;
        }
        return start;
    }
}
