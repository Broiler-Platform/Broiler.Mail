// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  3/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using MimeKit;

namespace Broiler.Mail.Infrastructure.Mail;

/// <summary>Shared MIME content for SMTP and the sender's private Sent copy.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=3; Fingerprint=3AF06A
// Broiler-Falsified-If: a message built for SMTP submission carries a Bcc header naming a blind-copy recipient
// Broiler-Human:        PENDING
internal static class OutgoingMessageFactory
{
    // Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=3; Fingerprint=289988
    // Broiler-Falsified-If: with includeBcc false, an address from the draft's Bcc list appears in any header of the returned message
    // Broiler-Human:        PENDING
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
