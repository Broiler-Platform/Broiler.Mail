using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class AccountSelectionViewModel : SaveViewModel
{
    private readonly IAccountCollectionStore _store;
    private readonly AccountProfileViewModel _editor;
    private readonly ComposerViewModel _composer;
    private readonly Func<bool> _storageBusy;
    private readonly IAccountRemovalService? _removal;
    private CancellationTokenSource? _removalCancellation;
    public AccountProfile? PendingRemoval { get; private set; }
    public string? LastRemovalBackup { get; private set; }
    public event Action? RemovalStarting;
    public event Action? RemovalFinished;
    public IReadOnlyList<AccountProfile> Profiles { get; private set; }
    public AccountId? SelectedId { get; private set; }
    public bool IsAdding { get; private set; }
    public bool IsChanging => IsBusy || _editor.IsSelectionBusy;
    public Func<bool> CanChangeView { get; set; } = () => true;
    public Func<bool> HasActiveReceives { get; set; } = () => false;
    internal void RefreshAvailability() => NotifyChanged();
    public bool CanChange => CanSave && _editor.CanSave && !_composer.IsBusy && !_storageBusy() && CanChangeView();
    public bool CanSwitch => CanChange && PendingRemoval is null && PendingLocalMailCleanup is null && PendingOrphanCleanup is null && !IsAdding && !_editor.HasUnsavedChanges;
    public bool CanAdd => CanChange && PendingRemoval is null && PendingLocalMailCleanup is null && PendingOrphanCleanup is null && !IsAdding && Profiles.Count < 32 && (Profiles.Count == 0 || !_editor.HasUnsavedChanges);
    public AccountProfile? SelectedProfile => Profiles.SingleOrDefault(profile => profile.Id == SelectedId);
    public bool CanManage => CanSwitch && SelectedProfile is not null;
    private bool DraftAllowsRemoval => !_composer.HasUnreadableDraft && _composer.DraftAccountId != SelectedId;
    public bool CanRemove => CanManage && _removal is not null && DraftAllowsRemoval && !HasActiveReceives();
    public bool CanConfirmRemoval => PendingRemoval is not null && CanChange && !_editor.HasUnsavedChanges &&
        PendingRemoval == SelectedProfile && DraftAllowsRemoval && !HasActiveReceives();
    public string RemovalExplanation => SelectedProfile is null ? "Add or select an account to manage it."
        : HasActiveReceives() ? "Finish or cancel all account receives before removal. Cancellation must finish before the recovery backup can start."
        : _removal is null ? "Account removal requires verified profile backup support."
        : !DraftAllowsRemoval ? "Resolve this account's retained draft or submission record in Compose before removing it. Unreadable draft data must be repaired first."
        : "Removal keeps downloaded mail and never changes server mail. A recovery backup is created first; backups do not contain passwords.";
    public bool CanMoveEarlier => CanManage && Profiles.ToList().FindIndex(profile => profile.Id == SelectedId) > 0;
    public bool CanMoveLater => CanManage && Profiles.ToList().FindIndex(profile => profile.Id == SelectedId) < Profiles.Count - 1;
    public string Guidance => _editor.HasUnsavedChanges && !IsAdding && Profiles.Count > 0
        ? "Save account edits before switching." : IsAdding ? "Finish new account setup, or cancel the new account." : Status;

    public AccountSelectionViewModel(IAccountCollectionStore store, AccountCollectionState state, AccountProfileViewModel editor,
        ComposerViewModel composer, Func<bool> storageBusy, IUiDispatcher dispatcher, string? loadError, IAccountRemovalService? removal = null, IAccountDownloadedMailStorage? localMail = null) : base(dispatcher, loadError)
    {
        (_store, _editor, _composer, _storageBusy) = (store, editor, composer, storageBusy);
        _removal = removal; _localMail = localMail;
        Profiles = state.Accounts; SelectedId = state.SelectedAccountId;
        editor.Changed += (_, _) => NotifyChanged(); composer.Changed += (_, _) => NotifyChanged();
        editor.ProfileSaved += (_, _) =>
        {
            if (editor.Profile is not { } profile) return;
            Profiles = Profiles.Any(p => p.Id == profile.Id) ? Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() : [.. Profiles, profile];
            SelectedId = profile.Id; IsAdding = false; PendingRemoval = PendingLocalMailCleanup = null; ClearOrphanList(); NotifyChanged();
        };
    }

    public Task SwitchAsync(AccountId id, CancellationToken token = default)
    {
        if (!CanSwitch) { Refuse(new InvalidOperationException("Save account edits and finish active operations before switching."), "Account not switched."); return Task.CompletedTask; }
        var profile = Profiles.SingleOrDefault(p => p.Id == id);
        if (profile is null) { Refuse(new InvalidOperationException("The account is not available."), "Account not switched."); return Task.CompletedTask; }
        _editor.SetSelectionBusy(true);
        return RunAsync(() => _store.SelectAsync(id, token), () => { SelectedId = id; LocalMailInfo = null; LastLocalMailCleanupBackup = null; _editor.SelectProfile(profile); },
            "Switching account…", "Account selected.", "Account not switched", "Account selection canceled.", _ => _editor.SetSelectionBusy(false));
    }

    public bool BeginAdd()
    {
        if (!CanAdd) return false;
        if (Profiles.Count == 0) return true; // Reopen first-account setup without discarding typed details.
        IsAdding = true; _editor.SelectProfile(null); NotifyChanged(); return true;
    }
    public bool CancelAdd()
    {
        if (!IsAdding || !CanChange) return false;
        IsAdding = false; _editor.SelectProfile(Profiles.Single(p => p.Id == SelectedId)); NotifyChanged(); return true;
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        if (!CanManage || SelectedProfile is not { } profile || profile.IsEnabled == enabled) return Task.CompletedTask;
        var updated = profile with { IsEnabled = enabled };
        _editor.SetSelectionBusy(true);
        return RunAsync(() => _store.SaveAndSelectAsync(updated, token), () =>
        {
            Profiles = Profiles.Select(account => account.Id == updated.Id ? updated : account).ToArray();
            _editor.SelectProfile(updated);
        }, enabled ? "Enabling account…" : "Disabling account…",
            enabled ? "Account enabled. Receive mail explicitly when ready." : "Account disabled. Cached mail, credentials and drafts are retained.",
            "Account state not changed", "Account state change canceled.", _ => _editor.SetSelectionBusy(false));
    }

    public Task MoveSelectedAsync(int direction, CancellationToken token = default)
    {
        if ((direction < 0 && !CanMoveEarlier) || (direction > 0 && !CanMoveLater) || direction == 0) return Task.CompletedTask;
        int index = Profiles.ToList().FindIndex(profile => profile.Id == SelectedId);
        var reordered = Profiles.ToArray(); int next = index + Math.Sign(direction);
        (reordered[index], reordered[next]) = (reordered[next], reordered[index]);
        _editor.SetSelectionBusy(true);
        return RunAsync(() => _store.ReorderAsync(reordered.Select(profile => profile.Id).ToArray(), token), () => Profiles = reordered,
            "Changing account order…", "Account order saved.", "Account order not changed", "Account order change canceled.",
            _ => _editor.SetSelectionBusy(false));
    }

    public bool RequestRemoval()
    {
        if (!CanRemove) return false;
        PendingRemoval = SelectedProfile; LastRemovalBackup = null; NotifyChanged(); return true;
    }

    public void CancelRemoval()
    {
        if (IsBusy)
        {
            try { _removalCancellation?.Cancel(); } catch (ObjectDisposedException) { }
            return;
        }
        PendingRemoval = null; NotifyChanged();
    }

    public Task ConfirmRemovalAsync(bool forgetPasswords, CancellationToken token = default)
    {
        if (!CanConfirmRemoval || _removal is null || PendingRemoval is not { } expected) return Task.CompletedTask;
        var remaining = Profiles.Where(profile => profile.Id != expected.Id).ToArray();
        var cancellation = _removalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        string? backup = null; bool released = false;
        void ReleaseInbox() { if (!released) { released = true; RemovalFinished?.Invoke(); } }
        _editor.SetSelectionBusy(true); RemovalStarting?.Invoke();
        return RunAsync(async () =>
        {
            if (!await _composer.SaveAsync().ConfigureAwait(false)) throw new IOException("The current draft could not be saved. Resolve its save error before removing an account.");
            await _removal.RemoveAsync(expected, forgetPasswords, path => backup = path, cancellation.Token).ConfigureAwait(false);
        }, () =>
        {
            Profiles = remaining; SelectedId = remaining.FirstOrDefault()?.Id; PendingRemoval = null;
            ReleaseInbox(); _editor.SelectProfile(remaining.FirstOrDefault());
        }, "Backing up and removing account…",
            forgetPasswords ? "Account removed and passwords forgotten. Downloaded mail and recovery backup are kept." : "Account removed. Passwords, downloaded mail and recovery backup are kept.",
            "Account removal did not finish",
            forgetPasswords ? "Removal canceled. The account remains listed; passwords may already have been forgotten. Any completed recovery backup is kept."
                : "Removal canceled. The account remains listed; passwords and downloaded mail were kept. Any completed recovery backup is kept.",
            error =>
            {
                LastRemovalBackup = backup; PendingRemoval = null;
                ReleaseInbox();
                if (error is not null) _editor.SelectProfile(expected);
                _editor.SetSelectionBusy(false); _removalCancellation = null; cancellation.Dispose();
            });
    }
}
