using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class InboxViewModel
{
    private CancellationTokenSource _offlineContext = new();
    private int _offlinePreferenceGeneration;
    private bool _lastFailureWasSelectedFolders;
    // Null starts at newest; an empty dictionary means the current saved-header walk is complete.
    private IReadOnlyDictionary<string, MailMessageKey>? _offlineNextBatch;
    public bool HasOfflineFolderContinuation => _offlineNextBatch is { Count: > 0 };
    public bool HasCompletedOfflineFolderBatch => _offlineNextBatch is not null;
    public bool CanRestartOfflineFolderBatches => SupportsOfflineFolders && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingOfflineFolders && OfflineFoldersReady && _account is not null && _offlineNextBatch is not null;
    public async Task<bool> RestartOfflineFolderBatchesAsync()
    {
        if (!CanRestartOfflineFolderBatches) return false;
        var account = _account!.Id; var selection = OfflineFolders.ToArray(); int generation = _offlinePreferenceGeneration;
        IsSavingOfflineFolders = true; Notify(); string? error = null;
        try
        {
            if (offlineFolders is IOfflineFolderProgressStore progress)
                await progress.SaveProgressAsync(account, selection, null, _offlineContext.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { error = failure.Message; }
        PostOfflineSelection(account, generation, () =>
        {
            IsSavingOfflineFolders = false;
            if (error is not null) { OfflineFolderNotice = "Offline progress was not reset. " + error; return; }
            _offlineNextBatch = null; OfflineFolderNotice = SelectionNotice();
            if (_lastFailureWasSelectedFolders) { ListProblem = null; _lastFailureWasSelectedFolders = _lastFailureWasDownload = false; }
            Status = "Offline-folder batches reset to newest saved headers. Download explicitly to recheck; saved mail was kept.";
        });
        return error is null;
    }
    private sealed class OfflineBatchEpochChangedException() : Exception("A saved folder epoch changed. Restart offline batches from newest saved headers; receive headers first if needed. Saved mail was kept.");
    public bool SupportsOfflineFolders => cache is not null && offlineFolders is not null;
    public IReadOnlyList<string> OfflineFolders { get; private set; } = [];
    public bool OfflineFoldersReady { get; private set; }
    public bool IsSavingOfflineFolders { get; private set; }
    public string OfflineFolderNotice { get; private set; } = "Offline-folder selections are not loaded.";
    public Task OfflineFolderLoadTask { get; private set; } = Task.CompletedTask;
    public bool IsSelectedFolderOffline => OfflineFolders.Contains(SelectedMailboxId, StringComparer.Ordinal);
    public bool CanToggleOfflineFolder => SupportsOfflineFolders && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingOfflineFolders && OfflineFoldersReady &&
        _account is not null && Folders.Any(folder => folder.MailboxId == SelectedMailboxId && folder.IsSelectable) &&
        (IsSelectedFolderOffline || OfflineFolders.Count < 8) && (SelectedMailboxId == "INBOX" || receiver is IMailFolderReceiver);
    public bool CanDownloadOfflineFolders => SupportsOfflineFolders && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingOfflineFolders && !IsSearchActive &&
        OfflineFoldersReady && OfflineFolders.Count > 0 && (_offlineNextBatch is null || _offlineNextBatch.Count > 0) && _account is { IsEnabled: true } && (receiver is IMailFolderReceiver || OfflineFolders.All(name => name == "INBOX"));
    public bool ProblemIsSelectedFolderDownload => ListProblem is not null && _lastFailureWasSelectedFolders;
    public Task ReloadOfflineFolderSelectionAsync()
    {
        if (_disposed || _storageMaintenance || IsBusy || IsSavingOfflineFolders || _account is null || !SupportsOfflineFolders) return Task.CompletedTask;
        LoadOfflineFolderSelection(_account); Notify(); return OfflineFolderLoadTask;
    }
    private void LoadOfflineFolderSelection(AccountProfile? account)
    {
        _offlineContext.Cancel(); _offlineContext.Dispose(); _offlineContext = new(); int generation = ++_offlinePreferenceGeneration;
        _offlineNextBatch = null; _offlineHeaderWalk = null;
        OfflineFolders = []; OfflineFoldersReady = false; IsSavingOfflineFolders = false;
        OfflineFolderNotice = "Choose up to eight folders. Download selected offline folders explicitly uses at most 500 saved headers per folder; it does not receive older headers or start automatic sync.";
        UpdateFolders();
        OfflineFolderLoadTask = account is not null && SupportsOfflineFolders ? ReadOfflineSelectionAsync(account.Id, generation, _offlineContext.Token) : Task.CompletedTask;
    }
    private async Task ReadOfflineSelectionAsync(AccountId account, int generation, CancellationToken token)
    {
        IReadOnlyList<string>? rows = null; IReadOnlyList<MailMessageKey>? remaining = null; string? error = null;
        try
        {
            rows = await offlineFolders!.LoadAsync(account, token).ConfigureAwait(false);
            if (offlineFolders is IOfflineFolderProgressStore progress)
                remaining = await progress.LoadProgressAsync(account, rows, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { error = failure.Message; }
        PostOfflineSelection(account, generation, () =>
        {
            if (error is null && rows is not null) { OfflineFolders = rows; _offlineNextBatch = remaining?.ToDictionary(key => key.MailboxId, StringComparer.Ordinal); OfflineFoldersReady = true; UpdateFolders(); }
            OfflineFolderNotice = error is null ? SelectionNotice() : "Offline-folder selections could not be loaded; editing and batch downloads are disabled until Reload succeeds. " + error;
        });
    }
    private string SelectionNotice()
    {
        string progress = _offlineNextBatch is null ? "Start at newest saved headers." : HasOfflineFolderContinuation
            ? $"Next batch is available for {_offlineNextBatch.Count} folders." : "Current saved-header walk is complete; Restart to recheck newer arrivals.";
        return $"{OfflineFolders.Count}/8 offline folders selected for this account. Selected: {string.Join(", ", OfflineFolders)}. Manual batches: at most 500 saved headers per folder per click. {progress} {(offlineFolders is IOfflineFolderProgressStore ? "Progress is saved across restart/reload; selection changes reset it." : "Progress is session-only for this host.")} No header receive, automatic refresh or eviction. Downloads retain supported body previews, complete decoded reader text within its retention limit and CID resources, not original MIME or general attachments.";
    }
    private void PostOfflineSelection(AccountId account, int generation, Action commit)
    {
        if (_disposed) return;
        try { dispatcher.Post(() => { if (_disposed || generation != _offlinePreferenceGeneration || _account?.Id != account) return; commit(); Notify(); }); }
        catch (ObjectDisposedException) { }
    }
    public async Task ToggleOfflineFolderAsync()
    {
        if (!CanToggleOfflineFolder) return;
        var account = _account!.Id; string folder = SelectedMailboxId; int generation = _offlinePreferenceGeneration; var token = _offlineContext.Token;
        string[] next = IsSelectedFolderOffline ? OfflineFolders.Where(name => name != folder).ToArray() : OfflineFolders.Append(folder).ToArray();
        IsSavingOfflineFolders = true; OfflineFolderNotice = "Saving this account's offline-folder selection…"; Notify(); string? error = null;
        try { await offlineFolders!.SaveAsync(account, next, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { error = failure.Message; }
        PostOfflineSelection(account, generation, () =>
        {
            IsSavingOfflineFolders = false;
            if (error is null) { _offlineNextBatch = null; _offlineHeaderWalk = null; OfflineFolders = Array.AsReadOnly(next); UpdateFolders(); OfflineFolderNotice = SelectionNotice(); }
            else OfflineFolderNotice = "Offline-folder selection was not confirmed saved. Previous selection is kept. " + error;
        });
    }
    /// <summary>Explicit bounded multi-folder body download over current saved headers only.</summary>
    public Task DownloadOfflineFoldersAsync()
    {
        if (!CanDownloadOfflineFolders) return Task.CompletedTask;
        var account = _account!; var cursors = _offlineNextBatch; var selection = OfflineFolders.ToArray();
        string[] folders = OfflineFolders.Where(folder => cursors is null || cursors.ContainsKey(folder)).ToArray(); int generation = 0;
        _lastFailureWasDiscovery = _lastPageWasOlder = false; _lastFailureWasDownload = true; _loadingPage = true;
        DownloadCompleted = DownloadTotal = 0;
        return RunAsync(async token =>
        {
            var targets = new List<MailMessageKey>(); var next = new Dictionary<string, MailMessageKey>(StringComparer.Ordinal); int capped = 0, empty = 0;
            foreach (string folder in folders)
            {
                token.ThrowIfCancellationRequested();
                var head = await cache!.GetLatestMailboxAsync(account.Id, folder, token).ConfigureAwait(false);
                MailMessageKey? before = cursors is not null && cursors.TryGetValue(folder, out var cursor) ? cursor : null;
                if (before is { } prior && (head is null || !head.Key.Contains(prior))) throw new OfflineBatchEpochChangedException();
                if (head is null) { empty++; continue; }
                var headers = await cache.ReadSummariesAsync(head.Key, MaximumLoadedMessages + 1, before?.Uid, token).ConfigureAwait(false);
                if (headers.Count > MaximumLoadedMessages) { capped++; next.Add(folder, headers[MaximumLoadedMessages - 1].Key); }
                if (headers.Count == 0) empty++;
                foreach (var header in headers.Take(MaximumLoadedMessages))
                {
                    if (!head.Key.Contains(header.Key)) throw new InvalidDataException("Offline selection returned another mailbox identity.");
                    targets.Add(header.Key);
                }
            }
            dispatcher.Post(() => { if (!_disposed && generation == _generation) { DownloadTotal = targets.Count; Notify(); } });
            for (int index = 0; index < targets.Count; index++)
            {
                token.ThrowIfCancellationRequested(); var key = targets[index]; var mailbox = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
                var head = await cache!.GetLatestMailboxAsync(account.Id, key.MailboxId, token).ConfigureAwait(false);
                if (head?.Key != mailbox) throw new InvalidDataException("Saved mailbox epoch changed; retry the selected-folder batch.");
                if (await cache.ReadBodyAsync(mailbox, key.Uid, token).ConfigureAwait(false) is null)
                {
                    var body = await receiver.GetBodyAsync(account, key, token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                    if (body.Key != key) throw new InvalidDataException("The receiver returned another message identity.");
                    if ((await cache.GetLatestMailboxAsync(account.Id, key.MailboxId, token).ConfigureAwait(false))?.Key != mailbox)
                        throw new InvalidDataException("Mailbox epoch changed during download; the returned body was not saved.");
                    await cache.SaveBodyAsync(mailbox, body, token).ConfigureAwait(false);
                }
                int completed = index + 1;
                dispatcher.Post(() =>
                {
                    if (_disposed || generation != _generation || _account?.Id != account.Id) return;
                    DownloadCompleted = completed;
                    Messages = Messages.Select(row => row.Key == key ? row with { IsBodyCached = true } : row).ToArray();
                    if (SelectedMessage?.Key == key) SelectedMessage = SelectedMessage with { IsBodyCached = true };
                    Status = $"Selected-folder download: {completed}/{targets.Count} saved bodies available; folder {key.MailboxId}. Cancel keeps confirmed saves."; Notify();
                });
            }
            token.ThrowIfCancellationRequested();
            if (offlineFolders is IOfflineFolderProgressStore progress)
                await progress.SaveProgressAsync(account.Id, selection, next.Values.ToArray(), token).ConfigureAwait(false);
            return (Count: targets.Count, Capped: capped, Empty: empty, Next: next);
        }, result =>
        {
            // Publish cursors only after every planned body save succeeds and the operation generation still matches.
            _offlineNextBatch = result.Next; OfflineFolderNotice = SelectionNotice();
            Status = $"Selected-folder download complete: {result.Count} saved bodies available across {folders.Length} folders in this batch. {result.Capped} folders exceeded the 500-header batch limit; {result.Empty} had no saved headers. No headers were received. " +
                (HasOfflineFolderContinuation ? "Download next offline batch to continue into older saved headers." : "Current saved-header walk is complete.") + " Restart to recheck newer arrivals; receive headers explicitly for missing coverage.";
        },
            "Planning selected-folder download from saved headers…", InboxProblemScope.List, downloading: true, began: value => generation = value, selectedFoldersDownload: true);
    }
}
