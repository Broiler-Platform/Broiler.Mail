using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class InboxViewModel
{
    private enum MailboxSnapshotAction { None, Capture, Inspect }
    private MailboxSnapshotAction _lastSnapshotAction;
    public bool SupportsMailboxSnapshots => cache is IMailMailboxSnapshotCache;
    public bool CanCaptureMailboxSnapshot => SupportsMailboxSnapshots && receiver is IMailMailboxSnapshotReceiver &&
        !_disposed && !_storageMaintenance && !_loadingNavigation && !IsBusy && !IsSearchActive && _account is { IsEnabled: true };
    public bool CanInspectMailboxSnapshot => SupportsMailboxSnapshots && !_disposed && !_storageMaintenance && !_loadingNavigation && !IsBusy && _account is not null;
    public string MailboxSnapshotNotice { get; private set; } = "Full-header snapshots are explicit and limited to 4,096 messages in the current folder. Membership is checked before and after fetching; absent downloaded mail is retained. Inspect reads only the saved checkpoint. No bodies, attachments or server changes are requested.";

    public Task CaptureMailboxSnapshotAsync()
    {
        if (!CanCaptureMailboxSnapshot) return Task.CompletedTask;
        var account = _account!; string folder = SelectedMailboxId; _loadingPage = true; _lastFailureWasDiscovery = _lastFailureWasDownload = false;
        return RunAsync(async token =>
        {
            var snapshot = await ((IMailMailboxSnapshotReceiver)receiver).GetMailboxSnapshotAsync(account, folder, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (snapshot is null || snapshot.Mailbox is null || snapshot.Messages is null || snapshot.Mailbox.AccountId != account.Id || snapshot.Mailbox.MailboxId != folder) throw new InvalidDataException("Snapshot returned another mailbox or invalid content.");
            var checkpoint = await ((IMailMailboxSnapshotCache)cache!).ApplyMailboxSnapshotAsync(snapshot, token).ConfigureAwait(false);
            ValidateCheckpoint(checkpoint, account.Id, folder);
            if (checkpoint.Mailbox != snapshot.Mailbox || checkpoint.ServerMessages != snapshot.Messages.Count) throw new InvalidDataException("Checkpoint does not match the captured snapshot.");
            var rows = await cache!.ReadSummariesAsync(snapshot.Mailbox, PageSize + 1, cancellationToken: token).ConfigureAwait(false);
            if (rows is null || rows.Count > PageSize + 1 || rows.Any(row => row is null || !snapshot.Mailbox.Contains(row.Key))) throw new InvalidDataException("Saved snapshot page has another mailbox identity.");
            return (checkpoint, rows);
        }, result =>
        {
            ResetMessages();
            Messages = result.rows.Take(PageSize).ToArray(); _hasOlderCached = result.rows.Count > PageSize; IsShowingCachedMail = true; HasLoaded = true; LastReceivedAt = result.checkpoint.ObservedAt;
            if (!_cachedMailboxIds.Contains(folder, StringComparer.Ordinal)) _cachedMailboxIds = _cachedMailboxIds.Append(folder).ToArray(); UpdateFolders();
            MailboxSnapshotNotice = CheckpointNotice(result.checkpoint);
            Status = "Complete header snapshot and checkpoint saved. Displaying saved headers, including retained messages absent at that observation. Inspect the checkpoint for coverage; ordinary Receive mail still fetches only a page.";
        }, "Reading all current-folder headers and verifying membership…", InboxProblemScope.List, snapshotAction: MailboxSnapshotAction.Capture);
    }

    public Task InspectMailboxSnapshotAsync()
    {
        if (!CanInspectMailboxSnapshot) return Task.CompletedTask;
        var account = _account!; string folder = SelectedMailboxId; _loadingPage = true; _lastFailureWasDiscovery = _lastFailureWasDownload = false;
        return RunAsync(async token =>
        {
            var checkpoint = await ((IMailMailboxSnapshotCache)cache!).ReadMailboxCheckpointAsync(account.Id, folder, token).ConfigureAwait(false);
            if (checkpoint is not null) ValidateCheckpoint(checkpoint, account.Id, folder);
            return checkpoint;
        }, checkpoint =>
        {
            MailboxSnapshotNotice = checkpoint is null ? "No complete header checkpoint is saved for this account/folder. Paged receive timestamps and offline batch cursors do not establish complete server membership." : CheckpointNotice(checkpoint);
            Status = "Saved header checkpoint inspected locally. No server request or mutation.";
        }, "Inspecting saved header checkpoint…", InboxProblemScope.List, snapshotAction: MailboxSnapshotAction.Inspect);
    }
    private static void ValidateCheckpoint(MailMailboxCheckpoint checkpoint, Broiler.Mail.Core.Accounts.AccountId account, string folder)
    {
        if (checkpoint is null || checkpoint.Mailbox is null || checkpoint.Mailbox.AccountId != account || checkpoint.Mailbox.MailboxId != folder || checkpoint.ServerMessages is < 0 or > MailMailboxSnapshot.MaximumMessages || checkpoint.ObservedAt == default || checkpoint.RetainedAbsentMessages < 0 || checkpoint.UidSetSha256 is null || checkpoint.UidSetSha256.Length != 64 || !checkpoint.UidSetSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved checkpoint identity or coverage is invalid.");
    }
    private static string CheckpointNotice(MailMailboxCheckpoint value) => $"Last complete header observation: account {value.Mailbox.AccountId}; folder {value.Mailbox.MailboxId}; UIDVALIDITY {value.Mailbox.UidValidity}; {value.ObservedAt:O}. {value.ServerMessages} server headers in the verified census. {value.RetainedAbsentMessages} currently saved headers in that epoch were absent from that observation and remain retained. UID-set SHA-256: {value.UidSetSha256}. This is historical membership, not live state or a body-download/rename/QRESYNC checkpoint. Later arrivals, changes and deletions require a new snapshot.";
}
