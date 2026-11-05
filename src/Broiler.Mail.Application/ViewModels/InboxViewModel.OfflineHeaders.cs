using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class InboxViewModel
{
    private OfflineHeaderWalk? _offlineHeaderWalk;
    private bool _lastFailureWasOfflineHeaders;
    public bool IsReceivingOfflineHeaders => IsBusy && _lastFailureWasOfflineHeaders && _loadingPage;
    public bool HasOfflineHeaderWalk => _offlineHeaderWalk is not null;
    public bool HasOfflineHeaderContinuation => _offlineHeaderWalk?.Snapshot().Count > 0;
    public int OfflineHeaderPagesSaved => _offlineHeaderWalk?.Pages ?? 0;
    public int OfflineHeadersSaved => _offlineHeaderWalk?.Headers ?? 0;
    public bool ProblemIsOfflineHeaderReceive => ListProblem is not null && _lastFailureWasOfflineHeaders;
    public bool CanReceiveOfflineHeaders => SupportsOfflineFolders && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingOfflineFolders &&
        !IsSearchActive && OfflineFoldersReady && OfflineFolders.Count > 0 && _account is { IsEnabled: true } &&
        (_offlineHeaderWalk is null || HasOfflineHeaderContinuation) && (receiver is IMailFolderReceiver || OfflineFolders.All(folder => folder == "INBOX"));
    public bool CanRestartOfflineHeaders => SupportsOfflineFolders && !_disposed && !_storageMaintenance && !IsBusy && !IsSavingOfflineFolders &&
        !IsSearchActive && OfflineFoldersReady && _account is not null && HasOfflineHeaderWalk;
    public string OfflineHeaderNotice => $"Receive headers separately: at most {PageSize} per selected folder per click (up to eight folders). " +
        $"{OfflineHeaderPagesSaved} pages / {OfflineHeadersSaved} headers confirmed saved in this walk. " +
        (_offlineHeaderWalk is null ? "Start from newest." : HasOfflineHeaderContinuation ? "Next header batch is available." : "This session's header walk is complete; Restart to recheck arrivals.") +
        " Server continuations are session-only; account/selection changes, Reload or reopening reset them. Saved headers remain. No automatic receive or body download; use Show saved headers to view the current folder. Restart body batches to include newly received headers.";

    public bool CanShowSavedHeaders => cache is not null && !_disposed && !_storageMaintenance && !IsBusy && !IsSearchActive && _account is not null;
    public Task ShowSavedHeadersAsync()
    {
        if (!CanShowSavedHeaders) return Task.CompletedTask;
        StopPending(); ResetMessages();
        CacheLoadTask = LoadCachedAsync(_account!, older: false); return CacheLoadTask;
    }

    public void RestartOfflineHeaders()
    {
        if (!CanRestartOfflineHeaders) return;
        _offlineHeaderWalk = null;
        if (_lastFailureWasOfflineHeaders) { ListProblem = null; _lastFailureWasOfflineHeaders = false; }
        Status = "Header walk reset to newest. Saved mail was kept; receive explicitly to start again."; Notify();
    }

    public Task ReceiveOfflineHeadersAsync()
    {
        if (!CanReceiveOfflineHeaders) return Task.CompletedTask;
        var account = _account!;
        var walk = _offlineHeaderWalk ??= new OfflineHeaderWalk(OfflineFolders);
        var plan = walk.PlanBatch(); var requests = plan.Requests; int generation = 0;
        _lastFailureWasDiscovery = _lastFailureWasDownload = _lastPageWasOlder = false;
        _loadingPage = true;
        return RunAsync(async token =>
        {
            foreach (var request in requests)
            {
                token.ThrowIfCancellationRequested();
                string folder = request.Key; var position = request.Value;
                var before = await cache!.GetLatestMailboxAsync(account.Id, folder, token).ConfigureAwait(false);
                if (position.Cursor is {} prior && before?.Key.UidValidity != prior.UidValidity)
                    throw new OfflineHeaderChangedException();
                var page = receiver is IMailFolderReceiver folderReceiver
                    ? await folderReceiver.GetMailboxAsync(account, folder, PageSize, position.Cursor, token).ConfigureAwait(false)
                    : await receiver.GetInboxAsync(account, PageSize, position.Cursor, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                ValidateOfflineHeaderPage(account.Id, folder, position, page);
                if ((await cache.GetLatestMailboxAsync(account.Id, folder, token).ConfigureAwait(false))?.Key != before?.Key)
                    throw new OfflineHeaderChangedException();
                await cache.SaveSummariesAsync(page.Mailbox!, page.Messages, DateTimeOffset.UtcNow, token).ConfigureAwait(false);
                // Only confirmed cache commits advance the session walk. A failed save safely repeats its page.
                walk.Accept(plan.Attempt, folder, page);
                dispatcher.Post(() =>
                {
                    if (_disposed || generation != _generation || _account?.Id != account.Id || !ReferenceEquals(walk, _offlineHeaderWalk)) return;
                    _cachedMailboxIds = _cachedMailboxIds.Append(folder).Distinct(StringComparer.Ordinal).ToArray(); UpdateFolders();
                    Status = $"Selected-folder headers: {walk.Pages} pages / {walk.Headers} headers saved. No bodies downloaded. Cancel keeps confirmed pages."; Notify();
                });
            }
            walk.CompleteBatch(plan.Attempt); return true;
        }, _ =>
        {
            Status = $"Selected-folder header batch complete: {walk.Pages} pages / {walk.Headers} headers saved in this walk. " +
                (HasOfflineHeaderContinuation ? "Receive next header batch to continue." : "This session's walk is complete.") +
                " Use Show saved headers to view the current folder. Restart body batches to include new headers. Download bodies separately; no bodies were fetched.";
        }, "Receiving selected-folder header pages…", InboxProblemScope.List, began: value => generation = value, offlineHeaders: true);
    }

    private sealed class OfflineHeaderChangedException() : Exception("The saved folder epoch changed. Restart the selected-folder header walk; saved mail was kept.");
    private sealed record HeaderPosition(MailInboxCursor? Cursor, uint? BeforeUid);
    private sealed class OfflineHeaderWalk(IEnumerable<string> folders)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, HeaderPosition> _remaining = folders.ToDictionary(folder => folder, _ => new HeaderPosition(null, null), StringComparer.Ordinal);
        private Dictionary<string, HeaderPosition>? _pending;
        private long _attempt;
        private int _pages, _headers;
        public int Pages { get { lock (_gate) return _pages; } }
        public int Headers { get { lock (_gate) return _headers; } }
        public Dictionary<string, HeaderPosition> Snapshot() { lock (_gate) return new(_remaining, StringComparer.Ordinal); }
        public (long Attempt, Dictionary<string, HeaderPosition> Requests) PlanBatch()
        { lock (_gate) { _pending ??= new(_remaining, StringComparer.Ordinal); return (++_attempt, new(_pending, StringComparer.Ordinal)); } }
        public void CompleteBatch(long attempt) { lock (_gate) { if (_attempt == attempt && _pending?.Count == 0) _pending = null; } }
        public void Accept(long attempt, string folder, MailInboxPage page)
        {
            lock (_gate)
            {
                // A canceled save can finish after a new attempt planned the same page.
                if (attempt != _attempt) return;
                if (page.Older is {} next) _remaining[folder] = new(next, page.Messages[^1].Key.Uid);
                else _remaining.Remove(folder);
                _pending!.Remove(folder);
                _pages++; _headers += page.Messages.Count;
            }
        }
    }
    private static void ValidateOfflineHeaderPage(AccountId account, string folder, HeaderPosition position, MailInboxPage page)
    {
        var mailbox = page.Mailbox;
        if (mailbox is null || mailbox.AccountId != account || mailbox.MailboxId != folder || page.Messages.Count > PageSize ||
            page.Messages.Any(row => !mailbox.Contains(row.Key)) || page.Messages.Select(row => row.Key.Uid).Distinct().Count() != page.Messages.Count ||
            page.Messages.Zip(page.Messages.Skip(1)).Any(pair => pair.First.Key.Uid <= pair.Second.Key.Uid) ||
            position.BeforeUid is {} before && page.Messages.Any(row => row.Key.Uid >= before))
            throw new InvalidDataException("Selected-folder header page has invalid scope, order or size.");
        if (position.Cursor is {} prior && (mailbox.UidValidity != prior.UidValidity || page.Messages.Count != Math.Min(PageSize, prior.NextIndex + 1)))
            throw new InvalidDataException("Selected-folder header page changed its continuation epoch or coverage.");
        if (page.Older is {} next)
        {
            int end = position.Cursor?.NextIndex ?? next.MessageCount - 1;
            if (next.AccountId != account || next.MailboxId != folder || next.UidValidity != mailbox.UidValidity || next.MessageCount <= 0 ||
                next.NextIndex < 0 || next.NextIndex >= next.MessageCount || page.Messages.Count != PageSize || next.NextIndex != end - page.Messages.Count ||
                next.UidNext == 0 || next.UidNext is {} uidNext && page.Messages.Any(row => row.Key.Uid >= uidNext) ||
                position.Cursor is {} previous && (next.MessageCount != previous.MessageCount || next.UidNext != previous.UidNext))
                throw new InvalidDataException("Selected-folder header continuation is invalid or did not advance.");
        }
        else if (position.Cursor is {} last && last.NextIndex >= PageSize)
            throw new InvalidDataException("Selected-folder header continuation ended prematurely.");
    }
}
