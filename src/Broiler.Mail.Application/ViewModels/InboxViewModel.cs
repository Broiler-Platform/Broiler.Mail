using System.Globalization;
// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   37
// Annotated:        17/37
// Exempt:           21
// Human-reviewed:   0/37
// IP risk:          Low
// Security risk:    Medium
// Criteria:         15/0
// Resource impact:  7/10 max
// Unverified:       37
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Owns the in-memory inbox. All state changes are published on the UI dispatcher.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=7; Fingerprint=8045C6
// Broiler-Falsified-If: a body fetched for an earlier selection is committed after SelectAsync has picked another message, so Body no longer belongs to SelectedMessage
// Broiler-Human:        PENDING
/// <summary>Which pane an inbox problem belongs to, so its explanation and Retry appear beside it.</summary>
public enum InboxProblemScope { None, List, Message }

/// <summary>Why an inbox operation failed, or that the user canceled it, as its pane explains it.</summary>
public sealed record InboxProblem(string Text, bool IsCancellation);

public sealed partial class InboxViewModel(IMailReceiver receiver, IUiDispatcher dispatcher, IMailCache? cache = null,
    OfflineDownloadPolicy downloadPolicy = OfflineDownloadPolicy.OnDemand, IOfflineFolderStore? offlineFolders = null, IAccountNavigationStore? navigation = null, IMessageSourceExports? sourceExports = null) : IDisposable
{
    public IMailCache? LocalCache => cache;
    internal IMailReceiver ReceiveService => receiver;
    public IMessageSourceExports? SourceExports => sourceExports;
    public IMailSourceReceiver? SourceReceiver => receiver as IMailSourceReceiver;
    public AccountProfile? CurrentAccountProfile => _account;
    public bool SupportsSourceExport => SourceExports is not null && SourceReceiver is not null;
    public bool CanDownloadOriginalSource => SupportsSourceExport && CanReceive && SelectedMessage is not null;
    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=Low; Resources=0; Fingerprint=75CF89
    // Broiler-Falsified-If: PageSize is larger than ImapMailReceiver.MaximumPageSize, so every receive is refused with ArgumentOutOfRangeException
    // Broiler-Human:        PENDING
    public const int PageSize = 50;
    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=Medium; Resources=0; Fingerprint=FC3D90
    // Broiler-Falsified-If: repeated Load older leaves more than MaximumLoadedMessages plus one page of summaries in Messages
    // Broiler-Human:        PENDING
    public const int MaximumLoadedMessages = 500;
    private IReadOnlyList<MailFolder>? _serverFolders;
    private IReadOnlyList<string> _cachedMailboxIds = [];
    private bool _lastFailureWasDiscovery;
    public string SelectedMailboxId { get; private set; } = "INBOX";
    public IReadOnlyList<MailFolder> Folders { get; private set; } = [new("INBOX", null, true, [])];
    public bool SupportsFolders => receiver is IMailFolderReceiver || cache is IMailFolderCache;
    public bool CanSwitchFolder => !_disposed && !_storageMaintenance && !_loadingNavigation && _account is not null;
    public bool CanRefreshFolders => !_disposed && !_storageMaintenance && !IsBusy &&
        _account is { IsEnabled: true } && receiver is IMailFolderReceiver;
    public bool ProblemIsFolderDiscovery => ListProblem is not null && _lastFailureWasDiscovery;
    public bool IsSavedOnlyFolder(string mailboxId) => mailboxId != "INBOX" &&
        _cachedMailboxIds.Contains(mailboxId, StringComparer.Ordinal) &&
        (_serverFolders is null || !_serverFolders.Any(folder => folder.MailboxId == mailboxId));
    public string FolderNotice => _serverFolders is null
        ? "Folder list includes saved mailboxes. Refresh folders to discover server mailboxes. Selecting a folder opens saved headers; Receive mail checks the server."
        : "Server folders and saved mailboxes are shown. Saved only means the folder has not been confirmed in the latest server list. Receive mail checks the selected folder.";

    public IReadOnlyList<MailFolderRoleMapping> FolderRoles { get; private set; } = [];
    public string FolderRoleNotice => "Folder roles: " + string.Join("; ", FolderRoles.Select(mapping =>
        $"{mapping.Role}: {mapping.MailboxId ?? "unassigned"}{(mapping.IsOverride ? " (override)" : "")}{(mapping.Problem is { } problem ? " — " + problem : "")}"));
    public string FolderRoleLabel(string mailboxId) => string.Join(", ", FolderRoles.Where(mapping => mapping.MailboxId == mailboxId)
        .Select(mapping => mapping.Role + (mapping.IsOverride ? " override" : "") + (!mapping.IsConfirmed ? " (unverified)" : "")));

    private void UpdateFolders()
    {
        FolderRoles = _account is null ? [] : MailFolderRoles.Resolve(_account.FolderOverrides, _serverFolders);
        var folders = (_serverFolders ?? []).ToList();
        foreach (string name in new[] { "INBOX", SelectedMailboxId }.Concat(_cachedMailboxIds).Concat(PinnedFolders).Concat(OfflineFolders).Concat(FolderRoles.Where(mapping => mapping.IsOverride && mapping.MailboxId is not null).Select(mapping => mapping.MailboxId!)))
            if (!folders.Any(folder => folder.MailboxId == name)) folders.Add(new(name, null, true, []));
        Folders = folders.OrderBy(folder => folder.MailboxId == "INBOX" ? 0 : 1)
            .ThenBy(folder => folder.MailboxId, StringComparer.Ordinal).ToArray();
        MoveDestinations = (_serverFolders ?? []).Where(folder => folder.IsSelectable && folder.MailboxId != SelectedMailboxId).ToArray();
        if (!MoveDestinations.Any(folder => folder.MailboxId == MoveDestinationMailboxId)) MoveDestinationMailboxId = null;
    }

    private bool _deleting;
    private AccountProfile? _deleteProfile;
    private MailMessageKey? _deleteCacheRetry;
    public bool SupportsPermanentDelete => receiver is IMailPermanentDeleter;
    public bool IsDeleting => IsBusy && _deleting;
    public MailMessageSummary? PendingPermanentDelete { get; private set; }
    public string? DeleteNotice { get; private set; }
    public string PermanentDeleteConfirmationText => PendingPermanentDelete is { } message && _deleteProfile is { } account
        ? $"Permanently delete '{message.Subject}' from {message.Key.MailboxId} for {account.DisplayName} <{account.EmailAddress}>?\nThis cannot be undone. It removes the server message and its active cached copy after confirmation. Backups may retain copies." : "";
    public bool IsPermanentDeleteFolder => FolderRoles.Any(role => role.Role == MailFolderRole.Trash && role.IsConfirmed && role.MailboxId == SelectedMailboxId);
    private bool CanDeleteCurrent => !NeedsCacheRepair && SupportsPermanentDelete && CanReceive && IsPermanentDeleteFolder && SelectedMessage is not null &&
        _moveCacheRetry is null && _deleteCacheRetry is null && (cache is null || cache is IMailDeleteCache);
    public bool CanRequestPermanentDelete => CanDeleteCurrent && PendingPermanentDelete is null;
    public bool CanConfirmPermanentDelete => CanDeleteCurrent && PendingPermanentDelete is { } pending && _deleteProfile == _account &&
        SelectedMessage?.Key == pending.Key;
    public bool CanRetryDeleteCache => !IsSearchActive && !_disposed && !_storageMaintenance && !IsBusy && cache is IMailDeleteCache &&
        _deleteCacheRetry is { } key && key.AccountId == _account?.Id;
    private const string UncertainDelete = "Deletion canceled or interrupted. The message may be gone or marked for deletion. Refresh the folder; no deletion is queued for retry.";
    public bool RequestPermanentDelete()
    {
        if (!CanRequestPermanentDelete) return false;
        PendingPermanentDelete = SelectedMessage; _deleteProfile = _account; Notify(); return true;
    }
    public void CancelPermanentDeleteConfirmation()
    { PendingPermanentDelete = null; _deleteProfile = null; Notify(); }
    private void RemoveDeletedFromView(MailMessageKey key)
    {
        Messages = Messages.Where(message => message.Key != key).ToArray();
        if (SelectedMessage?.Key == key) { SelectedMessage = null; Body = null; FlagChangeNotice = null; MessageProblem = null; }
        if (SelectedMailboxId == key.MailboxId)
        { _older = null; _hasOlderCached = false; if (Messages.Count == 0) HasLoaded = false; }
    }
    public Task ConfirmPermanentDeleteAsync()
    {
        if (!CanConfirmPermanentDelete || PendingPermanentDelete is not { } pending) return Task.CompletedTask;
        var account = _account!; var key = pending.Key;
        PendingPermanentDelete = null; _deleteProfile = null; _undoMove = null;
        _loadingPage = true; DeleteNotice = "Permanently deleting the confirmed message…";
        return RunAsync(async token =>
        {
            var result = await ((IMailPermanentDeleter)receiver).PermanentlyDeleteAsync(account, key, token).ConfigureAwait(false);
            if (result.Key != key || !Enum.IsDefined(result.Status)) throw new InvalidDataException("The deletion result has a different identity.");
            bool cacheSaved = cache is null;
            if (result.Status == MailDeleteStatus.Confirmed && cache is IMailDeleteCache deleteCache)
            {
                try { await deleteCache.ReconcilePermanentDeleteAsync(key, token).ConfigureAwait(false); cacheSaved = true; }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { cacheSaved = false; }
            }
            return (Result: result, CacheSaved: cacheSaved);
        }, completed =>
        {
            if (completed.Result.Status == MailDeleteStatus.Confirmed)
            {
                RemoveDeletedFromView(key);
                if (!completed.CacheSaved) _deleteCacheRetry = key;
            }
            DeleteNotice = Status = completed.Result.Explanation + (completed.Result.Status == MailDeleteStatus.Confirmed
                ? completed.CacheSaved ? cache is null ? " Backups may retain copies." : " The active cached copy was removed. Backups may retain copies."
                    : " Server deletion is confirmed, but the cached copy remains. Retry local deletion cleanup; it sends no server deletion."
                : " Local mail was kept. Refresh before another deliberate attempt.");
            if (completed.Result.Status == MailDeleteStatus.Confirmed) DeleteNotice = Status += " Receive mail to refresh folder membership and paging.";
        }, "Permanently deleting the confirmed message…", InboxProblemScope.Message, deleting: true);
    }
    public Task RetryDeleteCacheAsync()
    {
        if (!CanRetryDeleteCache || _deleteCacheRetry is not { } key) return Task.CompletedTask;
        _loadingPage = true;
        return RunAsync(async token =>
        { await ((IMailDeleteCache)cache!).ReconcilePermanentDeleteAsync(key, token).ConfigureAwait(false); return true; }, _ =>
        {
            _deleteCacheRetry = null; RemoveDeletedFromView(key);
            DeleteNotice = Status = "Local deletion cleanup saved. No server deletion was sent. Backups may retain copies.";
            if (SelectedMailboxId == key.MailboxId && IsShowingCachedMail && _account is { } account) CacheLoadTask = LoadCachedAsync(account, older: false);
        }, "Cleaning up the confirmed deletion locally…", InboxProblemScope.List);
    }

    private bool _moving;
    private (MailMessageKey Source, MailMessageKey Destination)? _undoMove;
    private (MailMessageKey Source, MailMessageKey Destination)? _moveCacheRetry;
    public bool SupportsMoves => receiver is IMailMover;
    public bool IsMoving => IsBusy && _moving;
    public string? MoveNotice { get; private set; }
    public IReadOnlyList<MailFolder> MoveDestinations { get; private set; } = [];
    public string? MoveDestinationMailboxId { get; private set; }
    public bool CanMoveSelected => !NeedsCacheRepair && SupportsMoves && CanReceive && SelectedMessage is not null && _moveCacheRetry is null && _deleteCacheRetry is null && PendingPermanentDelete is null;
    public bool CanMoveToDestination => CanMoveSelected && MoveDestinations.Any(folder => folder.MailboxId == MoveDestinationMailboxId);
    public bool CanArchive => CanMoveSelected && ConfirmedMoveRole(MailFolderRole.Archive) is not null;
    public bool CanTrash => CanMoveSelected && ConfirmedMoveRole(MailFolderRole.Trash) is not null;
    public bool CanUndoMove => !NeedsCacheRepair && !IsSearchActive && !_disposed && !_storageMaintenance && !IsBusy && SupportsMoves && _account is { IsEnabled: true } account &&
        _moveCacheRetry is null && _deleteCacheRetry is null && PendingPermanentDelete is null && _undoMove is { } move && move.Source.AccountId == account.Id &&
        (_serverFolders?.Any(folder => folder.MailboxId == move.Source.MailboxId && folder.IsSelectable) == true);
    public Task UndoLastMoveAsync()
    {
        if (!CanUndoMove || _undoMove is not { } move) return Task.CompletedTask;
        return RunMoveAsync(_account!, move.Destination, move.Source.MailboxId, undo: true);
    }
    public bool CanRetryMoveCache => !IsSearchActive && !_disposed && !_storageMaintenance && !IsBusy && cache is IMailMoveCache &&
        _moveCacheRetry is { } retry && retry.Source.AccountId == _account?.Id;
    private const string UncertainMove = "Move canceled or interrupted. The server may have moved the message. Check both folders; no move is queued for retry.";
    private string? ConfirmedMoveRole(MailFolderRole role) => FolderRoles.FirstOrDefault(mapping =>
        mapping.Role == role && mapping.IsConfirmed && mapping.MailboxId != SelectedMailboxId)?.MailboxId;
    public void ChooseMoveDestination(string? mailboxId)
    {
        if (IsBusy || !MoveDestinations.Any(folder => folder.MailboxId == mailboxId)) return;
        MoveDestinationMailboxId = mailboxId; Notify();
    }
    public Task ArchiveSelectedAsync() => MoveSelectedAsync(ConfirmedMoveRole(MailFolderRole.Archive));
    public Task TrashSelectedAsync() => MoveSelectedAsync(ConfirmedMoveRole(MailFolderRole.Trash));
    public Task MoveSelectedAsync(string? destinationMailboxId)
    {
        if (!CanMoveSelected || !MoveDestinations.Any(folder => folder.MailboxId == destinationMailboxId) || _moveCacheRetry is not null)
            return Task.CompletedTask;
        return RunMoveAsync(_account!, SelectedMessage!.Key, destinationMailboxId!, undo: false);
    }
    private Task RunMoveAsync(AccountProfile account, MailMessageKey source, string destinationMailboxId, bool undo)
    {
        // A later or uncertain operation must not leave a replayable old undo entry.
        _undoMove = null;
        _loadingPage = true; MoveNotice = undo ? $"Undoing the move back to {destinationMailboxId}…" : $"Moving to {destinationMailboxId}…";
        return RunAsync(async token =>
        {
            var result = await ((IMailMover)receiver).MoveAsync(account, source, destinationMailboxId!, token).ConfigureAwait(false);
            if (result.Source != source || result.DestinationMailboxId != destinationMailboxId || !Enum.IsDefined(result.Status) ||
                result.Status == MailMoveStatus.Confirmed && (result.Destination is not { } target || target.AccountId != account.Id ||
                    target.MailboxId != destinationMailboxId || target.Uid == 0 || target.UidValidity == 0))
                throw new InvalidDataException("The mover returned a different operation or invalid destination identity.");
            bool cacheSaved = cache is null;
            if (result.Status == MailMoveStatus.Confirmed && cache is IMailMoveCache moveCache)
            {
                try { await moveCache.ReconcileMoveAsync(source, result.Destination!.Value, token).ConfigureAwait(false); cacheSaved = true; }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { cacheSaved = false; }
            }
            return (Result: result, CacheSaved: cacheSaved);
        }, completed =>
        {
            var result = completed.Result;
            if (result.Status == MailMoveStatus.Confirmed)
            {
                Messages = Messages.Where(message => message.Key != source).ToArray();
                if (SelectedMessage?.Key == source) { SelectedMessage = null; Body = null; FlagChangeNotice = null; MessageProblem = null; }
                if (SelectedMailboxId == source.MailboxId || SelectedMailboxId == destinationMailboxId)
                { _older = null; _hasOlderCached = false; }
                if (!undo && result.Destination is { } moved) _undoMove = (source, moved);
                if (completed.CacheSaved && cache is not null)
                    _cachedMailboxIds = _cachedMailboxIds.Append(destinationMailboxId!).Distinct(StringComparer.Ordinal).ToArray();
                if (!completed.CacheSaved && result.Destination is { } destination) _moveCacheRetry = (source, destination);
                UpdateFolders();
            }
            MoveNotice = (undo ? $"Undo move to {destinationMailboxId}: " : $"Move to {destinationMailboxId}: ") + result.Explanation + (result.Status == MailMoveStatus.Confirmed
                ? completed.CacheSaved ? cache is null ? " Receive mail to refresh folder membership and paging."
                    : " Saved mail was reconciled. Receive mail to refresh folder membership and paging."
                    : " The server move is confirmed, but local mail was not reconciled. Retry the local move update; it never sends MOVE again."
                : " Local mail was kept. Check both folders before another explicit move.");
            if (result.Status == MailMoveStatus.Confirmed && !undo && CanUndoMove) MoveNotice += " Undo last move is available for this session.";
            Status = MoveNotice;
            if (result.Status == MailMoveStatus.Confirmed && completed.CacheSaved && cache is not null &&
                SelectedMailboxId == destinationMailboxId && _account is { } current)
                CacheLoadTask = LoadCachedAsync(current, older: false);
        }, undo ? "Undoing last move…" : "Moving message…", InboxProblemScope.Message, moving: true);
    }
    public Task RetryMoveCacheAsync()
    {
        if (!CanRetryMoveCache || _moveCacheRetry is not { } retry) return Task.CompletedTask;
        _loadingPage = true;
        return RunAsync(async token =>
        {
            await ((IMailMoveCache)cache!).ReconcileMoveAsync(retry.Source, retry.Destination, token).ConfigureAwait(false);
            return true;
        }, _ =>
        {
            _moveCacheRetry = null;
            Messages = Messages.Where(message => message.Key != retry.Source).ToArray();
            if (SelectedMessage?.Key == retry.Source) { SelectedMessage = null; Body = null; FlagChangeNotice = null; MessageProblem = null; }
            bool affectedFolder = SelectedMailboxId == retry.Source.MailboxId || SelectedMailboxId == retry.Destination.MailboxId;
            if (affectedFolder) { _older = null; _hasOlderCached = false; }
            _cachedMailboxIds = _cachedMailboxIds.Append(retry.Destination.MailboxId).Distinct(StringComparer.Ordinal).ToArray();
            UpdateFolders(); MoveNotice = Status = "Local move update saved. No server move was sent. Receive mail to refresh folder membership.";
            if (affectedFolder && IsShowingCachedMail && _account is { } account) CacheLoadTask = LoadCachedAsync(account, older: false);
        }, "Updating local mail after the confirmed move…", InboxProblemScope.List);
    }

    private bool _changingFlags;
    public bool SupportsReadStateChanges => receiver is IMailReadStateWriter;
    public bool IsChangingMessageFlags => IsBusy && _changingFlags;
    public bool SupportsStarStateChanges => receiver is IMailStarStateWriter;
    public bool CanChangeStarState => !NeedsCacheRepair && SupportsStarStateChanges && CanReceive && SelectedMessage is not null && PendingPermanentDelete is null;
    public bool CanChangeReadState => !NeedsCacheRepair && SupportsReadStateChanges && CanReceive && SelectedMessage is not null && PendingPermanentDelete is null;
    public string? FlagChangeNotice { get; private set; }
    private const string UncertainFlagState = "Message flag change canceled or interrupted. The server may have applied it. Receive mail to check; no change is queued for retry.";

    public Task SetSelectedReadStateAsync(bool isRead) => SetSelectedFlagAsync(isRead, starred: false);
    public Task SetSelectedStarStateAsync(bool isStarred) => SetSelectedFlagAsync(isStarred, starred: true);

    private Task SetSelectedFlagAsync(bool isSet, bool starred)
    {
        if (starred ? !CanChangeStarState : !CanChangeReadState) return Task.CompletedTask;
        string stateName = starred ? "starred state" : "read state";
        var account = _account!; var key = SelectedMessage!.Key;
        _loadingPage = true;
        FlagChangeNotice = $"Changing {stateName} on the server…";
        return RunAsync(async token =>
        {
            MailFlagChangeResult result;
            if (starred) result = await ((IMailStarStateWriter)receiver).SetStarStateAsync(account, key, isSet, token).ConfigureAwait(false);
            else
            {
                var read = await ((IMailReadStateWriter)receiver).SetReadStateAsync(account, key, isSet, token).ConfigureAwait(false);
                result = new(read.Key, read.IsRead, read.Status, read.Explanation);
            }
            if (result.Key != key || result.IsSet != isSet || !Enum.IsDefined(result.Status))
                throw new InvalidOperationException("The writer returned a different flag operation.");
            bool cacheSaved = cache is null;
            if (result.Status == MailFlagChangeStatus.Confirmed && (starred ? cache is IMailStarStateCache : cache is IMailReadStateCache))
            {
                try
                {
                    if (starred) await ((IMailStarStateCache)cache!).UpdateStarStateAsync(key, isSet, token).ConfigureAwait(false);
                    else await ((IMailReadStateCache)cache!).UpdateReadStateAsync(key, isSet, token).ConfigureAwait(false);
                    cacheSaved = true;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { cacheSaved = false; }
            }
            return (Result: result, CacheSaved: cacheSaved);
        }, completed =>
        {
            var result = completed.Result;
            if (result.Status == MailFlagChangeStatus.Confirmed)
            {
                Messages = Messages.Select(message => message.Key == key ? (starred ? message with { IsStarred = isSet } : message with { IsRead = isSet }) : message).ToArray();
                if (SelectedMessage?.Key == key) SelectedMessage = starred ? SelectedMessage with { IsStarred = isSet } : SelectedMessage with { IsRead = isSet };
            }
            FlagChangeNotice = result.Explanation + (!completed.CacheSaved && result.Status == MailFlagChangeStatus.Confirmed
                ? $" The server change is confirmed, but its cached {stateName} could not be saved. Receive mail before relying on offline flags." : "");
            Status = FlagChangeNotice;
        }, $"Changing {stateName}…", InboxProblemScope.Message, changingFlags: true);
    }

    public Task RefreshFoldersAsync()
    {
        if (!CanRefreshFolders) return Task.CompletedTask;
        var account = _account!;
        _lastFailureWasDiscovery = true; _lastFailureWasDownload = false; _lastPageWasOlder = false;
        return RunAsync(token => ((IMailFolderReceiver)receiver).GetFoldersAsync(account, token), folders =>
        {
            _serverFolders = folders; UpdateFolders();
            Status = $"{folders.Count} server folders discovered. Select a folder, then Receive mail to check it.";
        }, "Discovering server folders…", InboxProblemScope.List);
    }

    public bool SelectFolder(string mailboxId)
    {
        if (!CanSwitchFolder || !Folders.Any(folder => folder.MailboxId == mailboxId && folder.IsSelectable)) return false;
        if (mailboxId == SelectedMailboxId) return true;
        StopPending(); SelectedMailboxId = mailboxId;
        ResetMessages(); UpdateFolders();
        Status = $"Selected folder: {mailboxId}. Receive mail to check the server.";
        Notify();
        CacheLoadTask = cache is not null ? LoadCachedAsync(_account!, older: false) : Task.CompletedTask;
        return true;
    }

    private MailHeaderSearchQuery? _searchQuery;
    public bool SupportsLocalSearch => cache is IMailLocalSearch;
    public bool SearchAllSavedFolders { get; private set; }
    public bool IsAccountSearchActive => IsSearchActive && _searchQuery!.MailboxId is null;
    public void SetSearchAllSavedFolders(bool value) { if (_disposed) return; SearchAllSavedFolders = value; Notify(); }
    public bool IsSearchActive => _searchQuery is not null;
    public string SearchText { get; private set; } = "";
    public string SearchSenderText { get; private set; } = "";
    public void SetSearchSenderText(string text) { ArgumentNullException.ThrowIfNull(text); if (_disposed) return; SearchSenderText = text; Notify(); }
    public MailHeaderSearchField SearchField { get; private set; }
    public void SetSearchField(MailHeaderSearchField field)
    {
        if (!Enum.IsDefined(field)) throw new ArgumentOutOfRangeException(nameof(field));
        if (_disposed) return;
        SearchField = field; Notify();
    }
    public bool? SearchReadState { get; private set; }
    public bool? SearchStarState { get; private set; }
    public bool? SearchBodyCached { get; private set; }
    public bool SearchUnreadOnly => SearchReadState == false;
    public bool SearchStarredOnly => SearchStarState == true;
    public void SetSearchFilters(bool? isRead, bool? isStarred, bool? isCached)
    {
        if (_disposed) return;
        SearchReadState = isRead; SearchStarState = isStarred; SearchBodyCached = isCached; Notify();
    }
    public void SetSearchFlags(bool? isRead, bool? isStarred)
    {
        if (_disposed) return;
        SearchReadState = isRead; SearchStarState = isStarred; Notify();
    }
    public void SetSearchBodyAvailability(bool? isCached)
    {
        if (_disposed) return;
        SearchBodyCached = isCached; Notify();
    }
    public void SetSearchText(string text)
    {
        if (_disposed) return;
        SearchText = text; Notify();
    }
    public bool SearchUsesCustomDates { get; private set; }
    public string SearchCustomDateRange { get; private set; } = "";
    public string? SearchDateError => SearchUsesCustomDates && !TryCustomDates(out _, out _) ?
        "Enter UTC dates as YYYY-MM-DD..YYYY-MM-DD (start inclusive, end exclusive). Either end may be blank; at least one date is required, and the end must be later than the start." : null;
    public void SetSearchCustomDateRange(string value)
    {
        if (_disposed) return;
        SearchCustomDateRange = value; SearchUsesCustomDates = true; SearchReceivedWithinDays = null; Notify();
    }
    private bool TryCustomDates(out DateTimeOffset? start, out DateTimeOffset? end)
    {
        start = end = null;
        if (SearchCustomDateRange.Length > 24 || SearchCustomDateRange.Any(char.IsControl)) return false;
        var parts = SearchCustomDateRange.Split("..", StringSplitOptions.None);
        if (parts.Length != 2) return false;
        if (!TryDate(parts[0].Trim(), out start) || !TryDate(parts[1].Trim(), out end)) return false;
        return (start is not null || end is not null) && (start is null || end is null || start < end);
    }
    private static bool TryDate(string text, out DateTimeOffset? date)
    {
        date = null; if (text.Length == 0) return true;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return false;
        date = new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); return true;
    }
    public int? SearchReceivedWithinDays { get; private set; }
    public void SetSearchReceivedWithinDays(int? days)
    {
        if (days is not (null or 7 or 30 or 365)) throw new ArgumentOutOfRangeException(nameof(days));
        if (_disposed) return;
        SearchUsesCustomDates = false; SearchReceivedWithinDays = days; Notify();
    }
    private readonly List<(MailHeaderSearchQuery Query, int Page)> _searchBack = [];
    public bool CanLoadPreviousSearchPage => !_disposed && !_storageMaintenance && !IsBusy && IsSearchActive && _searchBack.Count > 0;
    public Task LoadPreviousSearchPageAsync()
    {
        if (!CanLoadPreviousSearchPage) return Task.CompletedTask;
        var previous = _searchBack[^1]; _searchBack.RemoveAt(_searchBack.Count - 1);
        return LoadSearchPageAsync(previous.Query, previous.Page);
    }
    public int SearchPageNumber { get; private set; }
    public bool CanLoadNextSearchPage => !_disposed && !_storageMaintenance && !IsBusy && IsSearchActive &&
        SearchHasMore && Messages.Count > 0;
    public Task LoadNextSearchPageAsync()
    {
        if (!CanLoadNextSearchPage) return Task.CompletedTask;
        var query = _searchQuery! with { BeforeMessage = Messages[^1].Key }; int page = SearchPageNumber + 1;
        if (_searchBack.Count == 128) _searchBack.RemoveAt(0);
        _searchBack.Add((_searchQuery!, SearchPageNumber));
        return LoadSearchPageAsync(query, page);
    }
    public bool SearchHasMore { get; private set; }
    public bool CanSearch => SupportsLocalSearch && !_disposed && !_storageMaintenance && !IsBusy && _account is not null &&
        SearchDateError is null && SearchText.Length <= 256 && !SearchText.Any(char.IsControl) && SearchSenderText.Length <= 256 && !SearchSenderText.Any(char.IsControl) &&
        (!string.IsNullOrWhiteSpace(SearchText) || !string.IsNullOrWhiteSpace(SearchSenderText) || SearchReadState is not null || SearchStarState is not null || SearchBodyCached is not null || SearchReceivedWithinDays is not null || SearchUsesCustomDates);
    public string? SearchNotice => !IsSearchActive ? null :
        $"Saved-header search for \"{_searchQuery!.Text}\" in {(_searchQuery.MailboxId ?? "all saved folders for " + (_account?.EmailAddress ?? _searchQuery.AccountId.ToString()))}: {SearchFieldLabel(_searchQuery.Field)}. " +
        (_searchQuery.Field is MailHeaderSearchField.IndexedRetainedBody or MailHeaderSearchField.IndexedHeadersOrRetainedBody
            ? "Body coverage is complete retained decoded reader text (up to 1,048,576 characters), including HTML text alternatives. Older preview-only, incomplete, over-limit and uncached bodies are excluded; attachments and raw HTML are excluded. A hit beyond the preview may not appear in the reader. Storage shows coverage counts. "
            : _searchQuery.Field is MailHeaderSearchField.BodyPreview or MailHeaderSearchField.HeadersOrBodyPreview or MailHeaderSearchField.IndexedBodyPreview or MailHeaderSearchField.IndexedHeadersOrBodyPreview
            ? "Body coverage is downloaded plain-text previews only (up to 32,000 characters, including any HTML text fallback). HTML source, attachments, uncached bodies and text beyond the preview are excluded. "
            : "Bodies, attachments and server-only mail are excluded. ") +
        (string.IsNullOrWhiteSpace(_searchQuery.Text) ? "No keyword; matching the submitted filters. " : "") +
        (_searchQuery.SenderContains is {} sender ? $"Saved sender header contains \"{sender}\" (case-insensitive; not verified identity). " : "") +
        (_searchQuery.IsRead is { } read ? (read ? "Read only. " : "Unread only. ") : "") +
        (_searchQuery.IsStarred is { } starred ? (starred ? "Starred only. " : "Unstarred only. ") : "") +
        (_searchQuery.IsBodyCached is { } cached ? (cached ? "Downloaded bodies only. " : "Bodies not downloaded. ") : "") +
        (_searchQuery.ReceivedFrom is not null || _searchQuery.ReceivedBefore is not null
            ? $"Received from {(_searchQuery.ReceivedFrom is { } from ? from.ToString("u", CultureInfo.InvariantCulture) : "any earlier date")} (inclusive) to {(_searchQuery.ReceivedBefore is { } end ? end.ToString("u", CultureInfo.InvariantCulture) : "any later date")} (exclusive); unknown dates excluded. "
            : "All received dates, including unknown dates. ") +
        $"Page {SearchPageNumber}: {Messages.Count} saved headers shown. " +
        (SearchHasMore ? "More matches are available with Next saved results. " : "") +
        "Find starts a new search from the first page. Saved mail can change between pages. " +
        "Results reflect the saved copy. Clear search to receive, download or change server mail.";

    private static string SearchFieldLabel(MailHeaderSearchField field) => field is MailHeaderSearchField.IndexedRetainedBody or MailHeaderSearchField.IndexedHeadersOrRetainedBody
        ? (field == MailHeaderSearchField.IndexedRetainedBody ? "complete retained bodies only" : "sender, subject or complete retained bodies") + " (indexed word phrase)"
        : (int)field >= 5
        ? SearchFieldLabel((MailHeaderSearchField)((int)field % 5)) + " (indexed word phrase; rebuild index in Settings → Storage)"
        : field switch
    {
        MailHeaderSearchField.Sender => "sender only", MailHeaderSearchField.Subject => "subject only",
        MailHeaderSearchField.BodyPreview => "saved body previews only",
        MailHeaderSearchField.HeadersOrBodyPreview => "sender, subject or saved body previews", _ => "sender and subject only",
    };

    public void SetSearch(string text, bool unreadOnly = false, bool starredOnly = false)
    {
        if (_disposed) return;
        SearchText = text; SearchReadState = unreadOnly ? false : null; SearchStarState = starredOnly ? true : null; Notify();
    }

    public Task SearchSavedHeadersAsync()
    {
        if (!CanSearch) return Task.CompletedTask;
        var now = DateTimeOffset.UtcNow;
        TryCustomDates(out var customStart, out var customEnd);
        var query = new MailHeaderSearchQuery(_account!.Id, SearchText)
        {
            Field = SearchField, MailboxId = SearchAllSavedFolders ? null : SelectedMailboxId, IsRead = SearchReadState,
            SenderContains = string.IsNullOrWhiteSpace(SearchSenderText) ? null : SearchSenderText.Trim(),
            IsStarred = SearchStarState, IsBodyCached = SearchBodyCached, MaximumCount = MaximumLoadedMessages,
            ReceivedFrom = SearchUsesCustomDates ? customStart : SearchReceivedWithinDays is { } days ? now.AddDays(-days) : null,
            ReceivedBefore = SearchUsesCustomDates ? customEnd : SearchReceivedWithinDays is not null ? now : null,
        };
        _searchBack.Clear(); return LoadSearchPageAsync(query, 1);
    }

    private Task LoadSearchPageAsync(MailHeaderSearchQuery query, int pageNumber)
    {
        StopPending(); ResetMessages(clearSearchHistory: false); _searchQuery = query; SearchPageNumber = pageNumber; _loadingPage = true;
        return RunAsync(async token =>
        {
            var result = await ((IMailLocalSearch)cache!).SearchHeadersAsync(query, token).ConfigureAwait(false);
            if (result.Messages.Count > query.MaximumCount || result.Messages.Any(message =>
                message.Key.AccountId != query.AccountId || query.MailboxId is {} folder && message.Key.MailboxId != folder || message.Key.Uid == 0 || message.Key.UidValidity == 0 ||
                query.BeforeMessage is {} before && (MailHeaderSearchOrder.Compare(message.Key,before) >= 0 || message.Key.MailboxId == before.MailboxId && message.Key.UidValidity != before.UidValidity)) ||
                result.Messages.Select(message=>message.Key).Distinct().Count()!=result.Messages.Count ||
                result.Messages.GroupBy(message=>message.Key.MailboxId).Any(group=>group.Select(message=>message.Key.UidValidity).Distinct().Count()!=1) ||
                result.Messages.Zip(result.Messages.Skip(1)).Any(pair => MailHeaderSearchOrder.Compare(pair.First.Key,pair.Second.Key) <= 0) ||
                query.MailboxId is null && (result.AccountMailboxSnapshot is not {} snapshot || snapshot.Length!=64 || !snapshot.All(Uri.IsHexDigit) ||
                    query.AccountMailboxSnapshot is {} expected && !string.Equals(expected,snapshot,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Search returned messages outside its scope or snapshot.");
            return result;
        }, result =>
        {
            _searchQuery = query.MailboxId is null ? query with { AccountMailboxSnapshot = result.AccountMailboxSnapshot } : query;
            Messages = result.Messages; SearchHasMore = result.HasMore;
            IsShowingCachedMail = true;
            Status = $"{Messages.Count} saved headers matched. This is not a complete server search.";
        }, "Searching saved headers…", InboxProblemScope.List);
    }

    public Task ClearSearchAsync()
    {
        if (_disposed || _storageMaintenance || !IsSearchActive) return Task.CompletedTask;
        StopPending(); ResetMessages(); Notify();
        return CacheLoadTask = _account is { } account ? LoadCachedAsync(account, older: false) : Task.CompletedTask;
    }

    private void ResetMessages(bool clearSearchHistory = true)
    {
        MailboxSnapshotNotice = "Inspect the saved checkpoint for this account/folder, or explicitly capture a complete header snapshot. Ordinary pages and offline batch cursors do not establish complete membership.";
        if (clearSearchHistory) _searchBack.Clear();
        _searchQuery = null; SearchHasMore = false; SearchPageNumber = 0;
        PendingPermanentDelete = null; _deleteProfile = null;
        FlagChangeNotice = null;
        Messages = []; SelectedMessage = null; Body = null; _older = null; HasLoaded = false;
        DownloadCompleted = DownloadTotal = 0;
        _lastFailureWasDownload = _lastFailureWasDiscovery = _lastPageWasOlder = false;
        IsShowingCachedMail = IsBodyFromCache = _hasOlderCached = false;
        LastReceivedAt = null; ClearProblem(); _statusScope = InboxProblemScope.None;
    }

    private AccountProfile? _account;
    private MailInboxCursor? _older;
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _disposed;
    private bool _storageMaintenance;
    private bool _loadingPage;
    private InboxProblemScope _running;
    private InboxProblemScope _statusScope;
    private bool _lastPageWasOlder;
    private bool _lastFailureWasDownload;
    private bool _downloadingBodies;
    public OfflineDownloadPolicy DownloadPolicy { get; private set; } = Enum.IsDefined(downloadPolicy)
        ? downloadPolicy : throw new ArgumentOutOfRangeException(nameof(downloadPolicy));
    public bool IsDownloadingBodies => IsBusy && _downloadingBodies;
    public int DownloadCompleted { get; private set; }
    public int DownloadTotal { get; private set; }
    public bool CanDownloadLoaded => cache is not null && CanReceive && Messages.Count > 0;
    private bool NeedsCacheRepair => cache is IMailCacheRecovery { PendingRecoveryCount: > 0 };
    public string? RecoveryNotice => cache is IMailCacheRecovery { PendingRecoveryCount: > 0 } recovery
        ? $"{recovery.PendingRecoveryCount} saved changes need attention. Use Settings → Storage to repair confirmed local changes or review uncertain server actions. Server actions are never automatically repeated." : null;
    public bool HasPersistentCache => cache is not null;
    public bool ProblemIsBodyDownload => ListProblem is not null && _lastFailureWasDownload;
    public string DownloadPolicyNotice => DownloadPolicy == OfflineDownloadPolicy.HeadersOnly
        ? "Headers-only policy: newly opened bodies are not saved unless explicitly downloaded. Existing saved bodies are kept."
        : "Bodies are saved when opened. Explicit downloads also keep loaded messages offline.";
    private bool _hasOlderCached;
    public Task CacheLoadTask { get; private set; } = Task.CompletedTask;
    public bool IsShowingCachedMail { get; private set; }
    public bool IsBodyFromCache { get; private set; }
    public DateTimeOffset? LastReceivedAt { get; private set; }
    private string MailboxNoun => SelectedMailboxId == "INBOX" ? "inbox" : "folder";
    public string? CacheNotice => IsSearchActive ? null : cache is null ? null : IsShowingCachedMail
        ? $"Saved {MailboxNoun} — last received {LastReceivedAt?.ToLocalTime():g}. Server changes may not be reflected. {DownloadPolicyNotice}{BatchNotice}"
        : HasLoaded ? $"Received headers are saved locally. {DownloadPolicyNotice}{BatchNotice}" : null;
    private string BatchNotice => DownloadTotal > 0 ? $" Last offline batch: {DownloadCompleted}/{DownloadTotal} bodies available." : "";

    public void SetDownloadPolicy(OfflineDownloadPolicy policy)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        if (_disposed || DownloadPolicy == policy) return;
        StopPending(); DownloadPolicy = policy; Notify();
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=8C487F
    // Broiler-Human:        PENDING
    public event EventHandler? Changed;
    public IReadOnlyList<MailMessageSummary> Messages { get; private set; } = [];
    public MailMessageSummary? SelectedMessage { get; private set; }
    public MailMessageBody? Body { get; private set; }
    public string Status { get; private set; } = "Save an account in the Account tab to receive mail.";
    /// <summary>
    /// <see cref="Status"/> describes the selected message, which is loading or loaded, rather than the
    /// list or the account, so it fits only while the message is shown.
    /// </summary>
    public bool StatusIsAboutMessage => _statusScope == InboxProblemScope.Message;
    public bool IsBusy { get; private set; }
    /// <summary>A page of summaries is loading; the list stays visible meanwhile.</summary>
    public bool IsLoadingList => IsBusy && _running == InboxProblemScope.List;
    /// <summary>The selected message's body is loading.</summary>
    public bool IsLoadingMessage => IsBusy && _running == InboxProblemScope.Message;
    /// <summary>At least one page loaded successfully for the current account, so an empty list means an empty inbox.</summary>
    public bool HasLoaded { get; private set; }
    /// <summary>
    /// Why the last page failed or was canceled, or null. It stays until the next page operation, so
    /// reading a message meanwhile does not hide that the rows are from an earlier receive.
    /// </summary>
    public InboxProblem? ListProblem { get; private set; }
    /// <summary>Why the selected message failed to load or its loading was canceled, or null. It stays until the next operation.</summary>
    public InboxProblem? MessageProblem { get; private set; }
    /// <summary>
    /// The explanation of the last failed or canceled operation, or null: the message's when both panes
    /// have one, since a page operation replaces both.
    /// </summary>
    public string? Problem => (MessageProblem ?? ListProblem)?.Text;
    public InboxProblemScope ProblemScope => MessageProblem is not null ? InboxProblemScope.Message
        : ListProblem is not null ? InboxProblemScope.List : InboxProblemScope.None;
    /// <summary>The problem is a cancellation the user asked for, not a failure.</summary>
    public bool ProblemIsCancellation => (MessageProblem ?? ListProblem)?.IsCancellation == true;
    /// <summary>The list problem came from Load older, not from receiving the newest messages.</summary>
    public bool ProblemIsOlderPage => ListProblem is not null && _lastPageWasOlder;
    /// <summary>
    /// Retrying the list problem loads the older page again, rather than receiving the newest messages,
    /// as <see cref="RetryAsync(InboxProblemScope)"/> does. Unlike <see cref="CanLoadOlder"/>, it does not
    /// change while a message loads, so the Retry beside the list keeps its name meanwhile.
    /// </summary>
    public bool ListRetryLoadsOlder => ProblemIsOlderPage && (IsShowingCachedMail ? _hasOlderCached : _older is not null) && Messages.Count < MaximumLoadedMessages;
    /// <summary>Whether <see cref="RetryAsync()"/> can repeat the operation of the last problem.</summary>
    public bool CanRetry => CanRetryIn(ProblemScope);

    /// <summary>Whether <see cref="RetryAsync(InboxProblemScope)"/> can repeat the operation of the problem of <paramref name="scope"/>.</summary>
    public bool CanRetryIn(InboxProblemScope scope) => !_disposed && !_storageMaintenance && !IsBusy && scope switch
    {
        InboxProblemScope.List => ListProblem is not null && (IsSearchActive ? SupportsLocalSearch : _lastSnapshotAction == MailboxSnapshotAction.Inspect ? CanInspectMailboxSnapshot : _lastSnapshotAction == MailboxSnapshotAction.Capture ? CanCaptureMailboxSnapshot : _lastFailureWasOfflineHeaders ? CanReceiveOfflineHeaders : _lastFailureWasSelectedFolders ? CanDownloadOfflineFolders : _lastFailureWasDiscovery ? CanRefreshFolders : CanReceive),
        InboxProblemScope.Message => MessageProblem is not null && SelectedMessage is not null && _account is not null,
        _ => false,
    };
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=A12292
    // Broiler-Falsified-If: CanReceive is true for a saved account whose IsEnabled is false
    // Broiler-Human:        PENDING
    public bool CanReceive => !IsSearchActive && !_disposed && !_storageMaintenance && !IsBusy && _account is { IsEnabled: true } &&
        (SelectedMailboxId == "INBOX" || receiver is IMailFolderReceiver) &&
        Folders.Any(folder => folder.MailboxId == SelectedMailboxId && folder.IsSelectable);
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=C155F8
    // Broiler-Falsified-If: CanLoadOlder stays true after Messages.Count has reached MaximumLoadedMessages
    // Broiler-Human:        PENDING
    public bool CanLoadOlder => !IsSearchActive && !_disposed && !_storageMaintenance && !IsBusy && Messages.Count < MaximumLoadedMessages &&
        (IsShowingCachedMail ? _hasOlderCached : CanReceive && _older is not null);
    /// <summary>
    /// Why Load older is unavailable although the server has older messages, or null: the session holds
    /// <see cref="MaximumLoadedMessages"/> summaries until Receive mail returns to the newest page. It
    /// stays while messages are read; while a page loads, that page's progress replaces it.
    /// </summary>
    public string? SessionLimitNotice => !IsLoadingList && (IsShowingCachedMail ? _hasOlderCached : _older is not null) && Messages.Count >= MaximumLoadedMessages
        ? "Session limit reached (500 messages). Older ones cannot be loaded in this session. Receive mail to start again." : null;
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=63EC38
    // Broiler-Falsified-If: CanSelect is true while a page load is still running
    // Broiler-Human:        PENDING
    public bool CanSelect => !_disposed && !_storageMaintenance && !_loadingPage;

    public void BeginStorageMaintenance()
    {
        if (_disposed) return;
        _storageMaintenance = true;
        if (OfflineFoldersReady) OfflineFolderNotice = SelectionNotice();
        StopPending();
        Status = "Downloaded mail maintenance is in progress."; Notify();
    }

    public void EndStorageMaintenance(bool cacheCleared)
    {
        if (_disposed) return;
        _storageMaintenance = false;
        if (cacheCleared && _account is { } account)
        { _account = null; SetAccount(account); }
        else { Status = "Local mail is unchanged. Receive mail or select a message to continue."; Notify(); }
    }

    private double _splitterFraction = 0.35;
    public double SplitterFraction
    {
        get => _splitterFraction;
        set
        {
            if (!double.IsFinite(value)) return;
            double clamped = Math.Clamp(value, 0.05, 0.95);
            if (_splitterFraction.Equals(clamped)) return;
            _splitterFraction = clamped;
            Notify();
        }
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=3; Fingerprint=BF24EC
    // Broiler-Falsified-If: a receive started before SetAccount switches to another profile still commits its messages afterwards
    // Broiler-Human:        PENDING
    public void SetAccount(AccountProfile? account)
    {
        if (_disposed || _account == account) return;
        StopPending();
        _account = account;
        SearchAllSavedFolders = false;
        SearchText = ""; SearchSenderText = ""; SearchReadState = SearchStarState = SearchBodyCached = null; SearchReceivedWithinDays = null; SearchUsesCustomDates = false; SearchCustomDateRange = ""; SearchField = MailHeaderSearchField.SenderOrSubject;
        SelectedMailboxId = "INBOX"; _serverFolders = null; _cachedMailboxIds = [];
        MoveNotice = null; _moveCacheRetry = null; _undoMove = null; MoveDestinationMailboxId = null;
        DeleteNotice = null; _deleteCacheRetry = null;
        ResetNavigation(account);
        ResetMessages(); UpdateFolders();
        LoadOfflineFolderSelection(account);
        Status = account is { IsEnabled: true }
            ? $"Ready to receive mail for {account.EmailAddress} using the saved profile."
            : "Save and enable an account in the Account tab to receive mail.";
        Notify();
        CacheLoadTask = cache is not null && account is not null ? LoadCachedAsync(account, older: false, restoreNavigation: SupportsAccountNavigation) : Task.CompletedTask;
    }

    private Task LoadCachedAsync(AccountProfile account, bool older, bool restoreNavigation = false)
    {
        _lastFailureWasDownload = false; DownloadCompleted = DownloadTotal = 0;
        string mailboxId = SelectedMailboxId;
        _lastFailureWasDiscovery = false;
        uint? before = older && Messages.Count > 0 ? Messages[^1].Key.Uid : null;
        _loadingPage = true;
        _lastPageWasOlder = older;
        return RunAsync(async token =>
        {
            AccountNavigation? preferences = null; string? navigationError = null;
            if (restoreNavigation)
            {
                try
                {
                    preferences = await navigation!.LoadAsync(account.Id, token).ConfigureAwait(false);
                    if (preferences.AccountId != account.Id) throw new InvalidDataException("Navigation returned another account identity.");
                    mailboxId = preferences.StartMailboxId;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                { preferences = null; navigationError = "Saved navigation could not be loaded. Opening INBOX; preferences were kept. Repair the file, then Reload navigation."; }
            }
            var mailbox = await cache!.GetLatestMailboxAsync(account.Id, mailboxId, token).ConfigureAwait(false);
            IReadOnlyList<MailMessageSummary> messages = mailbox is null ? [] :
                await cache.ReadSummariesAsync(mailbox.Key, PageSize + 1, before, token).ConfigureAwait(false);
            var names = cache is IMailFolderCache folderCache
                ? await folderCache.GetCachedMailboxIdsAsync(account.Id, token).ConfigureAwait(false) : _cachedMailboxIds;
            return (Mailbox: mailbox, Messages: messages, Names: names, Navigation: preferences, NavigationError: navigationError);
        }, saved =>
        {
            if (restoreNavigation)
            {
                _loadingNavigation = false; NavigationReady = saved.Navigation is not null;
                if (saved.Navigation is not null) _navigation = saved.Navigation;
                SelectedMailboxId = mailboxId;
                NavigationNotice = saved.NavigationError ?? NavigationSummary();
            }
            _cachedMailboxIds = saved.Names; UpdateFolders();
            LastReceivedAt = saved.Mailbox?.ReceivedAt;
            HasLoaded = saved.Mailbox is not null;
            IsShowingCachedMail = HasLoaded;
            _hasOlderCached = saved.Messages.Count > PageSize;
            Messages = (older ? Messages.Concat(saved.Messages.Take(PageSize)) : saved.Messages.Take(PageSize))
                .DistinctBy(message => message.Key).ToArray();
            Status = saved.Mailbox is null ? $"No saved {MailboxNoun} yet. Receive mail to download headers."
                : $"{Messages.Count} saved headers loaded. Downloaded messages can be read offline. Receive mail to check the server.";
        }, older ? "Opening older saved headers…" : $"Opening saved {MailboxNoun}…", InboxProblemScope.List);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=6EFC99
    // Broiler-Falsified-If: ReceiveAsync starts a fetch while CanReceive is false, for example for a disabled account
    // Broiler-Human:        PENDING
    public Task ReceiveAsync() => CanReceive ? LoadPageAsync(null) : Task.CompletedTask;
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=68C2B0
    // Broiler-Falsified-If: LoadOlderAsync starts a fetch when no older cursor exists or after MaximumLoadedMessages summaries are loaded
    // Broiler-Human:        PENDING
    public Task LoadOlderAsync() => !CanLoadOlder ? Task.CompletedTask
        : IsShowingCachedMail ? LoadCachedAsync(_account!, older: true) : LoadPageAsync(_older);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=D87A42
    // Broiler-Falsified-If: a page that repeats a message key already loaded leaves two entries with that key in Messages
    // Broiler-Human:        PENDING
    private Task LoadPageAsync(MailInboxCursor? cursor)
    {
        _lastFailureWasDownload = false; DownloadCompleted = DownloadTotal = 0;
        var account = _account!;
        string mailboxId = SelectedMailboxId; _lastFailureWasDiscovery = false;
        _loadingPage = true;
        _lastPageWasOlder = cursor is not null;
        return RunAsync(async token =>
        {
            var page = receiver is IMailFolderReceiver folderReceiver
                ? await folderReceiver.GetMailboxAsync(account, mailboxId, PageSize, cursor, token).ConfigureAwait(false)
                : await receiver.GetInboxAsync(account, PageSize, cursor, token).ConfigureAwait(false);
            if (page.Messages.Any(message => message.Key.AccountId != account.Id || message.Key.MailboxId != mailboxId) ||
                page.Older is { } continuation && (continuation.AccountId != account.Id || continuation.MailboxId != mailboxId))
                throw new InvalidDataException("The receiver returned mail from another mailbox.");
            if (cache is not null)
            {
                var key = page.Mailbox ?? (page.Messages.FirstOrDefault()?.Key is { } first
                    ? new MailboxKey(first.AccountId, first.MailboxId, first.UidValidity) : null);
                if (key is null || key.AccountId != account.Id || key.MailboxId != mailboxId)
                    throw new InvalidDataException("The receiver did not identify the requested mailbox epoch.");
                await cache.SaveSummariesAsync(key, page.Messages, DateTimeOffset.UtcNow, token).ConfigureAwait(false);
            }
            return page;
        }, page =>
        {
            if (cache is not null && !_cachedMailboxIds.Contains(mailboxId, StringComparer.Ordinal))
                _cachedMailboxIds = _cachedMailboxIds.Append(mailboxId).ToArray();
            UpdateFolders();
            IsShowingCachedMail = _hasOlderCached = false;
            LastReceivedAt = DateTimeOffset.UtcNow;
            HasLoaded = true;
            if (SelectedMessage is { } selected && page.Messages.Any(message => message.Key == selected.Key)) FlagChangeNotice = null;
            Messages = (cursor is null ? page.Messages : Messages.Concat(page.Messages))
                .DistinctBy(message => message.Key).ToArray();
            _older = page.Older;
            string? reading = cursor is null ? KeepReading(page) : null;
            Status = Messages.Count == 0 ? $"The {MailboxNoun} is empty." : SelectedMessage is null
                ? $"{Messages.Count} messages loaded. Select one to read. Reading does not mark messages as read on the server."
                : $"{Messages.Count} messages loaded. Reading does not mark messages as read on the server.";
            if (reading is not null) Status += " " + reading;
        }, cursor is null ? "Receiving newest messages…" : "Loading older messages…", InboxProblemScope.List);
    }

    /// <summary>
    /// Reconciles the open message with a refreshed newest page, by full key. A retained body keeps
    /// its identity, so the reader keeps its scroll position and preview. Absence from the page only
    /// proves removal when the page covers the message's UID range; otherwise it may simply be older.
    /// </summary>
    /// <returns>An explanation when the open message changed state, otherwise null.</returns>
    private string? KeepReading(MailInboxPage page)
    {
        if (SelectedMessage is not { } open) return null;
        if (Messages.FirstOrDefault(message => message.Key == open.Key) is { } refreshed)
        {
            SelectedMessage = refreshed;
            return null;
        }
        var fetched = page.Messages.Where(message => message.Key.AccountId == open.Key.AccountId && message.Key.MailboxId == open.Key.MailboxId).ToArray();
        if (fetched.Length > 0 && fetched.Any(message => message.Key.UidValidity != open.Key.UidValidity))
        {
            SelectedMessage = null;
            Body = null;
            return $"The server renumbered the {MailboxNoun}, so the open message was closed. Select it again to read it.";
        }
        // Newest pages are contiguous: every message from the oldest fetched UID upward was returned.
        if (fetched.Length == 0 || page.Older is null || open.Key.Uid >= fetched.Min(message => message.Key.Uid))
        {
            SelectedMessage = null;
            Body = null;
            return $"The open message is no longer in the {MailboxNoun}. It may have been moved or deleted on the server.";
        }
        return "The open message is older than the newest page and stays open. Use Load older to show it in the list.";
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=F7BF9B
    // Broiler-Falsified-If: a body whose Key differs from the requested key is committed to Body
    // Broiler-Human:        PENDING
    public Task SelectAsync(MailMessageKey key)
    {
        if (!CanSelect || _account is null) return Task.CompletedTask;
        // The open message may be outside the refreshed page; it can still be read again.
        var message = Messages.FirstOrDefault(item => item.Key == key) ?? (SelectedMessage?.Key == key ? SelectedMessage : null);
        if (message is null) return Task.CompletedTask;
        var account = _account;
        if (SelectedMessage?.Key != key) FlagChangeNotice = null;
        SelectedMessage = message;
        Body = null;
        IsBodyFromCache = false;
        bool fromCache = false;
        bool savedForOffline = false;
        var policy = DownloadPolicy;
        bool searchOnly = IsSearchActive;
        return RunAsync(async token =>
        {
            var mailbox = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
            if (cache is not null && await cache.ReadBodyAsync(mailbox, key.Uid, token).ConfigureAwait(false) is { } saved)
            { fromCache = savedForOffline = true; return saved; }
            if (searchOnly) throw new MailConnectionException("This body is not saved. Clear search and explicitly receive or download mail to read it.");
            MailMessageBody body;
            try { body = await receiver.GetBodyAsync(account, key, token).ConfigureAwait(false); }
            catch (MailConnectionException error) when (cache is not null)
            { throw new MailConnectionException("This message body has not been downloaded for offline reading. " + error.Message); }
            if (body.Key != key) throw new InvalidOperationException("The receiver returned a different message.");
            if (cache is not null && policy == OfflineDownloadPolicy.OnDemand)
            { await cache.SaveBodyAsync(mailbox, body, token).ConfigureAwait(false); savedForOffline = true; }
            return body;
        }, body =>
        {
            Body = body;
            IsBodyFromCache = fromCache;
            if (cache is not null && savedForOffline && SelectedMessage is { } selected)
            {
                SelectedMessage = selected with { IsBodyCached = true };
                Messages = Messages.Select(item => item.Key == selected.Key ? SelectedMessage : item).ToArray();
            }
            Status = body.IsHtmlFallback ? "Reading text extracted from HTML. External resources are not loaded." : "Reading plain text. This does not mark the message as read on the server.";
            if (body.IsTruncated) Status += " Preview limited to 32,000 characters.";
            if (cache is not null) Status += fromCache ? " Reading the saved offline copy." : savedForOffline
                ? " Downloaded body saved for offline reading." : " This body was not saved under the headers-only policy.";
        }, "Loading message…", InboxProblemScope.Message);
    }

    /// <summary>Deliberate, bounded offline download of only the current loaded inbox headers (up to 500).</summary>
    public Task DownloadLoadedBodiesAsync()
    {
        if (!CanDownloadLoaded) return Task.CompletedTask;
        var account = _account!; var targets = Messages.Take(MaximumLoadedMessages).ToArray(); var openBody = Body; int generation = 0;
        DownloadCompleted = 0; DownloadTotal = targets.Length; _loadingPage = true;
        _lastFailureWasDiscovery = false;
        _lastPageWasOlder = false; _lastFailureWasDownload = true;
        return RunAsync(async token =>
        {
            for (int index = 0; index < targets.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var key = targets[index].Key;
                var mailbox = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
                if (key.AccountId != account.Id) throw new InvalidDataException("Offline batch contains a different account.");
                if (await cache!.ReadBodyAsync(mailbox, key.Uid, token).ConfigureAwait(false) is null)
                {
                    var body = openBody?.Key == key ? openBody : await receiver.GetBodyAsync(account, key, token).ConfigureAwait(false);
                    if (body.Key != key) throw new InvalidDataException("The receiver returned a different message.");
                    await cache.SaveBodyAsync(mailbox, body, token).ConfigureAwait(false);
                }
                int completed = index + 1;
                dispatcher.Post(() =>
                {
                    if (_disposed || generation != _generation || _account?.Id != account.Id) return;
                    DownloadCompleted = completed;
                    Messages = Messages.Select(item => item.Key == key ? item with { IsBodyCached = true } : item).ToArray();
                    if (SelectedMessage?.Key == key) SelectedMessage = SelectedMessage with { IsBodyCached = true };
                    Status = $"Offline download: {completed}/{targets.Length} bodies available. Saved bodies are kept if canceled.";
                    Notify();
                });
            }
            return true;
        }, _ => Status = $"Offline download complete: {targets.Length}/{targets.Length} loaded bodies available. Load more headers explicitly to download older mail.",
            $"Downloading bodies for {targets.Length} loaded {MailboxNoun} headers…", InboxProblemScope.List,
            downloading: true, began: value => generation = value);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=3F339A
    // Broiler-Falsified-If: a result that completes after Cancel still replaces Messages or Body
    // Broiler-Human:        PENDING
    public void Cancel()
    {
        if (!IsBusy || _disposed) return;
        var scope = _running;
        bool downloading = IsDownloadingBodies;
        bool receivingOfflineHeaders = IsReceivingOfflineHeaders;
        bool changingFlags = IsChangingMessageFlags;
        bool moving = IsMoving;
        bool deleting = IsDeleting;
        StopPending();
        if (deleting) { DeleteNotice = Status = UncertainDelete; Notify(); return; }
        if (moving) { MoveNotice = Status = UncertainMove; Notify(); return; }
        if (changingFlags)
        {
            FlagChangeNotice = Status = UncertainFlagState; Notify(); return;
        }
        if (scope == InboxProblemScope.List && _lastSnapshotAction != MailboxSnapshotAction.None)
        {
            Status = "Header snapshot operation canceled. A checkpoint may already have committed; Inspect saved header snapshot before retrying. No server mail was changed.";
            SetProblem(scope, Status, canceled: true); Notify(); return;
        }
        Status = receivingOfflineHeaders ? $"Selected-folder header receive canceled: {OfflineHeaderPagesSaved} pages / {OfflineHeadersSaved} headers confirmed saved. Retry continues unsaved pages; Restart rechecks newest."
            : downloading ? $"Offline download canceled: {DownloadCompleted}/{DownloadTotal} bodies confirmed available. Saved bodies were kept. Retry skips them."
            : "Mail operation canceled. You can retry.";
        SetProblem(scope, downloading || receivingOfflineHeaders ? Status : scope == InboxProblemScope.Message ? "Loading the message was canceled."
            : IsSearchActive ? "Saved-header search was canceled. Retry searches saved mail only."
            : _lastPageWasOlder ? "Loading older messages was canceled." : "Receiving was canceled.", canceled: true);
        Notify();
    }

    /// <summary>Repeats the operation that failed or was canceled last: the same page, or the selected message.</summary>
    public Task RetryAsync() => RetryAsync(ProblemScope);

    /// <summary>Repeats the operation of the problem of <paramref name="scope"/>: the same page, or the selected message.</summary>
    public Task RetryAsync(InboxProblemScope scope)
    {
        if (!CanRetryIn(scope)) return Task.CompletedTask;
        if (scope == InboxProblemScope.List && IsSearchActive) return LoadSearchPageAsync(_searchQuery!, SearchPageNumber);
        if (scope == InboxProblemScope.List && _lastSnapshotAction != MailboxSnapshotAction.None) return _lastSnapshotAction == MailboxSnapshotAction.Capture ? CaptureMailboxSnapshotAsync() : InspectMailboxSnapshotAsync();
        if (scope == InboxProblemScope.List && _lastFailureWasOfflineHeaders) return ReceiveOfflineHeadersAsync();
        if (scope == InboxProblemScope.List && _lastFailureWasDiscovery) return RefreshFoldersAsync();
        if (scope == InboxProblemScope.List && _lastFailureWasSelectedFolders) return DownloadOfflineFoldersAsync();
        if (scope == InboxProblemScope.List && _lastFailureWasDownload) return DownloadLoadedBodiesAsync();
        return scope == InboxProblemScope.Message ? SelectAsync(SelectedMessage!.Key)
            : ListRetryLoadsOlder ? LoadOlderAsync() : ReceiveAsync();
    }

    private void SetProblem(InboxProblemScope scope, string text, bool canceled)
    {
        if (scope == InboxProblemScope.Message) MessageProblem = new(text, canceled);
        else ListProblem = new(text, canceled);
    }

    private void ClearProblem()
    {
        ListProblem = null;
        MessageProblem = null;
    }

    private void CancelPending()
    {
        var active = _operation;
        _operation = null;
        if (active is null) return;
        try { active.Cancel(); }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or AggregateException) { }
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=7; Fingerprint=05A835
    // Broiler-Falsified-If: a callback from an operation superseded by a later RunAsync or StopPending still commits its result on the dispatcher
    // Broiler-Human:        PENDING
    private async Task RunAsync<T>(Func<CancellationToken, Task<T>> operation, Action<T> commit, string busy, InboxProblemScope scope,
        bool downloading = false, Action<int>? began = null, bool changingFlags = false, bool moving = false, bool deleting = false, bool selectedFoldersDownload = false, bool offlineHeaders = false, MailboxSnapshotAction snapshotAction = MailboxSnapshotAction.None)
    {
        if (scope == InboxProblemScope.List) { _lastFailureWasSelectedFolders = selectedFoldersDownload; _lastFailureWasOfflineHeaders = offlineHeaders; _lastSnapshotAction = snapshotAction; }
        // A newer selection invalidates callbacks even if a receiver completes after cancellation.
        PendingPermanentDelete = null; _deleteProfile = null;
        CancelPending();
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        int generation = ++_generation;
        began?.Invoke(generation); _downloadingBodies = downloading; _changingFlags = changingFlags; _moving = moving; _deleting = deleting;
        IsBusy = true;
        _running = scope;
        // A new attempt replaces its pane's earlier explanation; its own outcome decides what is shown
        // next. A page also replaces the message's, but loading a message leaves the list's: its rows
        // are still from an earlier receive.
        MessageProblem = null;
        if (scope == InboxProblemScope.List) ListProblem = null;
        // The operation's progress, outcome, or cancellation is the status until another operation starts.
        _statusScope = scope;
        Status = busy;
        Notify();
        T? result = default;
        string? failure = null;
        try
        {
            result = await Task.Run(() => operation(cancellation.Token), cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { failure = "Mail operation canceled. You can retry."; }
        catch (MailConnectionException error) { failure = error.Message; }
        catch (MailMailboxSnapshotUnavailableException error) { failure = error.Message; }
        catch (OfflineHeaderChangedException error) { failure = error.Message; }
        catch (OfflineBatchEpochChangedException error) { failure = error.Message; }
        catch (MailSearchScopeChangedException error) { failure = error.Message; }
        catch (MailSearchIndexUnavailableException error) { failure = error.Message; }
        catch (Exception error) when (cache is not null && error is IOException or InvalidDataException)
        { failure = "Local mail storage could not be read or saved. Check free disk space, quota and profile/cache recovery instructions. " +
            (IsSearchActive ? "Saved search did not complete; no server search was sent." : downloading ? "The current body's save was not confirmed." : "No new download was confirmed as saved."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { failure = "Mail could not be loaded. Check the saved account and connection, then retry."; }
        if (cache is IMailCacheRecovery recovery)
        {
            try { await recovery.GetPendingRecoveryCountAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { failure ??= "Saved recovery state could not be read; server mutations are disabled until it is repaired."; }
        }
        if (snapshotAction == MailboxSnapshotAction.Capture && failure is not null) failure += " A checkpoint may already have committed; Inspect saved header snapshot before retrying. No server mail was changed.";
        try
        {
            dispatcher.Post(() =>
            {
                if (_disposed || generation != _generation) return;
                _operation = null;
                IsBusy = _loadingPage = _downloadingBodies = _changingFlags = _moving = _deleting = false;
                if (failure is not null && _loadingNavigation) { _loadingNavigation = false; NavigationNotice = "Navigation restore did not finish. Reload navigation to retry; ordinary folder navigation is available."; }
                if (failure is null) commit(result!);
                else if (deleting)
                { DeleteNotice = Status = UncertainDelete; }
                else if (moving)
                { MoveNotice = Status = UncertainMove; }
                else if (changingFlags)
                {
                    FlagChangeNotice = Status = UncertainFlagState;
                }
                else
                {
                    if (offlineHeaders) failure = $"Selected-folder headers stopped: {OfflineHeaderPagesSaved} pages / {OfflineHeadersSaved} headers confirmed saved. Retry retains completed pages; Restart if the server continuation changed. {failure}";
                    if (downloading) failure = $"Offline download stopped: {DownloadCompleted}/{DownloadTotal} bodies confirmed available. Saved bodies were kept. {failure}";
                    Status = failure + (Messages.Count > 0 ? $" The previously loaded {MailboxNoun} is still shown." : "");
                    SetProblem(scope, scope == InboxProblemScope.List && Messages.Count > 0 ? failure + " The messages below are from the last successful receive." : failure,
                        canceled: cancellation.IsCancellationRequested);
                }
                Notify();
            });
        }
        catch (ObjectDisposedException) { /* The native window has closed. */ }
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=1; Fingerprint=C714C0
    // Broiler-Falsified-If: StopPending does not advance the generation, so a commit already queued on the dispatcher still runs
    // Broiler-Human:        PENDING
    private void StopPending()
    {
        if (_loadingNavigation) { _loadingNavigation = false; NavigationNotice = "Navigation restore canceled. Ordinary folder navigation is available; Reload saved navigation to retry."; }
        PendingPermanentDelete = null; _deleteProfile = null;
        if (_deleting) DeleteNotice = UncertainDelete;
        if (_moving) MoveNotice = UncertainMove;
        ++_generation;
        // A dispatcher callback may be queued after the worker disposed its source.
        CancelPending();
        IsBusy = _loadingPage = _downloadingBodies = _changingFlags = _moving = _deleting = false;
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=3; Fingerprint=14B7E4
    // Broiler-Human:        PENDING
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=259239
    // Broiler-Falsified-If: an operation that completes after Dispose still changes Messages, Body or Status
    // Broiler-Human:        PENDING
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _offlineContext.Cancel(); _offlineContext.Dispose();
        StopPending();
    }
}
