namespace Broiler.Mail.Core.Accounts;

/// <summary>Non-secret configuration. Credentials belong in ICredentialStore.</summary>
public sealed record AccountProfile
{
    public required AccountId Id { get; init; }
    public required string DisplayName { get; init; }
    public required string EmailAddress { get; init; }
    public required MailServerSettings IncomingServer { get; init; }
    public MailServerSettings? OutgoingServer { get; init; }
    public bool IsEnabled { get; init; } = true;
}
