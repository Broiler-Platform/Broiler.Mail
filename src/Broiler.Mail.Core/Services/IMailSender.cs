// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           1
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  7/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>Version 2 seam. Implementations must check draft/account identity before submission.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=140067
// Broiler-Falsified-If: an implementation submits a draft whose AccountId or FromAddress differs from the account it is sent with
// Broiler-Human:        PENDING
public interface IMailSender
{
    bool IsAvailable => true;
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=F46089
    // Broiler-Falsified-If: a Bcc recipient appears in the header block of the message delivered to the To and Cc recipients
    // Broiler-Human:        PENDING
    Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default);
}
