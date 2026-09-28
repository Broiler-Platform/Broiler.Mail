namespace Broiler.Mail.Core.Accounts;

public sealed record MailServerSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string UserName { get; init; }
    public TransportSecurity Security { get; init; } = TransportSecurity.Tls;
    public AuthenticationMethod Authentication { get; init; } = AuthenticationMethod.Password;
}

public enum TransportSecurity { Tls, StartTls }

public enum AuthenticationMethod { Password, OAuth2 }

public enum MailProtocol { Imap, Smtp }
