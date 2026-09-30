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
// Resource impact:  8/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;
using Broiler.UI;

namespace Broiler.Mail.Application.Preview;

/// <summary>Presentation boundary; an HTML implementation must isolate untrusted content.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=7E65E6
// Broiler-Falsified-If: an implementation given an HTML message renders its HtmlText in the main window instead of through the isolated preview host
// Broiler-Human:        PENDING
public interface IMessagePreview
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=8; Fingerprint=5BDDB4
    // Broiler-Falsified-If: an implementation given an HTML message renders its HtmlText in the main window instead of through the isolated preview host
    // Broiler-Human:        PENDING
    UiElement CreateContent(MailMessageBody message);
}
