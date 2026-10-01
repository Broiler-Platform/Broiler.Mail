// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   14
// Annotated:        14/14
// Exempt:           21
// Human-reviewed:   0/14
// IP risk:          Low
// Security risk:    High
// Criteria:         14/9
// Resource impact:  7/10 max
// Unverified:       14
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=0E66E7
// Broiler-Falsified-If: a password is written under a credential key for connection details that differ from the saved profile
// Broiler-Human:        PENDING
public sealed class AccountProfileViewModel : SaveViewModel
{
    private readonly IAccountStore _store;
    private readonly AccountId _id;
    private readonly ICredentialStore _credentials;
    private readonly IMailReceiver _receiver;
    private CancellationTokenSource? _connectionCancellation;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=4B52C5
    // Broiler-Falsified-If: a profile without an outgoing server opens with SMTP configuration switched on
    // Broiler-Human:        PENDING
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
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=C124B9
    // Broiler-Falsified-If: the password controls are enabled while no profile has been saved
    // Broiler-Human:        PENDING
    public bool CanManagePassword => CanSave && Profile is not null;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=37C46F
    // Broiler-Falsified-If: the SMTP password controls are enabled for a saved profile without an outgoing server
    // Broiler-Human:        PENDING
    public bool CanManageSmtpPassword => CanManagePassword && Profile?.OutgoingServer is not null;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=6D5116
    // Broiler-Falsified-If: Cancel is offered while no connection test is running
    // Broiler-Human:        PENDING
    public bool CanCancelTest => IsBusy && _connectionCancellation is not null;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=87F27B
    // Broiler-Falsified-If: Profile is replaced by field values edited after the store write began rather than by the candidate the store wrote
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=2; Fingerprint=5E5C0D
    // Broiler-Falsified-If: the one-argument SavePasswordAsync stores the secret in the SMTP slot instead of the IMAP slot
    // Broiler-Human:        PENDING
    public Task SavePasswordAsync(string password, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(password, MailProtocol.Imap, cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=14B721
    // Broiler-Falsified-If: a password is written while the form host, port, user name or security differs from the saved profile instead of being refused
    // Broiler-Human:        PENDING
    public Task SavePasswordAsync(string password, MailProtocol protocol, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var account = RequireSavedProfile(protocol);
            return _credentials.WriteAsync(CredentialKey.For(account, protocol), password, cancellationToken);
        }, () => { }, "Saving password…", "Password saved.", "Password not saved", "Password save canceled.");

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=2; Fingerprint=D33B47
    // Broiler-Falsified-If: the one-argument ForgetPasswordAsync deletes the SMTP credential slot instead of the IMAP one
    // Broiler-Human:        PENDING
    public Task ForgetPasswordAsync(CancellationToken cancellationToken = default) =>
        ForgetPasswordAsync(MailProtocol.Imap, cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=EEDAAC
    // Broiler-Falsified-If: after the saved profile switches that protocol to OAuth2, Forget password is refused and the previously saved secret stays in the credential store
    // Broiler-Human:        PENDING
    public Task ForgetPasswordAsync(MailProtocol protocol, CancellationToken cancellationToken = default) =>
        RunAsync(() => _credentials.DeleteAsync(CredentialKey.For(RequireSavedProfile(protocol), protocol), cancellationToken),
            () => { }, "Removing password…", "Saved password removed.", "Password not removed", "Password removal canceled.");

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=FDC9C0
    // Broiler-Falsified-If: a connection test runs while the form holds unsaved connection edits instead of being refused with the save-your-changes message
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=899D31
    // Broiler-Falsified-If: Cancel pressed after a test finished throws ObjectDisposedException from the disposed token source
    // Broiler-Human:        PENDING
    public void CancelConnectionTest() => _connectionCancellation?.Cancel();

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=4CBB0D
    // Broiler-Falsified-If: a form whose host differs from the saved Profile returns Profile, so a password is bound to or tested against details the user has not saved
    // Broiler-Human:        PENDING
    private AccountProfile RequireSavedProfile(MailProtocol protocol = MailProtocol.Imap)
    {
        if (Profile is null) throw new InvalidOperationException("Save the account profile first.");
        if (BuildProfile() != Profile) throw new InvalidOperationException("Save your account changes before using the password or testing the connection.");
        var server = protocol == MailProtocol.Smtp ? Profile.OutgoingServer ?? throw new InvalidOperationException("Configure and save SMTP first.") : Profile.IncomingServer;
        if (server.Authentication != AuthenticationMethod.Password)
            throw new InvalidOperationException("Only password or app-password authentication is currently supported.");
        return Profile;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=7565C7
    // Broiler-Falsified-If: a host typed with a port, such as imap.example.com:993, yields a candidate profile without an ArgumentException
    // Broiler-Human:        PENDING
    private AccountProfile BuildProfile()
    {
        if (!int.TryParse(Port, out int port))
            throw new ConfigurationValidationException("IncomingServer.Port", "IMAP port must be a number between 1 and 65535.");
        MailServerSettings? outgoingServer = null;
        if (ConfigureSmtp)
        {
            if (!int.TryParse(SmtpPort, out int smtpPort))
                throw new ConfigurationValidationException("OutgoingServer.Port", "SMTP port must be a number between 1 and 65535.");
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
