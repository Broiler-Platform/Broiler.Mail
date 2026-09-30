using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

public sealed class AccountProfileViewModel : SaveViewModel
{
    private readonly IAccountStore _store;
    private readonly AccountId _id;
    private readonly ICredentialStore _credentials;
    private readonly IMailReceiver _receiver;
    private CancellationTokenSource? _connectionCancellation;

    public AccountProfileViewModel(IAccountStore store, ICredentialStore credentials, IMailReceiver receiver,
        IUiDispatcher dispatcher, AccountProfile? profile, string? loadError)
        : base(dispatcher, loadError)
    {
        _store = store;
        _credentials = credentials;
        _receiver = receiver;
        Profile = profile;
        _id = profile?.Id ?? AccountId.New();
        DisplayName = profile?.DisplayName ?? string.Empty;
        EmailAddress = profile?.EmailAddress ?? string.Empty;
        Host = profile?.IncomingServer.Host ?? string.Empty;
        Port = (profile?.IncomingServer.Port ?? 993).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UserName = profile?.IncomingServer.UserName ?? string.Empty;
        Security = profile?.IncomingServer.Security ?? TransportSecurity.Tls;
        Authentication = profile?.IncomingServer.Authentication ?? AuthenticationMethod.Password;
        ConfigureSmtp = profile?.OutgoingServer is not null;
        SmtpHost = profile?.OutgoingServer?.Host ?? string.Empty;
        SmtpPort = (profile?.OutgoingServer?.Port ?? 587).ToString(System.Globalization.CultureInfo.InvariantCulture);
        SmtpUserName = profile?.OutgoingServer?.UserName ?? string.Empty;
        SmtpSecurity = profile?.OutgoingServer?.Security ?? TransportSecurity.StartTls;
        SmtpAuthentication = profile?.OutgoingServer?.Authentication ?? AuthenticationMethod.Password;
        SentCopyMode = profile?.SentCopyMode ?? SentCopyMode.NotConfigured;
        SentFolder = profile?.SentFolder ?? string.Empty;
    }

    public AccountProfile? Profile { get; private set; }
    public string DisplayName { get; set; }
    public string EmailAddress { get; set; }
    public string Host { get; set; }
    public string Port { get; set; }
    public string UserName { get; set; }
    public TransportSecurity Security { get; set; }
    public AuthenticationMethod Authentication { get; set; }
    public bool ConfigureSmtp { get; set; }
    public string SmtpHost { get; set; }
    public string SmtpPort { get; set; }
    public string SmtpUserName { get; set; }
    public TransportSecurity SmtpSecurity { get; set; }
    public AuthenticationMethod SmtpAuthentication { get; set; }
    public SentCopyMode SentCopyMode { get; set; }
    public string SentFolder { get; set; }
    public bool CanManagePassword => CanSave && Profile is not null;
    public bool CanManageSmtpPassword => CanManagePassword && Profile?.OutgoingServer is not null;
    public bool CanCancelTest => IsBusy && _connectionCancellation is not null;

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot fields before awaiting; a draft edit cannot change an in-flight save.
        AccountProfile? candidate = null;
        return SaveAsync(async () =>
        {
            candidate = BuildProfile();
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        }, () => Profile = candidate!, "Account profile saved. Save each protocol's password separately; connection testing applies to IMAP.");
    }

    public Task SavePasswordAsync(string password, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(password, MailProtocol.Imap, cancellationToken);

    public Task SavePasswordAsync(string password, MailProtocol protocol, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var account = RequireSavedProfile(protocol);
            return _credentials.WriteAsync(CredentialKey.For(account, protocol), password, cancellationToken);
        }, () => { }, "Saving password…", "Password saved in Windows Credential Manager.", "Password not saved", "Password save canceled.");

    public Task ForgetPasswordAsync(CancellationToken cancellationToken = default) =>
        ForgetPasswordAsync(MailProtocol.Imap, cancellationToken);

    public Task ForgetPasswordAsync(MailProtocol protocol, CancellationToken cancellationToken = default) =>
        RunAsync(() => _credentials.DeleteAsync(CredentialKey.For(RequireSavedProfile(protocol), protocol), cancellationToken),
            () => { }, "Removing password…", "Saved password removed.", "Password not removed", "Password removal canceled.");

    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave) return Task.CompletedTask;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionCancellation = cancellation;
        return RunAsync(() => _receiver.TestConnectionAsync(RequireSavedProfile(), cancellation.Token), () => { },
            "Connecting and authenticating…", "Connected securely and authenticated. No messages were fetched.",
            "Connection test failed", "Connection test canceled.", () =>
            {
                _connectionCancellation = null;
                cancellation.Dispose();
            });
    }

    public void CancelConnectionTest() => _connectionCancellation?.Cancel();

    private AccountProfile RequireSavedProfile(MailProtocol protocol = MailProtocol.Imap)
    {
        if (Profile is null) throw new InvalidOperationException("Save the account profile first.");
        if (BuildProfile() != Profile) throw new InvalidOperationException("Save your account changes before using the password or testing the connection.");
        var server = protocol == MailProtocol.Smtp ? Profile.OutgoingServer ?? throw new InvalidOperationException("Configure and save SMTP first.") : Profile.IncomingServer;
        if (server.Authentication != AuthenticationMethod.Password)
            throw new InvalidOperationException("Only password or app-password authentication is currently supported.");
        return Profile;
    }

    private AccountProfile BuildProfile()
    {
        if (!int.TryParse(Port, out int port))
            throw new ArgumentException("IMAP port must be a number between 1 and 65535.");
        MailServerSettings? outgoingServer = null;
        if (ConfigureSmtp)
        {
            if (!int.TryParse(SmtpPort, out int smtpPort))
                throw new ArgumentException("SMTP port must be a number between 1 and 65535.");
            outgoingServer = new MailServerSettings
            {
                Host = SmtpHost.Trim(), Port = smtpPort, UserName = SmtpUserName.Trim(),
                Security = SmtpSecurity, Authentication = SmtpAuthentication,
            };
        }
        var candidate = new AccountProfile
        {
            Id = _id, DisplayName = DisplayName.Trim(), EmailAddress = EmailAddress.Trim(),
            IncomingServer = new MailServerSettings
            {
                Host = Host.Trim(), Port = port, UserName = UserName.Trim(), Security = Security, Authentication = Authentication,
            },
            OutgoingServer = outgoingServer,
            SentCopyMode = ConfigureSmtp ? SentCopyMode : SentCopyMode.NotConfigured,
            SentFolder = ConfigureSmtp && SentCopyMode == SentCopyMode.AppendToFolder ? SentFolder.Trim() : null,
            IsEnabled = Profile?.IsEnabled ?? true,
        };
        ConfigurationValidator.Validate(candidate);
        return candidate;
    }
}
