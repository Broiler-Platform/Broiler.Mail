// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           0
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/3
// Resource impact:  8/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Application.Preview;

/// <summary>Platform adapter. Must enforce renderer isolation and resource policy before displaying mail.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=CEA041
// Broiler-Falsified-If: a preview opened through ShowAsync fetches a remote image or stylesheet before the user chooses Load remote images
// Broiler-Human:        PENDING
public interface IHtmlPreviewHost : IDisposable
{
    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=8; Fingerprint=D8A40E
    // Broiler-Falsified-If: a preview opened through ShowAsync fetches a remote image or stylesheet before the user chooses Load remote images
    // Broiler-Human:        PENDING
    Task<string> ShowAsync(MailMessageBody message);
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=45295B
    // Broiler-Falsified-If: a preview still opening on its own thread when Close is called goes on to show its window
    // Broiler-Human:        PENDING
    void Close();
}
