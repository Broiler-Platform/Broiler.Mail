// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Application.Views;

/// <summary>Copyable parsed metadata, not original MIME headers. Never includes Bcc or transport secrets.</summary>
public static class ParsedMessageHeaders
{
    public static IEnumerable<string> Lines(MailMessageSummary message, MailCompositionSource? headers, bool expanded)
    {
        yield return $"From: {message.Sender}";
        if (headers is null) yield break;
        if (headers.To.Count > 0) yield return $"To: {string.Join(", ", headers.To)}";
        if (headers.Cc.Count > 0) yield return $"Cc: {string.Join(", ", headers.Cc)}";
        if (!expanded) yield break;
        yield return "Parsed headers only; original MIME/transport headers are not retained.";
        if (headers.From.Count > 0) yield return $"Parsed From: {string.Join(", ", headers.From)}";
        if (headers.ReplyTo.Count > 0) yield return $"Reply-To: {string.Join(", ", headers.ReplyTo)}";
        yield return $"Subject: {headers.Subject}";
        if (headers.MessageId is { } id) yield return $"Message-ID: {id}";
        if (headers.InReplyTo.Count > 0) yield return $"In-Reply-To: {string.Join(" ", headers.InReplyTo)}";
        if (headers.References.Count > 0) yield return $"References: {string.Join(" ", headers.References)}";
    }
}
