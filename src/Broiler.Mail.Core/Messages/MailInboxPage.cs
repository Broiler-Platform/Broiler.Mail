using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

public sealed record MailInboxPage(IReadOnlyList<MailMessageSummary> Messages, MailInboxCursor? Older);

/// <summary>A session-only continuation. Mailbox membership changes require a fresh first page.</summary>
public sealed record MailInboxCursor(AccountId AccountId, uint UidValidity, uint? UidNext, int MessageCount, int NextIndex);
