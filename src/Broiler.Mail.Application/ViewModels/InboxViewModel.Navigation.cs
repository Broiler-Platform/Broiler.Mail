using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class InboxViewModel
{
    private AccountNavigation? _navigation;
    private bool _loadingNavigation;
    private int _navigationGeneration;
    public bool SupportsAccountNavigation => navigation is not null && cache is not null;
    public bool NavigationReady { get; private set; }
    public bool IsSavingNavigation { get; private set; }
    public IReadOnlyList<string> PinnedFolders => _navigation?.PinnedMailboxIds ?? [];
    public string StartFolder => _navigation?.StartMailboxId ?? "INBOX";
    public string NavigationNotice { get; private set; } = "Local account navigation is not loaded.";
    public bool IsCurrentFolderPinned => PinnedFolders.Contains(SelectedMailboxId, StringComparer.Ordinal);
    public bool CanEditNavigation => SupportsAccountNavigation && NavigationReady && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingNavigation && _account is not null;
    public bool CanPinCurrentFolder => CanEditNavigation && (IsCurrentFolderPinned || PinnedFolders.Count < 8) &&
        Folders.Any(folder => folder.MailboxId == SelectedMailboxId && folder.IsSelectable);
    public bool CanSetStartFolder => CanEditNavigation && StartFolder != SelectedMailboxId && Folders.Any(folder => folder.MailboxId == SelectedMailboxId && folder.IsSelectable);
    public bool CanReloadNavigation => SupportsAccountNavigation && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingNavigation && _account is not null;
    public bool CanResetNavigation => CanEditNavigation && (StartFolder != "INBOX" || PinnedFolders.Count > 0);
    private void ResetNavigation(AccountProfile? account)
    {
        ++_navigationGeneration; _navigation = account is null ? null : new(account.Id, "INBOX", []);
        _loadingNavigation = account is not null && SupportsAccountNavigation;
        NavigationReady = IsSavingNavigation = false;
        NavigationNotice = _loadingNavigation ? "Opening this account's saved navigation locally…" : "Local account navigation is not available.";
    }
    private string NavigationSummary() => $"Navigation for {_account?.EmailAddress}: start folder {StartFolder}; {PinnedFolders.Count}/8 pinned folders. Opening folders reads saved headers only. Use Receive mail explicitly to check the server. These settings do not rename or create server folders.";
    public Task SetStartFolderAsync() => !CanSetStartFolder ? Task.CompletedTask : SaveNavigationAsync(_navigation! with { StartMailboxId = SelectedMailboxId });
    public Task TogglePinnedFolderAsync() => !CanPinCurrentFolder ? Task.CompletedTask : SaveNavigationAsync(_navigation! with
    {
        PinnedMailboxIds = IsCurrentFolderPinned ? PinnedFolders.Where(name => name != SelectedMailboxId).ToArray() : PinnedFolders.Append(SelectedMailboxId).ToArray(),
    });
    public Task ResetAccountNavigationAsync() => !CanResetNavigation ? Task.CompletedTask : SaveNavigationAsync(new(_account!.Id, "INBOX", []));
    private async Task SaveNavigationAsync(AccountNavigation next)
    {
        int generation = _navigationGeneration; IsSavingNavigation = true;
        NavigationNotice = "Saving local navigation preferences…"; Notify(); string? error = null;
        // The explicitly requested save remains scoped to its captured account even if the user switches away.
        try { await navigation!.SaveAsync(next).ConfigureAwait(false); }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { error = "Navigation preferences were not confirmed saved. Previous preferences were kept. Repair the file if needed, then Reload navigation."; }
        if (_disposed) return;
        try
        {
            dispatcher.Post(() =>
            {
                if (_disposed || generation != _navigationGeneration || _account?.Id != next.AccountId) return;
                IsSavingNavigation = false;
                if (error is null) { _navigation = next; UpdateFolders(); }
                NavigationNotice = error ?? NavigationSummary(); Notify();
            });
        }
        catch (ObjectDisposedException) { }
    }
    public Task ReloadAccountNavigationAsync()
    {
        if (!CanReloadNavigation) return Task.CompletedTask;
        StopPending(); ResetNavigation(_account); ResetMessages(); UpdateFolders(); SelectedMailboxId = "INBOX";
        return CacheLoadTask = LoadCachedAsync(_account!, older: false, restoreNavigation: true);
    }
    public bool CanOpenPinnedFolder(string mailboxId) => NavigationReady && CanSwitchFolder && !IsSavingNavigation &&
        PinnedFolders.Contains(mailboxId, StringComparer.Ordinal) && Folders.Any(folder => folder.MailboxId == mailboxId && folder.IsSelectable);
    public bool OpenPinnedFolder(string mailboxId) => CanOpenPinnedFolder(mailboxId) && SelectFolder(mailboxId);
}
