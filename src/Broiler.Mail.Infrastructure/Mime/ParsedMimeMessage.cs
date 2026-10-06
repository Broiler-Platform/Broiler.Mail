// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Infrastructure.Mime;

/// <summary>
/// Structured representation of a parsed RFC 5322 / MIME message.
/// 100% native .NET, trimmer-safe, zero external dependencies.
/// </summary>
public sealed class ParsedMimeMessage
{
    public string? Subject { get; set; }
    public string? MessageId { get; set; }
    public DateTimeOffset? Date { get; set; }
    public List<string> From { get; } = [];
    public List<string> To { get; } = [];
    public List<string> Cc { get; } = [];
    public List<string> Bcc { get; } = [];
    public List<string> ReplyTo { get; } = [];
    public List<string> InReplyTo { get; } = [];
    public List<string> References { get; } = [];

    public string? TextBody { get; set; }
    public string? HtmlBody { get; set; }

    public Dictionary<string, MailEmbeddedImage> EmbeddedImages { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<MimePartInfo> Parts { get; } = [];
    public List<KeyValuePair<string, string>> RawHeaders { get; } = [];
}

/// <summary>Metadata for an extracted MIME part.</summary>
public sealed class MimePartInfo
{
    public string MediaType { get; set; } = "text/plain";
    public string? ContentId { get; set; }
    public string? ContentLocation { get; set; }
    public string? FileName { get; set; }
    public string? Charset { get; set; }
    public string? TransferEncoding { get; set; }
    public byte[] Data { get; set; } = [];
}
