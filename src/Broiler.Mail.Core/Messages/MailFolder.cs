namespace Broiler.Mail.Core.Messages;

/// <summary>Server mailbox identity and hierarchy metadata; display names are not paths.</summary>
public sealed record MailFolder(string MailboxId, string? HierarchyDelimiter, bool IsSelectable,
    IReadOnlyList<string> Attributes);
