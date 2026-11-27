// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   11
// Annotated:        3/11
// Exempt:           8
// Human-reviewed:   0/11
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  3/10 max
// Unverified:       11
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.Mail.Application.Persistence;

namespace Broiler.Mail.Application.ViewModels;

// Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=2CB11B
// Hairs-Falsified-If: after the account profile is saved with new settings, Inbox or Composer still holds the previous AccountProfile
// Hairs-Human:        PENDING
public sealed class MailShellViewModel
{
    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=D9AD00
    // Hairs-Falsified-If: a later Account.Changed leaves Inbox or Composer on the profile that was current at construction
    // Hairs-Human:        PENDING
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
    public const string ApplicationName = "Broiler Mail";
    public string Title => ApplicationName;

    /// <summary>
    /// The main window's caption: the folder and the account, then the application, such as
    /// "Inbox (3 unread loaded) — alex@example.com — Broiler Mail". The model holds no server-side folder
    /// unread count, so the count is only of the loaded rows and says so. During a search the list holds
    /// results, not the folder, so the caption names the search and gives no count.
    /// </summary>
    public string WindowTitle => Inbox.IsSearchActive
        ? FormatSearchWindowTitle(Inbox.IsAccountSearchActive ? null : Inbox.SelectedMailboxId, Account.Profile?.EmailAddress)
        : FormatWindowTitle(Inbox.SelectedMailboxId, Inbox.Messages.Count(message => !message.IsRead), Account.Profile?.EmailAddress);

    /// <summary>The caption for a folder's own list: <paramref name="loadedUnread"/> counts loaded rows only.</summary>
    public static string FormatWindowTitle(string mailboxId, int loadedUnread, string? account)
    {
        string folder = FolderCaption(mailboxId);
        if (loadedUnread > 0) folder += $" ({loadedUnread} unread loaded)";
        return CaptionWithAccount(folder, account);
    }

    /// <summary>The caption while search results are shown; a null <paramref name="mailboxId"/> is an all-folder search.</summary>
    public static string FormatSearchWindowTitle(string? mailboxId, string? account) =>
        CaptionWithAccount(mailboxId is null ? "Search in all folders" : $"Search in {FolderCaption(mailboxId)}", account);

    private static string FolderCaption(string mailboxId)
    {
        string folder = mailboxId.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? "Inbox" : mailboxId;
        return new string(folder.Where(c => !char.IsControl(c)).Take(120).ToArray());
    }

    private static string CaptionWithAccount(string folder, string? account) =>
        string.IsNullOrWhiteSpace(account) ? $"{folder} — {ApplicationName}" : $"{folder} — {account} — {ApplicationName}";
    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=6B5558
    // Hairs-Human:        PENDING
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
