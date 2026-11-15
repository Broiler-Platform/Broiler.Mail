// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    Medium
// Criteria:         2/0
// Resource impact:  3/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;
using Broiler.UI;

namespace Broiler.Mail.Application.Preview;

// Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=9CE7B0
// Hairs-Falsified-If: the raw HtmlText of an HTML-only message is placed in the text view instead of its decoded PlainText
// Hairs-Human:        PENDING
public sealed class PlainTextMessagePreview : IMessagePreview
{
    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=0B634B
    // Hairs-Falsified-If: the raw HtmlText of an HTML-only message is placed in the text view instead of its decoded PlainText
    // Hairs-Human:        PENDING
    public UiElement CreateContent(MailMessageBody message) => new ScrollableMessageText
    {
        Text = message.PlainText,
    };
}
