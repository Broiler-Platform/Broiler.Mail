using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Core.Accounts;

/// <summary>Identifies a credential slot and binds its contents to one server configuration.</summary>
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

    public static CredentialKey For(AccountProfile account, MailProtocol protocol)
    {
        ConfigurationValidator.Validate(account);
        var server = protocol switch
        {
            MailProtocol.Imap => account.IncomingServer,
            MailProtocol.Smtp => account.OutgoingServer ?? throw new ArgumentException("Configure the outgoing server first."),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
        string identity = JsonSerializer.Serialize(new
        {
            Host = server.Host.ToLowerInvariant(), server.Port, server.UserName, server.Security, server.Authentication,
        });
        return new(account.Id, protocol, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }
}
