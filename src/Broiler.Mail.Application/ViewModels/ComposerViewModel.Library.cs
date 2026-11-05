using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI.Forms;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class ComposerViewModel
{
    private IReadOnlyList<DraftSnapshot> _savedDrafts = [];
    private Guid? _removeSaved;
    public int SavedDraftLibraryCount => _savedDrafts.Count;
    public bool SupportsDraftLibrary => _draftStore is IDraftLibraryStore;
    public IReadOnlyList<DraftSnapshot> SavedDrafts => Array.AsReadOnly(_savedDrafts.Where(row => row.Draft.AccountId == _account?.Id).ToArray());
    public bool CanManageSavedDrafts => SupportsDraftLibrary && !_disposed && !_closing && !_profileStorageBusy && _loadError is null && !IsBusy;
    public bool CanParkDraft => CanManageSavedDrafts && HasDraft && _savedDrafts.Count < 20;
    public Guid? SavedDraftRemoval => _removeSaved;
    public Task<bool> ParkDraftAsync() => CanParkDraft ? ChangeDraftLibraryAsync((store, revision) => store.ParkAsync(revision), "Draft saved in the library. Start another message when ready.") : Task.FromResult(false);
    public Task<bool> ResumeDraftAsync(Guid id) => CanManageSavedDrafts && SavedDrafts.Any(row => row.Draft.Id == id)
        ? ChangeDraftLibraryAsync((store, revision) => store.ActivateAsync(revision, id), "Saved draft resumed. Any previously open draft was saved in the library; nothing was sent.") : Task.FromResult(false);
    public bool RequestSavedDraftRemoval(Guid id)
    {
        if (!CanManageSavedDrafts || !SavedDrafts.Any(row => row.Draft.Id == id)) return false;
        _removeSaved = id; Notify(); return true;
    }
    public void KeepSavedDraft() { _removeSaved = null; Notify(); }
    public Task<bool> ConfirmSavedDraftRemovalAsync()
    {
        if (!CanManageSavedDrafts || _removeSaved is not { } id || !SavedDrafts.Any(row => row.Draft.Id == id)) return Task.FromResult(false);
        _removeSaved = null;
        return ChangeDraftLibraryAsync((store, revision) => store.RemoveSavedAsync(revision, id), "Selected saved draft removed locally. No server mail changed.");
    }
    private async Task<bool> ChangeDraftLibraryAsync(Func<IDraftLibraryStore, long, Task<DraftStoreState>> change, string success)
    {
        IsBusy = true; _removeSaved = null; Notify();
        DraftStoreState? changed = null; string? failure = null;
        try
        {
            if (!await _journal.FlushAsync().ConfigureAwait(false)) throw new IOException("The active draft could not be saved. Its text remains open.");
            changed = await change((IDraftLibraryStore)_draftStore, _journal.SavedRevision).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { failure = error.Message; }
        await OnUiAsync(() =>
        {
            if (_disposed) return;
            if (changed is not null)
            {
                _journal.Changed -= OnStorageChanged; _journal = new DraftJournal(_draftStore, changed); _journal.Changed += OnStorageChanged;
                _savedDrafts = changed.SavedDrafts ?? []; var snapshot = changed.Draft;
                _seed = snapshot?.Draft; To = snapshot?.ToText ?? ""; Cc = snapshot?.CcText ?? ""; Bcc = snapshot?.BccText ?? "";
                Subject = snapshot?.Draft.Subject ?? ""; PlainText = snapshot?.Draft.PlainText ?? "";
                SubmissionState = snapshot?.State == DraftSubmissionState.Sending ? DraftSubmissionState.Unknown : snapshot?.State ?? DraftSubmissionState.Editing;
                SentCopy = snapshot?.SentCopy == SentCopyState.Pending ? SentCopyState.Unknown : snapshot?.SentCopy ?? SentCopyState.NotRequested;
                SentCopyFolder = snapshot?.SentCopyFolder;
                if (snapshot?.State == DraftSubmissionState.Sending || snapshot?.SentCopy == SentCopyState.Pending) Persist();
            }
            IsBusy = false; Status = failure ?? success; StatusKind = failure is null ? FeedbackKind.Information : FeedbackKind.Error; Notify();
        }).ConfigureAwait(false);
        return changed is not null;
    }
}
