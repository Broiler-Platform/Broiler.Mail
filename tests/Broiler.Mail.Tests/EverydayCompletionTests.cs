using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Tests;

public sealed class EverydayCompletionTests
{
    [Theory]
    [InlineData("", "I have attached the file.", true)]
    [InlineData("ATTACHMENTS", "Body", true)]
    [InlineData("", "See attachment, please.", true)]
    [InlineData("", "> The attachment is quoted", false)]
    [InlineData("", "Body\n-- \nSignature attachment", false)]
    [InlineData("", "Body\n---------- Forwarded message ----------\nAttachment", false)]
    [InlineData("", "Unattached and reattachment", false)]
    public void AttachmentHintsUseWordsAndIgnoreQuotesAndSignatures(string subject, string text, bool expected)
        => Assert.Equal(expected, AttachmentReminder.IsMentioned(subject, text));

    [Fact]
    public void ReminderTracksEditableTextWithoutChangingDraftSubmissionState()
    {
        using var model = new ComposerViewModel(); model.SetAccount(TestDirectory.Profile()); model.StartNew();
        model.Edit("to@example.test", "", "", "Subject", "Attached is my file");
        Assert.Contains("plain text only", model.AttachmentReminderText); Assert.True(model.CanEdit);
        Assert.Equal(DraftSubmissionState.Editing, model.SubmissionState);
        model.Edit("to@example.test", "", "", "Subject", "No file mentioned"); Assert.Null(model.AttachmentReminderText);
    }

    [Fact]
    public void ParsedHeaderInspectionIncludesThreadingAndExplainsItsScope()
    {
        var account = TestDirectory.Profile();
        var summary = new MailMessageSummary { Key = new(account.Id, "INBOX", 7, 1), Sender = "Sender", Subject = "Short" };
        var source = new MailCompositionSource { From = ["sender@example.test"], To = ["to@example.test"], Cc = ["copy@example.test"],
            ReplyTo = ["reply@example.test"], Subject = "Full subject", MessageId = "one@example.test", InReplyTo = ["parent@example.test"], References = ["root@example.test"] };
        var brief = string.Join("\n", ParsedMessageHeaders.Lines(summary, source, false));
        Assert.DoesNotContain("Message-ID:", brief);
        var full = string.Join("\n", ParsedMessageHeaders.Lines(summary, source, true));
        foreach (var value in new[] { "Reply-To: reply@example.test", "Message-ID: one@example.test", "In-Reply-To: parent@example.test", "References: root@example.test", "original MIME/transport headers are not retained" }) Assert.Contains(value, full);
        Assert.DoesNotContain("Bcc:", full); Assert.Single(ParsedMessageHeaders.Lines(summary, null, true));
    }
}
