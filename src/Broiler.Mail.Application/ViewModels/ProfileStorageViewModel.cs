using Broiler.Mail.Core.Services;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

public sealed class ProfileStorageViewModel(IProfileStorage storage, IUiDispatcher dispatcher, Action<string>? openProfile = null) : IDisposable
{
    private CancellationTokenSource? _operation;
    private bool _disposed;
    public event EventHandler? Changed;
    public event Action? CleanupStarting;
    public event Action<bool>? CleanupFinished;
    public event Action? PolicyChangeStarting;
    public event Action? PolicyChangeFinished;
    public event Action<ProfileStoragePolicy>? PolicyChanged;
    public Func<bool>? CanStartSnapshot { get; set; }
    public Func<bool>? CanStartOperation { get; set; }
    public Func<Task<bool>>? SaveDraftBeforeSnapshot { get; set; }
    public string ProfileDirectory => storage.ProfileDirectory;
    public string BackupDestination { get; set; } = Path.Combine(storage.ProfileDirectory, "backups", "profile-" + Guid.NewGuid().ToString("N"));
    public string RestoreSource { get; set; } = "";
    public string RestoreDestination { get; set; } = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(storage.ProfileDirectory)) ?? storage.ProfileDirectory, "Mail-restored-" + Guid.NewGuid().ToString("N"));
    public string? RestoredProfile { get; private set; }
    public bool CanOpenRestored => !_disposed && !IsBusy && RestoredProfile is not null && openProfile is not null;
    public bool IsBusy { get; private set; }
    public bool IsSnapshotBusy { get; private set; }
    public bool ConfirmClear { get; private set; }
    public bool IsError { get; private set; }
    public string Status { get; private set; } = "Refresh usage to inspect local mail storage.";
    public ProfileStorageInfo? Info { get; private set; }
    public ProfileStoragePolicy SavedPolicy { get; private set; } = storage.Policy;
    public OfflineDownloadPolicy SelectedDownloadPolicy { get; set; } = storage.Policy.Downloads;
    public string QuotaMiB { get; set; } = (storage.Policy.MaximumBytes / (1024L * 1024)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public Task SavePolicyAsync()
    {
        string quota = QuotaMiB; var downloads = SelectedDownloadPolicy;
        return RunAsync(async token =>
        {
            if (!int.TryParse(quota, out int mib) || mib is < 1 or > 16384 || !Enum.IsDefined(downloads))
                throw new ArgumentException("Choose a whole cache limit from 1 to 16384 MiB and a supported download policy.");
            var applied = await storage.SetPolicyAsync(new(mib * 1024L * 1024, downloads), token).ConfigureAwait(false);
            return () => { SavedPolicy = applied; Info = null; PolicyChanged?.Invoke(applied);
                Status = "Storage policy saved and applied immediately. Existing downloaded mail was kept; automatic eviction is disabled."; };
        }, "Applying download policy and cache quota…", policyChange: true);
    }
    public string Usage => Info is { } value
        ? $"Cache: {MiB(value.DatabaseBytes)} MiB of {MiB(value.LimitBytes)} MiB; {value.HeaderCount} headers, {value.DownloadedBodyCount} downloaded bodies. Backups in this profile: {MiB(value.BackupBytes)} MiB."
        : "Storage usage has not been refreshed.";
    public void RequestClear() { if (_disposed || IsBusy) return; ConfirmClear = true; Changed?.Invoke(this, EventArgs.Empty); }
    public void CancelClear() { ConfirmClear = false; Changed?.Invoke(this, EventArgs.Empty); }
    public void Cancel() { try { _operation?.Cancel(); } catch (ObjectDisposedException) { } }

    public IReadOnlyList<MailUncertainAction> UncertainActions { get; private set; } = [];
    public Guid? SelectedUncertainActionId { get; private set; }
    public MailUncertainAction? PendingUncertainDismissal { get; private set; }
    public void SelectUncertainAction(Guid? id)
    {
        if (_disposed || IsBusy || PendingUncertainDismissal is not null) return;
        SelectedUncertainActionId = UncertainActions.Any(action => action.Id == id) ? id : null; Changed?.Invoke(this, EventArgs.Empty);
    }
    public Task ReviewUncertainActionsAsync() => storage is IMailCacheRecovery recovery && PendingUncertainDismissal is null ? RunAsync(async token =>
    {
        var actions = await recovery.ReadUncertainActionsAsync(token).ConfigureAwait(false);
        return () => { UncertainActions = actions; SelectedUncertainActionId = null; Status = $"{actions.Count} uncertain server actions. Check the message and destination with the provider before dismissing a record. No server action was repeated."; };
    }, "Reading saved uncertain actions…") : Task.CompletedTask;
    public void RequestUncertainDismissal()
    {
        if (_disposed || IsBusy) return;
        PendingUncertainDismissal = UncertainActions.SingleOrDefault(action => action.Id == SelectedUncertainActionId); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void CancelUncertainDismissal() { PendingUncertainDismissal = null; Changed?.Invoke(this, EventArgs.Empty); }
    public Task DismissUncertainActionAsync()
    {
        if (storage is not IMailCacheRecovery recovery || PendingUncertainDismissal is not { } pending || IsBusy) return Task.CompletedTask;
        PendingUncertainDismissal = null;
        return RunAsync(async token =>
        {
            await recovery.DismissUncertainActionAsync(pending.Id, token).ConfigureAwait(false);
            var actions = await recovery.ReadUncertainActionsAsync(token).ConfigureAwait(false);
            return () => { UncertainActions = actions; SelectedUncertainActionId = null; Status = "Uncertain action record dismissed after a verified backup. Cached mail was kept; receive folders to check current server state. No server action was repeated."; };
        }, "Backing up and dismissing the checked uncertain record…", cleanup: true, snapshot: true);
    }

    public bool SupportsSearchIndex => storage is IMailSearchIndex;
    public string SearchIndexStatus { get; private set; } = "Search index has not been inspected. It covers saved headers/previews, not server-only mail.";
    public Task InspectSearchIndexAsync() => storage is IMailSearchIndex index ? RunAsync(async token =>
    {
        var info = await index.GetSearchIndexInfoAsync(token).ConfigureAwait(false);
        return () => { SearchIndexStatus = $"{info.Status} {info.Documents} indexed rows; {info.Bytes / 1024} KiB."; Status = SearchIndexStatus; };
    }, "Inspecting saved-text search index…") : Task.CompletedTask;
    public Task RebuildSearchIndexAsync() => ChangeSearchIndexAsync(disable: false);
    public Task DisableSearchIndexAsync() => ChangeSearchIndexAsync(disable: true);
    private Task ChangeSearchIndexAsync(bool disable) => storage is IMailSearchIndex index ? RunAsync(async token =>
    {
        if (disable) await index.DisableSearchIndexAsync(token).ConfigureAwait(false); else await index.RebuildSearchIndexAsync(token).ConfigureAwait(false);
        var info = await index.GetSearchIndexInfoAsync(token).ConfigureAwait(false);
        return () => { SearchIndexStatus = info.Status; Status = disable ? "Search index disabled. Saved mail was kept; literal search remains available." : "Search index rebuilt from saved headers, previews and retained decoded bodies. No server mail was downloaded; older preview-only text was not recovered."; };
    }, disable ? "Disabling derived search index…" : "Rebuilding saved-text index…", cleanup: true, snapshot: true) : Task.CompletedTask;
    public bool SupportsRepair => storage is IMailCacheRecovery;
    public Task RepairAsync() => storage is IMailCacheRecovery recovery ? RunAsync(async token =>
    {
        var result = await recovery.RepairAsync(token).ConfigureAwait(false);
        return () => { Status = $"Local repair: {result.Repaired} completed; {result.Pending} pending. No server operations were sent."; IsError = result.Pending > 0; Info = null; };
    }, "Repairing confirmed local mail changes…", cleanup: true, snapshot: true) : Task.CompletedTask;

    public Task RefreshAsync() => RunAsync(async token =>
    {
        var info = await storage.GetInfoAsync(token).ConfigureAwait(false);
        return () => { Info = info; Status = "Storage usage refreshed."; };
    }, "Reading storage usage…");

    public Task BackupAsync()
    {
        string destination = BackupDestination;
        return RunAsync(async token =>
        {
            var saved = await storage.CreateBackupAsync(destination, token).ConfigureAwait(false);
            return () => { RestoreSource = saved; Status = $"Verified backup created at {saved}. OS credentials are not included."; };
        }, "Creating and verifying profile backup…", snapshot: true);
    }

    public Task RestoreAsync()
    {
        string source = RestoreSource, destination = RestoreDestination;
        return RunAsync(async token =>
        {
            var restored = await storage.RestoreBackupAsync(source, destination, token).ConfigureAwait(false);
            return () => { RestoredProfile = restored; Status = $"Backup restored as a separate profile at {restored}. The current profile is unchanged."; };
        }, "Verifying and restoring a separate profile…");
    }

    public Task ClearAsync()
    {
        if (!ConfirmClear || IsBusy || _disposed) return Task.CompletedTask;
        string destination = BackupDestination;
        ConfirmClear = false;
        return RunAsync(async token =>
        {
            var result = await storage.ClearDownloadedMailAsync(destination, token).ConfigureAwait(false);
            return () => { Info = null; RestoreSource = result.BackupDirectory;
                Status = $"Downloaded mail cleared. A verified recovery backup is at {result.BackupDirectory}. Drafts, accounts and server mail were kept." +
                    (result.Compacted ? "" : " The database could not be compacted; reusable space remains inside it."); };
        }, "Backing up, then clearing downloaded mail…", cleanup: true, snapshot: true);
    }

    public void OpenRestored()
    {
        if (!CanOpenRestored) return;
        try { openProfile!(RestoredProfile!); Status = "The restored profile was opened in a separate Mail window."; IsError = false; }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Status = "The restored profile could not be opened. " + error.Message; IsError = true; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(Func<CancellationToken, Task<Action>> operation, string busy, bool cleanup = false, bool snapshot = false, bool policyChange = false)
    {
        if (_disposed || IsBusy) return;
        if (CanStartOperation?.Invoke() == false)
        { Status = "Finish or cancel all account receives before local storage operations. Cancellation must finish before maintenance can start."; IsError = false; Changed?.Invoke(this, EventArgs.Empty); return; }
        if (snapshot && CanStartSnapshot?.Invoke() == false)
        { Status = "Wait for the current compose operation to finish before backing up or clearing downloaded mail."; IsError = false; Changed?.Invoke(this, EventArgs.Empty); return; }
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        IsBusy = true; IsSnapshotBusy = snapshot; IsError = false; Status = busy;
        if (cleanup) CleanupStarting?.Invoke();
        if (policyChange) PolicyChangeStarting?.Invoke();
        Changed?.Invoke(this, EventArgs.Empty);
        Action? commit = null; string? error = null; bool canceled = false;
        try
        {
            if (snapshot && SaveDraftBeforeSnapshot is not null && !await SaveDraftBeforeSnapshot().ConfigureAwait(false))
                throw new IOException("The current draft could not be saved. Resolve its save error before backing up.");
            cancellation.Token.ThrowIfCancellationRequested();
            commit = await operation(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { canceled = true; error = "Storage operation canceled. Any completed backup was kept; the current profile was not replaced."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "Storage operation could not finish. " + failure.Message; }
        try
        {
            dispatcher.Post(() =>
            {
                if (_disposed) return;
                IsBusy = IsSnapshotBusy = false; _operation = null;
                if (error is null) commit!(); else { IsError = !canceled; Status = error; }
                if (cleanup) CleanupFinished?.Invoke(error is null);
                if (policyChange) PolicyChangeFinished?.Invoke();
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (ObjectDisposedException) { }
    }

    private static string MiB(long bytes) => (bytes / (1024d * 1024)).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
    public void Dispose() { _disposed = true; try { _operation?.Cancel(); } catch (ObjectDisposedException) { } }
}
