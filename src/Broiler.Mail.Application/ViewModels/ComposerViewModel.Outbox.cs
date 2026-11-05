using System.Globalization;
using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.UI.Forms;

namespace Broiler.Mail.Application.ViewModels;

public enum OutboxFilter { All, Unsent, InProgress, NeedsReview, Accepted }
public sealed record OutboxRecord(Guid Id, AccountId AccountId, string FromAddress, string Subject, bool IsActive,
    DraftSubmissionState Submission, SentCopyState SentCopy, DateTimeOffset? SubmissionDate, bool InterruptedSend, bool InterruptedCopy)
{
    public bool IsUnsent => Submission is DraftSubmissionState.Editing or DraftSubmissionState.Failed;
    public bool NeedsReview => Submission == DraftSubmissionState.Unknown || Submission == DraftSubmissionState.Accepted && SentCopy is SentCopyState.Failed or SentCopyState.Unknown;
    public bool IsInProgress => Submission == DraftSubmissionState.Sending || SentCopy == SentCopyState.Pending;
    public string SubmissionLabel => InterruptedSend ? "Unknown (interrupted sending)" : Submission.ToString();
    public string CopyLabel => InterruptedCopy ? "Unknown (interrupted Sent copy)" : SentCopy.ToString();
    public string Guidance => Submission switch
    {
        DraftSubmissionState.Editing => "Unsent local draft. Open in Compose to review; sending remains an explicit action.",
        DraftSubmissionState.Failed => "Submission failed before acceptance. Review the draft and connection before an explicit send.",
        DraftSubmissionState.Sending => "Submission is in progress. Do not close the application; no second send is available here.",
        DraftSubmissionState.Unknown => "Server acceptance is unknown. Check the provider/recipient before starting another send; no automatic resend.",
        _ => "SMTP accepted this message; delivery is not guaranteed. Do not resend because of a Sent-copy problem.",
    };
    public string CopyGuidance => SentCopy switch
    {
        SentCopyState.ProviderManaged => "Provider-managed copy; the app has not verified it.",
        SentCopyState.Pending => "The app is saving a Sent copy; SMTP acceptance is already recorded.",
        SentCopyState.Saved => "The app recorded a successful Sent-copy save.",
        SentCopyState.Failed => "Sent copy was not saved. Check the folder/credentials; do not resend the message.",
        SentCopyState.Unknown => "Sent-copy outcome is unknown. Check the folder before another copy; no automatic retry.",
        _ => "No app Sent copy was requested or attempted.",
    };
}

public sealed partial class ComposerViewModel
{
    public AccountProfile? OutboxAccount => _account;
    public OutboxFilter SelectedOutboxFilter { get; private set; }
    public IReadOnlyList<OutboxRecord> OutboxRecords => Array.AsReadOnly(AllOutboxRecords().Where(MatchesOutboxFilter).ToArray());
    private OutboxRecord[] AllOutboxRecords()
    {
        if (_loadError is not null || _account is null) return [];
        var rows = new List<OutboxRecord>();
        void Add(DraftSnapshot snapshot, bool active)
        {
            if (snapshot.Draft.AccountId != _account.Id) return;
            bool interruptedSend = snapshot.State == DraftSubmissionState.Sending && !(active && IsBusy);
            bool interruptedCopy = snapshot.SentCopy == SentCopyState.Pending && !(active && IsBusy);
            rows.Add(new(snapshot.Draft.Id,snapshot.Draft.AccountId,snapshot.Draft.FromAddress,snapshot.Draft.Subject,active,
                interruptedSend ? DraftSubmissionState.Unknown : snapshot.State,interruptedCopy ? SentCopyState.Unknown : snapshot.SentCopy,
                snapshot.Draft.SubmissionDate,interruptedSend,interruptedCopy));
        }
        if (Snapshot() is { } current) Add(current,true);
        foreach (var row in _savedDrafts) Add(row,false);
        return rows.ToArray();
    }
    private bool MatchesOutboxFilter(OutboxRecord row) => SelectedOutboxFilter switch
    {
        OutboxFilter.Unsent => row.IsUnsent, OutboxFilter.InProgress => row.IsInProgress, OutboxFilter.NeedsReview => row.NeedsReview,
        OutboxFilter.Accepted => row.Submission == DraftSubmissionState.Accepted, _ => true,
    };
    public void SetOutboxFilter(OutboxFilter filter)
    { if (_disposed || !Enum.IsDefined(filter) || SelectedOutboxFilter == filter) return; SelectedOutboxFilter=filter; Notify(); }
    public string OutboxNotice
    {
        get
        {
            if (_loadError is not null) return "Outbox unavailable: saved drafts could not be read. Repair or restore drafts before restarting; nothing is sent automatically.";
            if (_account is null) return "Select an account to view its retained local drafts and submission records.";
            var rows=AllOutboxRecords();
            return $"Outbox for {_account.EmailAddress}: {rows.Length} retained local records; {rows.Count(row=>row.IsUnsent)} unsent, {rows.Count(row=>row.IsInProgress)} in progress, {rows.Count(row=>row.NeedsReview)} needing review, {rows.Count(row=>row.Submission==DraftSubmissionState.Accepted)} SMTP accepted. No automatic sending, scheduling or retries. This is not complete sent history.";
        }
    }
    public bool CanOpenOutboxRecord(Guid id) => !_disposed && !_closing && !_profileStorageBusy && _loadError is null && !IsBusy &&
        OutboxRecords.Any(row=>row.Id==id && (row.IsActive || SupportsDraftLibrary));
    public async Task<bool> OpenOutboxRecordAsync(Guid id)
    {
        try { return await OpenOutboxRecordCoreAsync(id).ConfigureAwait(false); }
        catch (ObjectDisposedException) { return false; }
    }
    private async Task<bool> OpenOutboxRecordCoreAsync(Guid id)
    {
        if (!CanOpenOutboxRecord(id)) return false;
        var account=_account!.Id;
        if (_seed?.Id != id) return await ResumeDraftAsync(id).ConfigureAwait(false) && _account?.Id==account && _seed?.Id==id;
        IsBusy=true; Notify(); bool saved=await SaveAsync().ConfigureAwait(false), opened=false;
        await OnUiAsync(()=>
        {
            opened=saved && !_disposed && _account?.Id==account && _seed?.Id==id;
            IsBusy=false;
            Status=opened ? "Retained record ready in Compose; nothing was sent." : "Record was not opened. Resolve the current draft save problem or account change first.";
            StatusKind=opened ? FeedbackKind.Information : FeedbackKind.Error; Notify();
        }).ConfigureAwait(false);
        return opened;
    }
    public string OutboxDetails(Guid id)
    {
        var record=OutboxRecords.SingleOrDefault(row=>row.Id==id); if(record is null)return "";
        var snapshot=_seed?.Id==id ? Snapshot() : _savedDrafts.SingleOrDefault(row=>row.Draft.Id==id); if(snapshot is null)return "";
        var draft=snapshot.Draft;
        return string.Join("\n",new[]
        {
            $"Account: {OutboxDisplay(_account!.DisplayName,200)} <{OutboxDisplay(_account.EmailAddress,320)}>", $"Account ID: {record.AccountId}", $"Draft ID: {record.Id}",
            "Location: "+(record.IsActive ? "Current composer" : "Saved draft library"), "Pinned From: "+OutboxDisplay(draft.FromAddress,320),
            "Pinned display name: "+(draft.FromDisplayName is null ? "Legacy account-name fallback" : OutboxDisplay(draft.FromDisplayName,200)),
            "Submission date (UTC): "+(draft.SubmissionDate?.ToUniversalTime().ToString("u",CultureInfo.InvariantCulture) ?? "Not assigned"),
            "Subject: "+OutboxDisplay(draft.Subject,2000), "SMTP: "+record.SubmissionLabel, record.Guidance,
            "Sent copy: "+record.CopyLabel, "Sent folder: "+OutboxDisplay(snapshot.SentCopyFolder??"Not configured",1024), record.CopyGuidance,
            "To (raw edit): "+OutboxDisplay(snapshot.ToText,4096), "Cc (raw edit): "+OutboxDisplay(snapshot.CcText,4096), "Bcc (raw edit): "+OutboxDisplay(snapshot.BccText,4096),
            "Recipient fields are raw local edits (up to 4,096 characters each), not proof of delivery. Body text is omitted here. Opening in Compose never submits or repeats a Sent copy.",
        });
    }
    public static string OutboxDisplay(string value,int maximum)
    {
        var result=new StringBuilder();
        foreach(var rune in value.EnumerateRunes())
        {
            if(result.Length+rune.Utf16SequenceLength>maximum){result.Append("…");break;}
            result.Append(Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune)==UnicodeCategory.Format ? " " : rune.ToString());
        }
        return result.ToString();
    }
}
