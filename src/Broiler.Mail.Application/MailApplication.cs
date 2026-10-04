// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           13
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    High
// Criteria:         3/2
// Resource impact:  4/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.Preview;

namespace Broiler.Mail.Application;

/// <summary>Application composition without native platform or protocol-library dependencies.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=92648C
// Broiler-Falsified-If: an unreadable drafts.json leaves DraftLoadError null, so the composer autosaves over the file
// Broiler-Human:        PENDING
public sealed class MailApplication(
    IAccountStore accounts, ISettingsStore settings, IMailReceiver receiver, IMailSender sender, ICredentialStore credentials, IDraftStore? drafts = null, ISentCopyWriter? sentCopies = null,
    IOutgoingConnectionTester? outgoingTester = null)
{
    public IAccountStore Accounts { get; } = accounts ?? throw new ArgumentNullException(nameof(accounts));
    public ISettingsStore Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));
    public IMailReceiver Receiver { get; } = receiver ?? throw new ArgumentNullException(nameof(receiver));
    public IMailSender Sender { get; } = sender ?? throw new ArgumentNullException(nameof(sender));
    public ICredentialStore Credentials { get; } = credentials ?? throw new ArgumentNullException(nameof(credentials));
    public IDraftStore Drafts { get; } = drafts ?? new MemoryDraftStore();
    public ISentCopyWriter? SentCopies { get; } = sentCopies;
    /// <summary>The non-sending SMTP sign-in test; without it the Account tab offers no SMTP test.</summary>
    public IOutgoingConnectionTester? OutgoingTester { get; } = outgoingTester;
    public DraftStoreState LoadedDraft { get; private set; } = new(0, null);
    public string? DraftLoadError { get; private set; }

    public AccountProfile? LoadedAccount { get; private set; }
    public ApplicationSettings LoadedSettings { get; private set; } = new();
    public string? AccountLoadError { get; private set; }
    public string? SettingsLoadError { get; private set; }

    // Broiler-AI:           Origin=AI; Spec=ADR-0001; IP=Low; Security=High; Resources=4; Fingerprint=C77BBA
    // Broiler-Falsified-If: an unreadable drafts.json leaves DraftLoadError null, so the composer autosaves over the file
    // Broiler-Human:        PENDING
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LoadedDraft = await Drafts.LoadAsync(cancellationToken).ConfigureAwait(false);
            DraftLoadError = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            DraftLoadError = "The saved draft could not be loaded. Draft creation and saving are disabled; restore or repair drafts.json before restarting.";
        }
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=277523
    // Broiler-Falsified-If: a draft or account load error recorded by InitializeAsync is not passed to its view model, so saving is enabled over the unreadable file
    // Broiler-Human:        PENDING
    public MailShellView CreateShell(IUiDispatcher? dispatcher = null, IHtmlPreviewHost? htmlPreview = null)
        => new(CreateViewModel(dispatcher), htmlPreview);

    /// <summary>Create state independently of views, allowing deterministic in-memory fixtures to be prepared before layout.</summary>
    public MailShellViewModel CreateViewModel(IUiDispatcher? dispatcher = null)
    {
        dispatcher ??= new ImmediateUiDispatcher();
        return new MailShellViewModel(
            new AccountProfileViewModel(Accounts, Credentials, Receiver, dispatcher, LoadedAccount, AccountLoadError, OutgoingTester),
            new SettingsViewModel(Settings, dispatcher, LoadedSettings, SettingsLoadError),
            new InboxViewModel(Receiver, dispatcher),
            new ComposerViewModel(Drafts, LoadedDraft, dispatcher, Sender, DraftLoadError, SentCopies));
    }
}
