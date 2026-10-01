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
// Resource impact:  7/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Core.Services;

/// <summary>One append attempt after durable SMTP acceptance. Never sends or retries mail.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=A3C8F8
// Broiler-Falsified-If: an implementation resubmits the message by SMTP or repeats the APPEND after an append whose outcome is unknown
// Broiler-Human:        PENDING
public interface ISentCopyWriter
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=5A054F
    // Broiler-Falsified-If: an APPEND whose tagged response was lost is reported as Failed rather than Unknown, inviting a second copy
    // Broiler-Human:        PENDING
    Task<SentCopyState> AppendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default);
}
