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
    }

    public AccountProfile? Profile { get; private set; }
    public string DisplayName { get; set; }
    public string EmailAddress { get; set; }
    public string Host { get; set; }
    public string Port { get; set; }
    public string UserName { get; set; }
    public TransportSecurity Security { get; set; }
    public AuthenticationMethod Authentication { get; set; }
    public bool CanManagePassword => CanSave && Profile is not null;
    public bool CanCancelTest => IsBusy && _connectionCancellation is not null;

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot fields before awaiting; a draft edit cannot change an in-flight save.
        AccountProfile? candidate = null;
        return SaveAsync(async () =>
        {
            candidate = BuildProfile();
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        }, () => Profile = candidate!, "Account profile saved. Save a password for these connection details, then test the connection.");
    }

    public Task SavePasswordAsync(string password, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var account = RequireSavedProfile();
            return _credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), password, cancellationToken);
        }, () => { }, "Saving password…", "Password saved in Windows Credential Manager.", "Password not saved", "Password save canceled.");

    public Task ForgetPasswordAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _credentials.DeleteAsync(CredentialKey.For(RequireSavedProfile(), MailProtocol.Imap), cancellationToken),
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

    private AccountProfile RequireSavedProfile()
    {
        if (Profile is null) throw new InvalidOperationException("Save the account profile first.");
        if (BuildProfile() != Profile) throw new InvalidOperationException("Save your account changes before using the password or testing the connection.");
        if (Profile.IncomingServer.Authentication != AuthenticationMethod.Password)
            throw new InvalidOperationException("Only password or app-password authentication is currently supported.");
        return Profile;
    }

    private AccountProfile BuildProfile()
    {
        if (!int.TryParse(Port, out int port))
            throw new ArgumentException("Server port must be a number between 1 and 65535.");
        var candidate = new AccountProfile
        {
            Id = _id, DisplayName = DisplayName.Trim(), EmailAddress = EmailAddress.Trim(),
            IncomingServer = new MailServerSettings
            {
                Host = Host.Trim(), Port = port, UserName = UserName.Trim(), Security = Security, Authentication = Authentication,
            },
            OutgoingServer = Profile?.OutgoingServer,
            IsEnabled = Profile?.IsEnabled ?? true,
        };
        ConfigurationValidator.Validate(candidate);
        return candidate;
    }
}
