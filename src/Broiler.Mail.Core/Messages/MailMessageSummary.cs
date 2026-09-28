namespace Broiler.Mail.Core.Messages;

public sealed record MailMessageSummary
{
    public required MailMessageKey Key { get; init; }
    public required string Sender { get; init; }
    public required string Subject { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public bool IsRead { get; init; }
}
