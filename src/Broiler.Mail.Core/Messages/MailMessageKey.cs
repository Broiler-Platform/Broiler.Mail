using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Core.Messages;

/// <summary>IMAP identity, scoped to an account, mailbox, and UIDVALIDITY epoch.</summary>
public readonly record struct MailMessageKey(
    AccountId AccountId, string MailboxId, uint UidValidity, uint Uid);
