// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           4
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  2/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Core.Accounts;

/// <summary>Identifies a credential slot and binds its contents to one server configuration.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=F383DF
// Broiler-Falsified-If: keys for two accounts, two protocols, or two server settings that differ in anything but host letter case compare equal
// Broiler-Human:        PENDING
public sealed record CredentialKey
{
    private CredentialKey(AccountId accountId, MailProtocol protocol, string binding)
    {
        AccountId = accountId;
        Protocol = protocol;
        Binding = binding;
    }

    public AccountId AccountId { get; }
    public MailProtocol Protocol { get; }
    public string Binding { get; }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=B8FC89
    // Broiler-Falsified-If: a profile whose server host, port, username, security or authentication changed yields the same Binding as before, so the old secret is released to the new server
    // Broiler-Human:        PENDING
    public static CredentialKey For(AccountProfile account, MailProtocol protocol)
    {
        ConfigurationValidator.Validate(account);
        var server = protocol switch
        {
            MailProtocol.Imap => account.IncomingServer,
            MailProtocol.Smtp => account.OutgoingServer ?? throw new ArgumentException("Configure the outgoing server first."),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
        string identity = JsonSerializer.Serialize(new CredentialBinding(
            server.Host.ToLowerInvariant(), server.Port, server.UserName, server.Security, server.Authentication),
            CredentialJsonContext.Default.CredentialBinding);
        return new(account.Id, protocol, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }
}
