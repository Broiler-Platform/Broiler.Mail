using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using MimeKit;

namespace Broiler.Mail.Infrastructure.Mail;

/// <summary>Shared MIME content for SMTP and the sender's private Sent copy.</summary>
internal static class OutgoingMessageFactory
{
    public static MimeMessage Create(AccountProfile account, MailDraft draft, bool includeBcc = false)
    {
        var message = new MimeMessage
        {
            MessageId = $"{draft.Id:N}@broiler.mail", Date = draft.SubmissionDate ?? DateTimeOffset.UtcNow,
            Subject = draft.Subject, Body = new TextPart("plain") { Text = draft.PlainText },
        };
        try
        {
            message.From.Add(new MailboxAddress(account.DisplayName, draft.FromAddress));
            foreach (string address in draft.To) message.To.Add(MailboxAddress.Parse(address));
            foreach (string address in draft.Cc) message.Cc.Add(MailboxAddress.Parse(address));
            if (includeBcc) foreach (string address in draft.Bcc) message.Bcc.Add(MailboxAddress.Parse(address));
            message.InReplyTo = draft.InReplyTo;
            foreach (string reference in draft.References) message.References.Add(reference);
            return message;
        }
        catch { message.Dispose(); throw; }
    }
}
