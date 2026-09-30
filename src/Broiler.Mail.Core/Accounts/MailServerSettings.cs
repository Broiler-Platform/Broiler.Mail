// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   4
// Annotated:        4/4
// Exempt:           11
// Human-reviewed:   0/4
// IP risk:          None
// Security risk:    Medium
// Criteria:         4/0
// Resource impact:  1/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=2B6945
// Broiler-Falsified-If: two settings that differ only in Port or Security compare equal, so a password can be saved against an unsaved port or security change
// Broiler-Human:        PENDING
public sealed record MailServerSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string UserName { get; init; }
    public TransportSecurity Security { get; init; } = TransportSecurity.Tls;
    public AuthenticationMethod Authentication { get; init; } = AuthenticationMethod.Password;
}

// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=Medium; Resources=0; Fingerprint=5B5098
// Broiler-Falsified-If: a member exists that the IMAP or SMTP connect code maps to SecureSocketOptions.None or Auto, allowing an unencrypted session
// Broiler-Human:        PENDING
public enum TransportSecurity { Tls, StartTls }

// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=79C70F
// Broiler-Falsified-If: a member other than Password lets the IMAP or SMTP code send the saved password instead of failing before connecting
// Broiler-Human:        PENDING
public enum AuthenticationMethod { Password, OAuth2 }

// Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=BA84BE
// Broiler-Falsified-If: two members share an underlying value, so the IMAP and SMTP passwords occupy one credential target
// Broiler-Human:        PENDING
public enum MailProtocol { Imap, Smtp }
