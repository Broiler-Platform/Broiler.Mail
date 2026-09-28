using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application;

/// <summary>Application composition without native platform or protocol-library dependencies.</summary>
public sealed class MailApplication(
    IAccountStore accounts, ISettingsStore settings, IMailReceiver receiver, IMailSender sender, ICredentialStore credentials)
{
    public IAccountStore Accounts { get; } = accounts ?? throw new ArgumentNullException(nameof(accounts));
    public ISettingsStore Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));
    public IMailReceiver Receiver { get; } = receiver ?? throw new ArgumentNullException(nameof(receiver));
    public IMailSender Sender { get; } = sender ?? throw new ArgumentNullException(nameof(sender));
    public ICredentialStore Credentials { get; } = credentials ?? throw new ArgumentNullException(nameof(credentials));

    public AccountProfile? LoadedAccount { get; private set; }
    public ApplicationSettings LoadedSettings { get; private set; } = new();
    public string? AccountLoadError { get; private set; }
    public string? SettingsLoadError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profiles = await Accounts.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (profiles.Count > 1)
                throw new InvalidDataException("Version 1 supports only one account.");
            LoadedAccount = profiles.SingleOrDefault();
            AccountLoadError = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AccountLoadError = $"Account data could not be loaded: {error.Message} Saving is disabled to protect the file.";
        }

        try
        {
            LoadedSettings = await Settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            SettingsLoadError = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SettingsLoadError = $"Settings could not be loaded: {error.Message} Saving is disabled to protect the file.";
        }
    }

    public MailShellView CreateShell(IUiDispatcher? dispatcher = null)
    {
        dispatcher ??= new ImmediateUiDispatcher();
        return new(new MailShellViewModel(
            new AccountProfileViewModel(Accounts, Credentials, Receiver, dispatcher, LoadedAccount, AccountLoadError),
            new SettingsViewModel(Settings, dispatcher, LoadedSettings, SettingsLoadError),
            new InboxViewModel(Receiver, dispatcher)));
    }
}
