// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   28
// Annotated:        13/28
// Exempt:           44
// Human-reviewed:   0/28
// IP risk:          Low
// Security risk:    High
// Criteria:         13/9
// Resource impact:  7/10 max
// Unverified:       28
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=CAB93C
// Broiler-Falsified-If: a password is written under a credential key for connection details that differ from the saved profile
// Broiler-Human:        PENDING
/// <summary>The next action that brings an account closer to receiving mail.</summary>
public enum AccountSetupStep { SaveDetails, SavePassword, TestConnection, Ready }

/// <summary>The result of the most recent connection test for the saved profile and password.</summary>
public enum ConnectionCheck { NotRun, Running, Passed, Failed }

/// <summary>Where optional outgoing mail stands. Separate from <see cref="AccountSetupStep"/>: receiving never waits for it.</summary>
public enum OutgoingSetupStep { NotConfigured, SavePassword, Test, Ready }

public sealed class AccountProfileViewModel : SaveViewModel
{
    private readonly IAccountStore _store;
    private readonly AccountId _id;
    private readonly ICredentialStore _credentials;
    private readonly IMailReceiver _receiver;
    private readonly IOutgoingConnectionTester? _outgoingTester;
    private CancellationTokenSource? _connectionCancellation;
    private int _credentialCheck;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=53AD41
    // Broiler-Falsified-If: a profile without an outgoing server opens with SMTP configuration switched on
    // Broiler-Human:        PENDING
    public AccountProfileViewModel(IAccountStore store, ICredentialStore credentials, IMailReceiver receiver,
        IUiDispatcher dispatcher, AccountProfile? profile, string? loadError, IOutgoingConnectionTester? outgoingTester = null)
        : base(dispatcher, loadError)
    {
        _store = store;
        _credentials = credentials;
        _receiver = receiver;
        _outgoingTester = outgoingTester;
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
        if (loadError is null) RefreshCredentialState();
    }

    /// <summary>Whether an IMAP password is stored for the saved profile; null while still checking.</summary>
    public bool? HasPassword { get; private set; }
    /// <summary>Whether an SMTP password is stored for the saved outgoing server; null while checking or without SMTP.</summary>
    public bool? HasSmtpPassword { get; private set; }
    public ConnectionCheck ConnectionCheck { get; private set; }
    /// <summary>Why the last connection test failed, shown beside the setup step.</summary>
    public string? ConnectionFailure { get; private set; }
    /// <summary>The result of the most recent SMTP sign-in test. Kept in memory only; it never affects receiving.</summary>
    public ConnectionCheck OutgoingCheck { get; private set; }
    /// <summary>Why the last SMTP sign-in test failed, shown beside the outgoing step.</summary>
    public string? OutgoingFailure { get; private set; }
    /// <summary>The kind of the last SMTP sign-in failure, when the tester classified it.</summary>
    public MailConnectionFailure? OutgoingFailureKind { get; private set; }
    /// <summary>The protocol of the most recently started test, so Cancel can hand focus back to the test that ran.</summary>
    public MailProtocol? LastTest { get; private set; }
    /// <summary>Whether this app can test an SMTP sign-in at all.</summary>
    public bool SupportsOutgoingTest => _outgoingTester is not null;

    /// <summary>The form differs from the saved profile, including a profile that was never saved.</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            if (Profile is null) return true;
            try { return BuildProfile() != Profile; }
            catch (ConfigurationValidationException) { return true; }
        }
    }

    public AccountSetupStep NextStep =>
        HasUnsavedChanges ? AccountSetupStep.SaveDetails
        : HasPassword != true ? AccountSetupStep.SavePassword
        : ConnectionCheck != ConnectionCheck.Passed ? AccountSetupStep.TestConnection
        : AccountSetupStep.Ready;

    public OutgoingSetupStep OutgoingStep =>
        Profile?.OutgoingServer is null ? OutgoingSetupStep.NotConfigured
        : OutgoingCheck == ConnectionCheck.Passed ? OutgoingSetupStep.Ready
        : HasSmtpPassword == false ? OutgoingSetupStep.SavePassword
        : OutgoingSetupStep.Test;

    /// <summary>The view calls this after copying edited fields, so unsaved-change state stays current.</summary>
    public void NotifyEdited() => NotifyChanged();

    /// <summary>
    /// Checks which passwords are stored for the saved profile. Only the presence is kept; the secret
    /// itself is dropped immediately and never reaches the view, status text, or diagnostics.
    /// </summary>
    private void RefreshCredentialState(bool receiving = true, bool sending = true)
    {
        int check = ++_credentialCheck;
        var profile = Profile;
        if (profile is null)
        {
            HasPassword = false;
            HasSmtpPassword = null;
            return;
        }
        // Both are read again; only a binding that changed shows as unknown until then.
        if (receiving) HasPassword = null;
        if (sending) HasSmtpPassword = null;
        _ = Task.Run(async () =>
        {
            bool? imap = await IsStoredAsync(CredentialKey.For(profile, MailProtocol.Imap)).ConfigureAwait(false);
            bool? smtp = profile.OutgoingServer is null ? null : await IsStoredAsync(CredentialKey.For(profile, MailProtocol.Smtp)).ConfigureAwait(false);
            Post(() =>
            {
                if (check != _credentialCheck) return;
                HasPassword = imap;
                HasSmtpPassword = smtp;
                NotifyChanged();
            });
        });
    }

    private async Task<bool?> IsStoredAsync(CredentialKey key)
    {
        try { return await _credentials.ContainsAsync(key).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            // Unknown rather than missing: the store may be locked or unavailable.
            return null;
        }
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
    public bool CanTestOutgoing => CanManageSmtpPassword && SupportsOutgoingTest;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=6D5116
    // Broiler-Falsified-If: Cancel is offered while no connection test is running
    // Broiler-Human:        PENDING
    public bool CanCancelTest => IsBusy && _connectionCancellation is not null;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=B60920
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
        }, () =>
        {
            var previous = Profile;
            Profile = candidate!;
            if (candidate == previous) return;
            // Server settings are part of the credential binding, so a protocol's presence and test result start
            // over when its own settings change. Each protocol is judged on its own: adding outgoing mail does not
            // undo a passed receiving test, and editing the incoming server does not undo a passed SMTP test.
            bool receiving = previous is null || previous.IncomingServer != candidate!.IncomingServer || previous.IsEnabled != candidate.IsEnabled;
            bool sending = previous is null || previous.OutgoingServer != candidate!.OutgoingServer || previous.IsEnabled != candidate.IsEnabled;
            if (receiving) { ConnectionCheck = ConnectionCheck.NotRun; ConnectionFailure = null; }
            if (sending) ResetOutgoingCheck();
            RefreshCredentialState(receiving, sending);
        }, "Account profile saved.");
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=2; Fingerprint=5E5C0D
    // Broiler-Falsified-If: the one-argument SavePasswordAsync stores the secret in the SMTP slot instead of the IMAP slot
    // Broiler-Human:        PENDING
    public Task SavePasswordAsync(string password, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(password, MailProtocol.Imap, cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=143BC2
    // Broiler-Falsified-If: a password is written while the form host, port, user name or security differs from the saved profile instead of being refused
    // Broiler-Human:        PENDING
    public Task SavePasswordAsync(string password, MailProtocol protocol, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var account = RequireSavedProfile(protocol);
            return _credentials.WriteAsync(CredentialKey.For(account, protocol), password, cancellationToken);
        }, () => RecordPassword(protocol, stored: true), "Saving password…", "Password saved.", "Password not saved", "Password save canceled.");

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=2; Fingerprint=D33B47
    // Broiler-Falsified-If: the one-argument ForgetPasswordAsync deletes the SMTP credential slot instead of the IMAP one
    // Broiler-Human:        PENDING
    public Task ForgetPasswordAsync(CancellationToken cancellationToken = default) =>
        ForgetPasswordAsync(MailProtocol.Imap, cancellationToken);

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=2; Fingerprint=35354D
    // Broiler-Falsified-If: after the saved profile switches that protocol to OAuth2, Forget password is refused and the previously saved secret stays in the credential store
    // Broiler-Human:        PENDING
    public Task ForgetPasswordAsync(MailProtocol protocol, CancellationToken cancellationToken = default) =>
        RunAsync(() => _credentials.DeleteAsync(CredentialKey.For(RequireSavedProfile(protocol), protocol), cancellationToken),
            () => RecordPassword(protocol, stored: false), "Removing password…", "Saved password removed.", "Password not removed", "Password removal canceled.");

    private void RecordPassword(MailProtocol protocol, bool stored)
    {
        // A different password needs a new test of that protocol only.
        if (protocol == MailProtocol.Smtp)
        {
            HasSmtpPassword = stored;
            ResetOutgoingCheck();
            return;
        }
        HasPassword = stored;
        ConnectionCheck = ConnectionCheck.NotRun;
        ConnectionFailure = null;
    }

    private void ResetOutgoingCheck()
    {
        OutgoingCheck = ConnectionCheck.NotRun;
        OutgoingFailure = null;
        OutgoingFailureKind = null;
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=AF7681
    // Broiler-Falsified-If: a connection test runs while the form holds unsaved connection edits instead of being refused with the save-your-changes message
    // Broiler-Human:        PENDING
    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave || ProfileToTest(MailProtocol.Imap) is not { } account) return Task.CompletedTask;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionCancellation = cancellation;
        LastTest = MailProtocol.Imap;
        ConnectionCheck = ConnectionCheck.Running;
        return RunAsync(() => _receiver.TestConnectionAsync(account, cancellation.Token), () => ConnectionCheck = ConnectionCheck.Passed,
            "Connecting and authenticating…", "Connected securely and authenticated. No messages were fetched.",
            "Connection test failed", "Connection test canceled.", failure =>
            {
                if (failure is not null)
                {
                    // A canceled test proves nothing either way; a real failure is kept beside the step.
                    bool canceled = cancellation.IsCancellationRequested;
                    ConnectionCheck = canceled ? ConnectionCheck.NotRun : ConnectionCheck.Failed;
                    ConnectionFailure = canceled ? null : failure;
                }
                _connectionCancellation = null;
                cancellation.Dispose();
            });
    }

    /// <summary>
    /// Signs in to the saved SMTP server without sending anything. The result stands beside the outgoing step
    /// only; the receiving checklist and <see cref="NextStep"/> never depend on it. Cancel and Escape stop it
    /// like the connection test, and only one test runs at a time.
    /// </summary>
    public Task TestOutgoingConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave || _outgoingTester is not { } tester || ProfileToTest(MailProtocol.Smtp) is not { } account) return Task.CompletedTask;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionCancellation = cancellation;
        LastTest = MailProtocol.Smtp;
        MailConnectionFailure? kind = null;
        OutgoingCheck = ConnectionCheck.Running;
        return RunAsync(async () =>
            {
                try { await tester.TestConnectionAsync(account, cancellation.Token).ConfigureAwait(false); }
                catch (MailConnectionException error) { kind = error.Failure; throw; }
            }, () => { ResetOutgoingCheck(); OutgoingCheck = ConnectionCheck.Passed; },
            "Signing in to the SMTP server… No message is sent.",
            "The SMTP server accepted the sign-in over an encrypted connection. No message was sent.",
            "SMTP sign-in test failed", "SMTP sign-in test canceled.", failure =>
            {
                if (failure is not null)
                {
                    // As for the connection test: canceled proves nothing, and a real failure stays beside the
                    // outgoing step with its kind.
                    if (cancellation.IsCancellationRequested) ResetOutgoingCheck();
                    else
                    {
                        OutgoingCheck = ConnectionCheck.Failed;
                        OutgoingFailure = failure;
                        OutgoingFailureKind = kind;
                        // The tester found no password bound to these details, whatever the earlier check said.
                        if (kind == MailConnectionFailure.MissingPassword) HasSmtpPassword = false;
                    }
                }
                _connectionCancellation = null;
                cancellation.Dispose();
            });
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=899D31
    // Broiler-Falsified-If: Cancel pressed after a test finished throws ObjectDisposedException from the disposed token source
    public void CancelConnectionTest()
    {
        try { _connectionCancellation?.Cancel(); }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or AggregateException) { }
    }

    /// <summary>
    /// The saved profile a test may use, or null after refusing the test. A test that cannot start, because of
    /// unsaved or invalid edits or a missing or unsupported server, is no result: the status says what to do
    /// first instead of reporting a failed test, and the results of the saved profile stand.
    /// </summary>
    private AccountProfile? ProfileToTest(MailProtocol protocol)
    {
        try { return RequireSavedProfile(protocol); }
        catch (Exception refusal) when (refusal is InvalidOperationException or ArgumentException)
        {
            Refuse(refusal, "The test did not start.");
            return null;
        }
    }

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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=D3A8D0
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
