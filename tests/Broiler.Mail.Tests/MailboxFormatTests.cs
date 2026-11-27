using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Tests;

public sealed class MailboxFormatTests
{
    [Theory]
    [InlineData("a@example.test", "", "a@example.test")]
    [InlineData("a@example.test", "  ", "a@example.test")]
    [InlineData("a@example.test", "Ann Lee", "\"Ann Lee\" <a@example.test>")]
    [InlineData("a@example.test", "Lee, Ann", "\"Lee, Ann\" <a@example.test>")]
    [InlineData("a@example.test", "Ann \"AJ\" Lee", "\"Ann \\\"AJ\\\" Lee\" <a@example.test>")]
    public void FormatsQuotedMailboxThatParsesBackToOneAddress(string address, string name, string expected)
    {
        string text = MailComposition.FormatMailbox(address, name);
        Assert.Equal(expected, text);
        Assert.Equal([address], MailComposition.ParseRecipients(text));
        Assert.Equal(["b@example.test", address], MailComposition.ParseRecipients("b@example.test, " + text));
    }
}
