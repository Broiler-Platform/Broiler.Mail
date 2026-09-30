// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           8
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

/// <summary>Non-secret configuration. Credentials belong in ICredentialStore.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=AB2C4D
// Broiler-Falsified-If: two profiles that differ only in OutgoingServer or SentFolder compare equal, so a password can be saved against unsaved SMTP edits
// Broiler-Human:        PENDING
public sealed record AccountProfile
{
    public required AccountId Id { get; init; }
    public required string DisplayName { get; init; }
    public required string EmailAddress { get; init; }
    public required MailServerSettings IncomingServer { get; init; }
    public MailServerSettings? OutgoingServer { get; init; }
    public SentCopyMode SentCopyMode { get; init; }
    public string? SentFolder { get; init; }
    public bool IsEnabled { get; init; } = true;
}
