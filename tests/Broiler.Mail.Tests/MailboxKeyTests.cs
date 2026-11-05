using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Tests;

public sealed class MailboxKeyTests
{
    [Fact]
    public void CacheScopeRejectsOtherAccountsFoldersEpochsAndInvalidUids()
    {
        var account = AccountId.New();
        var mailbox = new MailboxKey(account, "INBOX", 42);
        var message = new MailMessageKey(account, "INBOX", 42, 7);
        Assert.True(mailbox.Contains(message));
        Assert.False(mailbox.Contains(message with { AccountId = AccountId.New() }));
        Assert.False(mailbox.Contains(message with { MailboxId = "Archive" }));
        Assert.False(mailbox.Contains(message with { UidValidity = 43 }));
        Assert.False(mailbox.Contains(message with { Uid = 0 }));
        Assert.NotEqual(mailbox, new MailboxKey(account, "Archive", 42));
        Assert.NotEqual(mailbox, new MailboxKey(AccountId.New(), "INBOX", 42));
        Assert.Equal(mailbox, new MailboxKey(account, "INBOX", 42));
    }

    [Fact]
    public void InvalidScopeCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => new MailboxKey(default, "INBOX", 1));
        Assert.Throws<ArgumentException>(() => new MailboxKey(AccountId.New(), " ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MailboxKey(AccountId.New(), "INBOX", 0));
    }
}
