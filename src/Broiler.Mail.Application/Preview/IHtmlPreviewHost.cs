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

/// <summary>Where a message's HTML preview window is in its life.</summary>
public enum HtmlPreviewPhase
{
    /// <summary>The window is open and shows the document.</summary>
    Open,
    /// <summary>The window was closed after it opened.</summary>
    Closed,
    /// <summary>The preview could not be shown; the text preview remains available.</summary>
    Unavailable,
    /// <summary>The preview was closed or replaced before its window appeared.</summary>
    Canceled,
}

/// <summary>A preview phase change for one message, with the text to show for it.</summary>
public sealed record HtmlPreviewChange(MailMessageKey Message, HtmlPreviewPhase Phase, string Text);

/// <summary>Platform adapter. Must enforce renderer isolation and resource policy before displaying mail.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=Low; Security=High; Resources=8; Fingerprint=CEA041
// Broiler-Falsified-If: a preview opened through ShowAsync fetches a remote image or stylesheet before the user chooses Load remote images
// Broiler-Human:        PENDING
public interface IHtmlPreviewHost : IDisposable
{
    /// <summary>
    /// Raised, possibly on another thread, when a preview opens, closes, fails, or is canceled. The
    /// message key lets a reader ignore changes that belong to a message it no longer shows.
    /// </summary>
    event EventHandler<HtmlPreviewChange>? Changed;

    /// <summary>The message whose preview is open or opening, or null.</summary>
    MailMessageKey? Current { get; }

    // Broiler-AI:           Origin=AI; Spec=ADR-0005; IP=None; Security=High; Resources=8; Fingerprint=D8A40E
    // Broiler-Falsified-If: a preview opened through ShowAsync fetches a remote image or stylesheet before the user chooses Load remote images
    // Broiler-Human:        PENDING
    Task<string> ShowAsync(MailMessageBody message);
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=45295B
    // Broiler-Falsified-If: a preview still opening on its own thread when Close is called goes on to show its window
    // Broiler-Human:        PENDING
    void Close();
}
