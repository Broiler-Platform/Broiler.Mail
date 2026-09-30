namespace Broiler.Mail.Core.Messages;

/// <summary>Parsed source headers for composition, independent of the abbreviated inbox display.</summary>
public sealed record MailCompositionSource
{
    public string Subject { get; init; } = string.Empty;
    public IReadOnlyList<string> From { get; init; } = [];
    public IReadOnlyList<string> ReplyTo { get; init; } = [];
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public string? MessageId { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    public IReadOnlyList<string> InReplyTo { get; init; } = [];
}
