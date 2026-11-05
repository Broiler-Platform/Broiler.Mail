using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class AccountSelectionViewModel
{
    private CancellationTokenSource? _orphanCancellation;
    private IOrphanDownloadedMailStorage? OrphanStorage => _localMail as IOrphanDownloadedMailStorage;
    public bool SupportsOrphanMail => OrphanStorage is not null;
    public IReadOnlyList<AccountDownloadedMailInfo> OrphanMail { get; private set; } = [];
    public AccountDownloadedMailInfo? SelectedOrphanMail { get; private set; }
    public AccountDownloadedMailInfo? PendingOrphanCleanup { get; private set; }
    public bool OrphanMailHasMore { get; private set; }
    public string? LastOrphanCleanupBackup { get; private set; }
    public bool CanFindOrphanMail => SupportsOrphanMail && CanChange && !IsAdding && PendingRemoval is null && PendingLocalMailCleanup is null && PendingOrphanCleanup is null &&
        (Profiles.Count == 0 || !_editor.HasUnsavedChanges);
    public bool CanRequestOrphanCleanup => CanFindOrphanMail && !_composer.HasUnreadableDraft && SelectedOrphanMail is {} selected &&
        OrphanMail.Contains(selected) && !Profiles.Any(profile => profile.Id == selected.AccountId) && !HasActiveReceives();
    public bool CanConfirmOrphanCleanup => PendingOrphanCleanup is {} pending && pending == SelectedOrphanMail && CanChange &&
        !IsAdding && (Profiles.Count == 0 || !_editor.HasUnsavedChanges) && !_composer.HasUnreadableDraft && !Profiles.Any(profile => profile.Id == pending.AccountId) && !HasActiveReceives();
    public string OrphanMailNotice => $"{OrphanMail.Count} removed/unconfigured account IDs listed (maximum 64). " +
        (OrphanMailHasMore ? "More remain; clear selected entries and Reload to see subsequent IDs. " : "") +
        "List explicitly from this profile, including old epochs and empty mailbox heads. Counts are not account-specific disk usage. Nothing is downloaded or deleted by listing. Settings, passwords, all drafts, contacts, folder preferences and exported files are kept by cleanup. Recovery backups still contain cleared mail; this is not secure erasure.";
    private void ClearOrphanList()
    { OrphanMail = []; SelectedOrphanMail = PendingOrphanCleanup = null; OrphanMailHasMore = false; }
    public void SelectOrphanMail(AccountId? account)
    {
        if (!CanFindOrphanMail) return;
        SelectedOrphanMail = OrphanMail.SingleOrDefault(row => row.AccountId == account); NotifyChanged();
    }
    public Task FindOrphanMailAsync(CancellationToken token = default)
    {
        if (!CanFindOrphanMail || OrphanStorage is not {} storage) return Task.CompletedTask;
        ClearOrphanList(); OrphanDownloadedMailPage? page = null; _editor.SetSelectionBusy(true);
        return RunAsync(async () =>
        {
            page = await storage.FindOrphanDownloadedMailAsync(token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            if (page.Accounts.Count > 64 || page.Accounts.Select(row => row.AccountId).Distinct().Count() != page.Accounts.Count ||
                page.Accounts.Any(row => row.AccountId.Value == Guid.Empty || row.HeaderCount < 0 || row.DownloadedBodyCount < 0 || row.DownloadedBodyCount > row.HeaderCount || row.MailboxCount < 0 || Profiles.Any(profile => profile.Id == row.AccountId)))
                throw new InvalidDataException("Removed-account list returned invalid or configured identities. Reload account settings before trying again.");
        }, () => { OrphanMail = Array.AsReadOnly(page!.Accounts.ToArray()); OrphanMailHasMore = page.HasMore; },
            "Inspecting mail for removed accounts…", "Removed-account mail list updated. Select an ID explicitly; nothing was cleared.",
            "Removed-account mail inspection failed", "Removed-account mail inspection canceled.", _ => _editor.SetSelectionBusy(false));
    }
    public bool RequestOrphanCleanup()
    {
        if (!CanRequestOrphanCleanup) return false;
        PendingOrphanCleanup = SelectedOrphanMail; LastOrphanCleanupBackup = null; NotifyChanged(); return true;
    }
    public void CancelOrphanCleanup()
    {
        if (IsBusy) { try { _orphanCancellation?.Cancel(); } catch (ObjectDisposedException) {} return; }
        PendingOrphanCleanup = null; NotifyChanged();
    }
    public Task ConfirmOrphanCleanupAsync(CancellationToken token = default)
    {
        if (!CanConfirmOrphanCleanup || OrphanStorage is not {} storage || PendingOrphanCleanup is not {} expected) return Task.CompletedTask;
        var cancellation = _orphanCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        string? backup = null; AccountMailCleanupResult? result = null; bool released = false;
        void Release() { if (!released) { released = true; LocalMailCleanupFinished?.Invoke(result is not null); } }
        _editor.SetSelectionBusy(true); LocalMailCleanupStarting?.Invoke();
        return RunAsync(async () =>
        {
            if (!await _composer.SaveAsync().ConfigureAwait(false)) throw new IOException("Save the current draft successfully before clearing removed-account mail.");
            cancellation.Token.ThrowIfCancellationRequested();
            var completed = await storage.ClearOrphanDownloadedMailAsync(expected.AccountId,
                Path.Combine(_localMail!.ProfileDirectory, "backups", "before-orphan-mail-clear-" + Guid.NewGuid().ToString("N")), path => backup = path, cancellation.Token).ConfigureAwait(false);
            if (completed.AccountId != expected.AccountId) throw new InvalidDataException("Cleanup returned another account identity.");
            result = completed;
        }, () => { ClearOrphanList(); Release(); }, "Backing up and clearing removed-account mail…",
            "Selected removed-account mail was cleared. Settings, passwords, drafts and server mail were kept. Reload the list explicitly.",
            "Removed-account cleanup did not finish", "Removed-account cleanup canceled. Any completed recovery backup was kept.", _ =>
            { LastOrphanCleanupBackup = backup; ClearOrphanList(); Release(); _editor.SetSelectionBusy(false); _orphanCancellation = null; cancellation.Dispose(); });
    }
}
