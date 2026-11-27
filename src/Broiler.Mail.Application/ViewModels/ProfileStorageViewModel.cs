// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   49
// Annotated:        0/49
// Exempt:           26
// Human-reviewed:   0/49
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       49
//
// GENERATED - DO NOT EDIT MANUALLY

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
    /// <summary>The message shown when a chosen folder holds no backup manifest.</summary>
    public const string NotABackupMessage = "That folder is not a Broiler Mail backup. Choose a folder created by Create verified backup.";
    /// <summary>The folder new backups are created in; the profile's own backups folder unless one was chosen.</summary>
    public string BackupLocation { get; private set; } = DefaultBackupLocation(storage.ProfileDirectory);
    public bool IsDefaultBackupLocation => SamePath(BackupLocation, DefaultBackupLocation(storage.ProfileDirectory));
    /// <summary>The new folder the next backup or cleanup backup is written to. Generated inside <see cref="BackupLocation"/>
    /// and generated again after every successful backup, so a second backup never meets the first one's folder.</summary>
    public string BackupDestination { get; set; } = NewBackupDestination(DefaultBackupLocation(storage.ProfileDirectory));
    public string RestoreSource { get; set; } = "";
    /// <summary>The folder a restored profile is created in; the current profile's parent unless one was chosen.</summary>
    public string RestoreLocation { get; private set; } = DefaultRestoreLocation(storage.ProfileDirectory);
    public string RestoreDestination { get; set; } = NewRestoreDestination(DefaultRestoreLocation(storage.ProfileDirectory));
    /// <summary>True while <see cref="Status"/> is a warning, such as a picked folder this device cannot use.</summary>
    public bool IsWarning { get; private set; }

    /// <summary>Creates new backups in <paramref name="parent"/>. Refuses a path that is not fully qualified.</summary>
    public bool SetBackupLocation(string parent)
    {
        if (_disposed || IsBusy) return false;
        if (!TryFullPath(parent, out string location)) return Refuse("Choose a folder on this device for new backups. The backup location was not changed.");
        BackupLocation = location; BackupDestination = NewBackupDestination(location);
        return Inform($"New backups will be created in {location}.");
    }

    /// <summary>Creates new backups in the profile's own backups folder again.</summary>
    public void UseDefaultBackupLocation()
    {
        if (_disposed || IsBusy) return;
        BackupLocation = DefaultBackupLocation(storage.ProfileDirectory); BackupDestination = NewBackupDestination(BackupLocation);
        Inform($"New backups will be created in the profile's backups folder, {BackupLocation}.");
    }

    /// <summary>Creates restored profiles in <paramref name="parent"/>. Refuses a path that is not fully qualified.</summary>
    public bool SetRestoreLocation(string parent)
    {
        if (_disposed || IsBusy) return false;
        if (!TryFullPath(parent, out string location)) return Refuse("Choose a folder on this device for the restored profile. The restore location was not changed.");
        RestoreLocation = location; RestoreDestination = NewRestoreDestination(location);
        return Inform($"The backup will be restored into a new folder in {location}.");
    }

    /// <summary>Chooses the backup to restore. Refuses a folder without a backup manifest and keeps the previous choice.</summary>
    public bool SetRestoreSource(string folder)
    {
        if (_disposed || IsBusy) return false;
        if (!TryFullPath(folder, out string source) || !File.Exists(Path.Combine(source, "backup.json"))) return Refuse(NotABackupMessage);
        RestoreSource = source;
        return Inform($"Backup to restore: {source}. It is verified again before restoring.");
    }

    /// <summary>Shows <paramref name="message"/> as a warning without changing any choice.</summary>
    public void Warn(string message)
    {
        if (_disposed) return;
        Status = message; IsError = false; IsWarning = true; Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The path of a new child of <paramref name="parent"/> named <paramref name="name"/>: the name itself, then
    /// "name (2)" through "name (99)", then the name with a unique suffix. Existing files and folders are skipped.
    /// </summary>
    internal static string UniqueChild(string parent, string name)
    {
        static bool Taken(string path) => Directory.Exists(path) || File.Exists(path);
        string candidate = Path.Combine(parent, name);
        if (!Taken(candidate)) return candidate;
        for (int number = 2; number <= 99; number++)
            if (!Taken(candidate = Path.Combine(parent, $"{name} ({number})"))) return candidate;
        return Path.Combine(parent, name + " " + Guid.NewGuid().ToString("N"));
    }

    private static string DefaultBackupLocation(string profile) => Path.Combine(profile, "backups");
    private static string DefaultRestoreLocation(string profile) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(profile)) ?? profile;
    private static string NewBackupDestination(string location) =>
        UniqueChild(location, "Broiler Mail backup " + DateTime.Now.ToString("yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture));
    private static string NewRestoreDestination(string location) =>
        UniqueChild(location, "Mail-restored-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture));
    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool TryFullPath(string? path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); return true; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    private bool Refuse(string message) { Status = message; IsError = true; IsWarning = false; Changed?.Invoke(this, EventArgs.Empty); return false; }
    private bool Inform(string message) { Status = message; IsError = IsWarning = false; Changed?.Invoke(this, EventArgs.Empty); return true; }
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
            return () => { RestoreSource = saved; BackupDestination = NewBackupDestination(BackupLocation);
                Status = $"Verified backup created at {saved}. OS credentials are not included."; };
        }, "Creating and verifying profile backup…", snapshot: true);
    }

    public Task RestoreAsync()
    {
        string source = RestoreSource, destination = RestoreDestination;
        return RunAsync(async token =>
        {
            var restored = await storage.RestoreBackupAsync(source, destination, token).ConfigureAwait(false);
            return () => { RestoredProfile = restored; RestoreDestination = NewRestoreDestination(RestoreLocation); Status = $"Backup restored as a separate profile at {restored}. The current profile is unchanged."; };
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
            return () => { Info = null; RestoreSource = result.BackupDirectory; BackupDestination = NewBackupDestination(BackupLocation);
                Status = $"Downloaded mail cleared. A verified recovery backup is at {result.BackupDirectory}. Drafts, accounts and server mail were kept." +
                    (result.Compacted ? "" : " The database could not be compacted; reusable space remains inside it."); };
        }, "Backing up, then clearing downloaded mail…", cleanup: true, snapshot: true);
    }

    public void OpenRestored()
    {
        if (!CanOpenRestored) return;
        try { openProfile!(RestoredProfile!); Status = "The restored profile was opened in a separate Mail window."; IsError = IsWarning = false; }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Status = "The restored profile could not be opened. " + error.Message; IsError = true; IsWarning = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(Func<CancellationToken, Task<Action>> operation, string busy, bool cleanup = false, bool snapshot = false, bool policyChange = false)
    {
        if (_disposed || IsBusy) return;
        if (CanStartOperation?.Invoke() == false)
        { Status = "Finish or cancel all account receives before local storage operations. Cancellation must finish before maintenance can start."; IsError = IsWarning = false; Changed?.Invoke(this, EventArgs.Empty); return; }
        if (snapshot && CanStartSnapshot?.Invoke() == false)
        { Status = "Wait for the current compose operation to finish before backing up or clearing downloaded mail."; IsError = IsWarning = false; Changed?.Invoke(this, EventArgs.Empty); return; }
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        IsBusy = true; IsSnapshotBusy = snapshot; IsError = IsWarning = false; Status = busy;
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
