using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

/// <summary>Version 2 composition contract; no send operation is implemented yet.</summary>
public sealed record MailDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required AccountId AccountId { get; init; }
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string PlainText { get; init; } = string.Empty;
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
}
