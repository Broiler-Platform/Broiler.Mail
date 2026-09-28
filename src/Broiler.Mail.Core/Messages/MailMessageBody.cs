namespace Broiler.Mail.Core.Messages;

/// <summary>Decoded body data. HtmlText remains untrusted and must not be rendered directly.</summary>
public sealed record MailMessageBody(MailMessageKey Key, string PlainText, string? HtmlText = null)
{
    public bool IsHtmlFallback { get; init; }
    public bool IsTruncated { get; init; }
}
