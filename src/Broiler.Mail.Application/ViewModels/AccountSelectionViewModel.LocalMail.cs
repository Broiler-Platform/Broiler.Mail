using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class AccountSelectionViewModel
{
    private readonly IAccountDownloadedMailStorage? _localMail;
    private CancellationTokenSource? _localMailCancellation;
    public AccountProfile? PendingLocalMailCleanup { get; private set; }
    public AccountDownloadedMailInfo? LocalMailInfo { get; private set; }
    public string? LastLocalMailCleanupBackup { get; private set; }
    public event Action? LocalMailCleanupStarting;
    public event Action<bool>? LocalMailCleanupFinished;
    public bool SupportsLocalMailCleanup => _localMail is not null;
    public bool CanInspectLocalMail => _localMail is not null && CanManage;
    public bool CanRequestLocalMailCleanup => CanInspectLocalMail && !_composer.HasUnreadableDraft && !HasActiveReceives();
    public bool CanConfirmLocalMailCleanup => PendingLocalMailCleanup is not null && CanChange && !_editor.HasUnsavedChanges &&
        PendingLocalMailCleanup == SelectedProfile && !_composer.HasUnreadableDraft && !HasActiveReceives();
    public string LocalMailNotice => LocalMailInfo is { } info && info.AccountId == SelectedId
        ? $"Downloaded mail for {SelectedProfile?.EmailAddress}: {info.HeaderCount} saved headers, {info.DownloadedBodyCount} saved body previews, {info.MailboxCount} current mailbox heads. Counts include retained old-epoch headers. Shared database bytes are not an account-specific disk estimate."
        : "Inspect local downloaded-mail counts for this account. Clearing removes its saved headers/body previews only, after a verified recovery backup. Account settings, passwords, all drafts, contacts and folder preferences stay. Server mail is never changed.";
    public Task InspectLocalMailAsync(CancellationToken token = default)
    {
        if (!CanInspectLocalMail || SelectedProfile is not { } expected) return Task.CompletedTask;
        AccountDownloadedMailInfo? info = null; _editor.SetSelectionBusy(true);
        return RunAsync(async () =>
        {
            info = await _localMail!.GetAccountDownloadedMailInfoAsync(expected.Id, token).ConfigureAwait(false);
            if (info.AccountId != expected.Id) throw new InvalidDataException("Storage returned another account identity.");
        }, () => { if (SelectedProfile == expected) LocalMailInfo = info; }, "Inspecting downloaded mail…", "Local downloaded-mail counts updated.",
            "Downloaded-mail inspection failed", "Downloaded-mail inspection canceled.", _ => _editor.SetSelectionBusy(false));
    }
    public bool RequestLocalMailCleanup()
    {
        if (!CanRequestLocalMailCleanup) return false;
        PendingLocalMailCleanup = SelectedProfile; LastLocalMailCleanupBackup = null; NotifyChanged(); return true;
    }
    public void CancelLocalMailCleanup()
    {
        if (IsBusy) { try { _localMailCancellation?.Cancel(); } catch (ObjectDisposedException) { } return; }
        PendingLocalMailCleanup = null; NotifyChanged();
    }
    public Task ConfirmLocalMailCleanupAsync(CancellationToken token = default)
    {
        if (!CanConfirmLocalMailCleanup || _localMail is null || PendingLocalMailCleanup is not { } expected) return Task.CompletedTask;
        var cancellation = _localMailCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        string? backup = null; AccountMailCleanupResult? result = null; bool released = false;
        void Release() { if (!released) { released = true; LocalMailCleanupFinished?.Invoke(result is not null); } }
        _editor.SetSelectionBusy(true); LocalMailCleanupStarting?.Invoke();
        return RunAsync(async () =>
        {
            if (!await _composer.SaveAsync().ConfigureAwait(false)) throw new IOException("The current draft could not be saved. Resolve its save error before clearing downloaded mail.");
            var completed = await _localMail.ClearAccountDownloadedMailAsync(expected.Id,
                Path.Combine(_localMail.ProfileDirectory, "backups", "before-account-mail-clear-" + Guid.NewGuid().ToString("N")),
                path => backup = path, cancellation.Token).ConfigureAwait(false);
            if (completed.AccountId != expected.Id) throw new InvalidDataException("Cleanup returned another account identity.");
            result = completed;
        }, () =>
        {
            LocalMailInfo = null; PendingLocalMailCleanup = null; Release();
        }, "Backing up and clearing this account's downloaded mail…", "This account's downloaded mail was cleared. Account settings, passwords, drafts and server mail were kept.",
            "Downloaded-mail cleanup did not finish", "Downloaded-mail cleanup canceled before completion. Any completed recovery backup was kept.",
            _ =>
            {
                LastLocalMailCleanupBackup = backup; PendingLocalMailCleanup = null; Release();
                _editor.SetSelectionBusy(false); _localMailCancellation = null; cancellation.Dispose();
            });
    }
}
