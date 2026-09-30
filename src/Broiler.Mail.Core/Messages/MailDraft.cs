using Broiler.Mail.Core.Accounts;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Messages;

/// <summary>A plain-text composition snapshot with a pinned sender and optional reply thread.</summary>
public sealed record MailDraft
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    public required AccountId AccountId { get; init; }
    [JsonRequired] public string FromAddress { get; init; } = string.Empty;
    public DateTimeOffset? SubmissionDate { get; init; }
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string PlainText { get; init; } = string.Empty;
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
}
