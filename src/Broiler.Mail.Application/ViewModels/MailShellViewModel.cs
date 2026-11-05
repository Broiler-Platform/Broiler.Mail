// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   3
// Annotated:        3/3
// Exempt:           7
// Human-reviewed:   0/3
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  3/10 max
// Unverified:       3
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.Mail.Application.Persistence;

namespace Broiler.Mail.Application.ViewModels;

// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=2CB11B
// Broiler-Falsified-If: after the account profile is saved with new settings, Inbox or Composer still holds the previous AccountProfile
// Broiler-Human:        PENDING
public sealed class MailShellViewModel
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=0805D1
    // Broiler-Falsified-If: a later Account.Changed leaves Inbox or Composer on the profile that was current at construction
    // Broiler-Human:        PENDING
    public MailShellViewModel(AccountProfileViewModel account, SettingsViewModel settings, InboxViewModel inbox, ComposerViewModel? composer = null)
    {
        Account = account;
        Settings = settings;
        Inbox = inbox;
        Composer = composer ?? new ComposerViewModel();
        if (settings.Storage is { } storage)
        {
            storage.CleanupStarting += Inbox.BeginStorageMaintenance;
            storage.CleanupFinished += Inbox.EndStorageMaintenance;
            storage.PolicyChangeStarting += Inbox.BeginStorageMaintenance;
            storage.PolicyChangeFinished += () => Inbox.EndStorageMaintenance(false);
            storage.PolicyChanged += policy => Inbox.SetDownloadPolicy(policy.Downloads);
            storage.CanStartSnapshot = () => !Composer.IsBusy && Accounts?.IsBusy != true;
            storage.SaveDraftBeforeSnapshot = Composer.SaveAsync;
            storage.Changed += (_, _) => Composer.SetProfileStorageBusy(storage.IsSnapshotBusy || Accounts?.IsBusy == true);
        }
        // The remembered split applies to this session; the host saves it again with the window layout.
        Inbox.SplitterFraction = settings.Settings.InboxSplitterFraction;
        Compose = new CompositionCommands(Composer, Inbox);
        Composer.SetAccount(account.Profile);
        Inbox.SetAccount(account.Profile);
        Account.Changed += (_, _) => { Inbox.SetAccount(Account.Profile); Composer.SetAccount(Account.Profile); };
    }
    public string Title => "Broiler Mail";
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=6B5558
    // Broiler-Human:        PENDING
    public AccountProfile? CurrentAccount => Account.Profile;
    public string Status => Inbox.Status;
    public SettingsViewModel Settings { get; }
    public AccountProfileViewModel Account { get; }
    public InboxViewModel Inbox { get; }
    public ComposerViewModel Composer { get; }
    /// <summary>New, Reply, Reply all, and Forward, shared by the reader and the composer.</summary>
    public CompositionCommands Compose { get; }
    public AccountSelectionViewModel? Accounts { get; private set; }
    public AccountReceiveQueueViewModel? AccountReceives { get; private set; }
    public void ConfigureAccounts(IAccountCollectionStore store, AccountCollectionState state, IUiDispatcher dispatcher, string? loadError, IAccountRemovalService? removal = null, IAccountDownloadedMailStorage? localMail = null)
    {
        var coordinated = store as CoordinatedAccountCollectionStore ?? new CoordinatedAccountCollectionStore(store);
        Accounts = new(coordinated, state, Account, Composer, () => Settings.Storage?.IsBusy == true, dispatcher, loadError, removal, localMail);
        if (Inbox.LocalCache is {} cache)
        {
            AccountReceives = new(coordinated, Inbox.ReceiveService, cache, dispatcher, () => loadError is null && Accounts.CanSave && !Account.IsBusy && !Account.HasUnsavedChanges && Settings.Storage?.IsBusy != true && Accounts.PendingRemoval is null && Accounts.PendingLocalMailCleanup is null && Accounts.PendingOrphanCleanup is null);
            AccountReceives.UpdateAccounts(Accounts.Profiles);
            Accounts.HasActiveReceives = () => AccountReceives.HasWork;
            Accounts.Changed += (_, _) => AccountReceives.UpdateAccounts(Accounts.Profiles);
            AccountReceives.Changed += (_, _) => Accounts.RefreshAvailability();
            if (Settings.Storage is {} storage) { storage.CanStartOperation = () => !AccountReceives.HasWork; storage.Changed += (_, _) => Accounts.RefreshAvailability(); }
        }
        Accounts.LocalMailCleanupStarting += Inbox.BeginStorageMaintenance;
        Accounts.LocalMailCleanupFinished += cleared => Inbox.EndStorageMaintenance(cleared);
        Accounts.RemovalStarting += Inbox.BeginStorageMaintenance;
        Accounts.RemovalFinished += () => Inbox.EndStorageMaintenance(false);
        Accounts.Changed += (_, _) => Composer.SetProfileStorageBusy(Accounts.IsBusy || Settings.Storage?.IsSnapshotBusy == true);
    }
}
