// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
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

// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=2B6945
// Hairs-Falsified-If: two settings that differ only in Port or Security compare equal, so a password can be saved against an unsaved port or security change
// Hairs-Human:        PENDING
public sealed record MailServerSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string UserName { get; init; }
    public TransportSecurity Security { get; init; } = TransportSecurity.Tls;
    public AuthenticationMethod Authentication { get; init; } = AuthenticationMethod.Password;
}

// Hairs-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=Medium; Resources=0; Fingerprint=5B5098
// Hairs-Falsified-If: a member exists that the IMAP or SMTP connect code maps to SecureSocketOptions.None or Auto, allowing an unencrypted session
// Hairs-Human:        PENDING
public enum TransportSecurity { Tls, StartTls }

// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=79C70F
// Hairs-Falsified-If: a member other than Password lets the IMAP or SMTP code send the saved password instead of failing before connecting
// Hairs-Human:        PENDING
public enum AuthenticationMethod { Password, OAuth2 }

// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=BA84BE
// Hairs-Falsified-If: two members share an underlying value, so the IMAP and SMTP passwords occupy one credential target
// Hairs-Human:        PENDING
public enum MailProtocol { Imap, Smtp }
