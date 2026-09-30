// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   30
// Annotated:        30/30
// Exempt:           20
// Human-reviewed:   0/30
// IP risk:          Low
// Security risk:    High
// Criteria:         25/5
// Resource impact:  7/10 max
// Unverified:       30
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Application.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>One recoverable composition with serialized autosave and explicit submission outcomes.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=7; Fingerprint=9A6344
// Broiler-Falsified-If: a draft whose submission ended Accepted or Unknown is handed to the sender a second time
// Broiler-Human:        PENDING
public sealed class ComposerViewModel : IDisposable
{
    private AccountProfile? _account;
    private MailDraft? _seed;
    private readonly DraftJournal _journal;
    private readonly IUiDispatcher _dispatcher;
    private readonly IMailSender? _sender;
    private readonly ISentCopyWriter? _sentCopies;
    private readonly string? _loadError;
    private bool _disposed;
    private bool _closing;
    // Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=Medium; Resources=4; Fingerprint=6DD1B6
    // Broiler-Falsified-If: a draft saved in the Sending state reopens as Editing and can be sent again
    // Broiler-Human:        PENDING
    public ComposerViewModel(IDraftStore? store = null, DraftStoreState? initial = null,
        IUiDispatcher? dispatcher = null, IMailSender? sender = null, string? loadError = null, ISentCopyWriter? sentCopies = null)
    {
        initial ??= new(0, null);
        _journal = new(store ?? new MemoryDraftStore(), initial);
        _dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        _sender = sender;
        _sentCopies = sentCopies;
        _loadError = loadError;
        if (initial.Draft is { } saved)
        {
            _seed = saved.Draft;
            To = saved.ToText; Cc = saved.CcText; Bcc = saved.BccText;
            Subject = saved.Draft.Subject; PlainText = saved.Draft.PlainText;
            SubmissionState = saved.State == DraftSubmissionState.Sending ? DraftSubmissionState.Unknown : saved.State;
            SentCopy = saved.SentCopy == SentCopyState.Pending ? SentCopyState.Unknown : saved.SentCopy;
            SentCopyFolder = saved.SentCopyFolder;
            Status = "Recovered your saved draft.";
        }
        _journal.Changed += OnStorageChanged;
        if (_loadError is null && (initial.Draft?.State == DraftSubmissionState.Sending || initial.Draft?.SentCopy == SentCopyState.Pending)) Persist();
    }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=8C487F
    // Broiler-Human:        PENDING
    public event EventHandler? Changed;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=4DADC2
    // Broiler-Human:        PENDING
    public Guid? DraftId => _seed?.Id;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=D61F57
    // Broiler-Human:        PENDING
    public bool HasDraft => _seed is not null;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=F7FD23
    // Broiler-Falsified-If: Start is offered while a saved draft exists, so a new message would replace it
    // Broiler-Human:        PENDING
    public bool CanStart => !_disposed && !_closing && _loadError is null && !IsBusy && !HasDraft && _account is { IsEnabled: true };
    public bool IsBusy { get; private set; }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=7702C6
    // Broiler-Human:        PENDING
    public bool HasLoadError => _loadError is not null;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=DFD2EA
    // Broiler-Falsified-If: CanEdit is true for a draft in the Accepted or Unknown state
    // Broiler-Human:        PENDING
    public bool CanEdit => !_disposed && _loadError is null && !_closing && HasDraft && !IsBusy && SubmissionState is DraftSubmissionState.Editing or DraftSubmissionState.Failed;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=81BEA1
    // Broiler-Falsified-If: Discard is offered while a send or discard is still in progress
    // Broiler-Human:        PENDING
    public bool CanDiscard => !_disposed && _loadError is null && !_closing && HasDraft && !IsBusy;
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=28DC6C
    // Broiler-Falsified-If: CanSend is true for an account that has no outgoing server
    // Broiler-Human:        PENDING
    public bool CanSend => CanEdit && _sender?.IsAvailable == true && _account?.OutgoingServer is not null;
    public DraftSubmissionState SubmissionState { get; private set; }
    public SentCopyState SentCopy { get; private set; }
    public string? SentCopyFolder { get; private set; }
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=791967
    // Broiler-Falsified-If: the Failed or Unknown Sent-copy text tells the user to resend the message
    // Broiler-Human:        PENDING
    public string SentCopyText => SentCopy switch
    {
        SentCopyState.ProviderManaged => "Sent copy: managed by your provider. Broiler.Mail has not verified the copy.",
        SentCopyState.Pending => $"Sent copy: saving to {SentCopyFolder}… SMTP has already accepted the message.",
        SentCopyState.Saved => $"Sent copy: saved to {SentCopyFolder}.",
        SentCopyState.Failed => $"Sent copy: not saved to {SentCopyFolder}. Check the IMAP password, folder path, and permissions. SMTP acceptance is unchanged; do not resend. The composition is retained locally.",
        SentCopyState.Unknown => $"Sent copy: outcome unknown for {SentCopyFolder}. Check the folder before copying manually. No automatic retry or resend.",
        _ => SubmissionState == DraftSubmissionState.Accepted ? "Sent copy: not configured; no app copy was attempted." : "Sent copy: not attempted.",
    };
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=36133A
    // Broiler-Falsified-If: the Unknown submission state is described as a failure that may be retried
    // Broiler-Human:        PENDING
    public string SubmissionText => SubmissionState switch
    {
        DraftSubmissionState.Sending => "Sending… Do not close the app.",
        DraftSubmissionState.Accepted => "Accepted by the server. Delivery is not guaranteed. This draft will not be sent again.",
        DraftSubmissionState.Failed => "Sending failed before acceptance. The draft is retained; retry only when ready.",
        DraftSubmissionState.Unknown => "Sending outcome unknown. Check the recipient/provider before starting another message; automatic resend is disabled.",
        _ => "Not sent.",
    };
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=EAA81F
    // Broiler-Falsified-If: StorageStatus reports that the draft is saved locally while the journal holds an unsaved edit
    // Broiler-Human:        PENDING
    public string StorageStatus => _loadError ?? _journal.Error ?? (!_journal.IsSaved ? "Saving draft…" :
        _journal.IsPersistent ? "Draft saved locally." : "Demo/headless draft is in memory only.");
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=376603
    // Broiler-Human:        PENDING
    public string FromAddress => _seed?.FromAddress ?? _account?.EmailAddress ?? "No saved account";
    public string To { get; private set; } = "";
    public string Cc { get; private set; } = "";
    public string Bcc { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string PlainText { get; private set; } = "";
    public string Status { get; private set; } = "Save an account, then start a new message or read a message to reply.";

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=5A97FB
    // Broiler-Falsified-If: after the saved account changes its SMTP server, a send still uses the previous AccountProfile
    // Broiler-Human:        PENDING
    public void SetAccount(AccountProfile? account)
    {
        if (_account == account) return;
        _account = account;
        Status = HasDraft ? "Your draft and its original sender are retained."
            : CanStart ? "Start a new message, or read a message in Inbox to reply or forward." : "Save and enable an account to compose mail.";
        Notify();
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=0874DB
    // Broiler-Falsified-If: StartNew replaces an existing draft instead of refusing with the retained-draft status
    // Broiler-Human:        PENDING
    public bool StartNew() => Start(null, null);
    public bool StartFromMessage(MailMessageBody body, CompositionKind kind) => Start(body, kind);

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=661BA7
    // Broiler-Falsified-If: a reply whose original sender address has a quoted local part containing a comma becomes two recipients when the joined To text is parsed again
    // Broiler-Human:        PENDING
    private bool Start(MailMessageBody? body, CompositionKind? kind)
    {
        if (!CanStart)
        {
            Status = HasDraft ? "Your existing draft is retained. Discard it explicitly before starting another." : "Save and enable an account first.";
            Notify();
            return false;
        }
        try { _seed = body is null ? MailComposition.Create(_account!) : MailComposition.Create(_account!, body, kind!.Value); }
        catch (ArgumentException error) { Status = error.Message; Notify(); return false; }
        To = string.Join(", ", _seed.To);
        Cc = string.Join(", ", _seed.Cc);
        Bcc = string.Join(", ", _seed.Bcc);
        Subject = _seed.Subject;
        PlainText = _seed.PlainText;
        SubmissionState = DraftSubmissionState.Editing;
        SentCopy = SentCopyState.NotRequested;
        SentCopyFolder = null;
        Status = "Draft started. Changes are saved automatically.";
        Persist();
        Notify();
        return true;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=CF549E
    // Broiler-Falsified-If: an Edit on an Accepted or Unknown draft resets the submission state to Editing so the message can be sent again
    // Broiler-Human:        PENDING
    public void Edit(string to, string cc, string bcc, string subject, string body)
    {
        if (!CanEdit) return;
        To = to; Cc = cc; Bcc = bcc; Subject = subject; PlainText = body;
        SubmissionState = DraftSubmissionState.Editing;
        Status = "Draft edited.";
        Persist();
        Notify();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=42EE4E
    // Broiler-Falsified-If: a draft started under one account builds after the saved account changed its EmailAddress, keeping the old From address instead of throwing
    // Broiler-Human:        PENDING
    public MailDraft BuildDraft()
    {
        if (_seed is null) throw new InvalidOperationException("Start a draft first.");
        if (_account is not { IsEnabled: true } || _account.Id != _seed.AccountId || _account.EmailAddress != _seed.FromAddress)
            throw new InvalidOperationException("The saved sender changed. This draft retains its original sender; restore that account identity or start a new draft.");
        var to = MailComposition.ParseRecipients(To);
        var cc = MailComposition.ParseRecipients(Cc);
        var bcc = MailComposition.ParseRecipients(Bcc);
        var draft = _seed with { To = to, Cc = cc, Bcc = bcc, Subject = Subject, PlainText = PlainText };
        MailComposition.ValidateDraft(_account, draft);
        return draft;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=4313D4
    // Broiler-Falsified-If: an invalid recipient makes CheckDraft throw instead of setting the validation message as the status
    // Broiler-Human:        PENDING
    public void CheckDraft()
    {
        try { _ = BuildDraft(); Status = "Draft fields are valid. No mail was sent."; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Status = error.Message; }
        Notify();
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=70FAA8
    // Broiler-Falsified-If: SaveAsync writes to the draft store after the saved draft failed to load
    // Broiler-Human:        PENDING
    public Task<bool> SaveAsync() => _loadError is null ? _journal.FlushAsync() : Task.FromResult(false);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=353A42
    // Broiler-Falsified-If: PrepareCloseAsync returns true while an edit has not been written by the draft store
    // Broiler-Human:        PENDING
    public async Task<bool> PrepareCloseAsync()
    {
        if (IsBusy) return false;
        _closing = true;
        Notify();
        bool saved = _journal.IsSaved || await SaveAsync().ConfigureAwait(false);
        if (!saved) await OnUiAsync(() => { _closing = false; Notify(); }).ConfigureAwait(false);
        return saved;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=57BE7C
    // Broiler-Falsified-If: a discard whose tombstone write fails still clears the composer fields and loses the draft text
    // Broiler-Human:        PENDING
    public async Task<bool> DiscardAsync()
    {
        if (!CanDiscard) return false;
        IsBusy = true;
        Notify();
        var previous = Snapshot();
        _journal.Update(null);
        bool saved = await _journal.FlushAsync().ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            if (saved)
            {
                _seed = null;
                To = Cc = Bcc = Subject = PlainText = "";
                SubmissionState = DraftSubmissionState.Editing;
                SentCopy = SentCopyState.NotRequested;
                SentCopyFolder = null;
                Status = "Draft discarded from local storage.";
            }
            else
            {
                _journal.Update(previous, save: false);
                Status = "Draft could not be discarded. Its text remains open.";
            }
            IsBusy = false;
            Notify();
        }).ConfigureAwait(false);
        return saved;
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=Low; Security=High; Resources=7; Fingerprint=BEF7BA
    // Broiler-Falsified-If: the sender is invoked before the draft store has written the Sending snapshot
    // Broiler-Human:        PENDING
    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSend) return;
        MailDraft draft;
        try { draft = BuildDraft(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Status = error.Message; Notify(); return; }
        var account = _account!;
        draft = draft with { SubmissionDate = DateTimeOffset.UtcNow };
        _seed = _seed! with { SubmissionDate = draft.SubmissionDate };
        IsBusy = true;
        SubmissionState = DraftSubmissionState.Sending;
        SentCopy = SentCopyState.NotRequested;
        SentCopyFolder = account.SentCopyMode == SentCopyMode.AppendToFolder ? account.SentFolder : null;
        Persist();
        Notify();
        // Persist intent before crossing the network boundary. An interrupted Sending record recovers as Unknown.
        if (!await _journal.FlushAsync().ConfigureAwait(false))
        {
            await OnUiAsync(() =>
            {
                SubmissionState = DraftSubmissionState.Failed;
                IsBusy = false;
                Status = "Sending was not started because the draft could not be saved.";
                _journal.Update(Snapshot(), save: false);
                Notify();
            }).ConfigureAwait(false);
            return;
        }
        DraftSubmissionState outcome;
        string? outcomeMessage = null;
        try
        {
            var result = await Task.Run(() => _sender!.SendAsync(account, draft, cancellationToken), cancellationToken).ConfigureAwait(false);
            outcomeMessage = result.StatusMessage;
            outcome = result.Status switch
            {
                SubmissionStatus.Accepted => DraftSubmissionState.Accepted,
                SubmissionStatus.Rejected => DraftSubmissionState.Failed,
                _ => DraftSubmissionState.Unknown,
            };
        }
        // Exceptions/cancellation after invoking a transport cannot prove that a server did not accept mail.
        catch (Exception)
        { outcome = DraftSubmissionState.Unknown; }
        await OnUiAsync(() =>
        {
            SubmissionState = outcome;
            if (outcome == DraftSubmissionState.Accepted)
                SentCopy = account.SentCopyMode switch
                {
                    SentCopyMode.ProviderManaged => SentCopyState.ProviderManaged,
                    SentCopyMode.AppendToFolder => SentCopyState.Pending,
                    _ => SentCopyState.NotRequested,
                };
            Persist(); Notify();
        }).ConfigureAwait(false);
        bool durable = await _journal.FlushAsync().ConfigureAwait(false);
        if (outcome == DraftSubmissionState.Accepted && account.SentCopyMode == SentCopyMode.AppendToFolder)
        {
            if (durable)
            {
                // The accepted/pending record must reach disk before the one append attempt.
                SentCopyState copy;
                try
                {
                    copy = _sentCopies is null ? SentCopyState.Failed : await Task.Run(
                        () => _sentCopies.AppendAsync(account, draft, cancellationToken)).ConfigureAwait(false);
                    if (copy is not (SentCopyState.Saved or SentCopyState.Failed or SentCopyState.Unknown)) copy = SentCopyState.Unknown;
                }
                catch (Exception) { copy = SentCopyState.Unknown; }
                await OnUiAsync(() => { SentCopy = copy; Persist(); Notify(); }).ConfigureAwait(false);
                durable = await _journal.FlushAsync().ConfigureAwait(false);
            }
            else
                await OnUiAsync(() => { SentCopy = SentCopyState.Failed; _journal.Update(Snapshot(), save: false); Notify(); }).ConfigureAwait(false);
        }
        await OnUiAsync(() =>
        {
            IsBusy = false;
            Status = durable ? outcomeMessage ?? "Submission result saved. The draft is retained." : "Submission or Sent-copy result could not be saved. Keep the app open and retry Save draft; do not resend or repeat the copy.";
            Notify();
        }).ConfigureAwait(false);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=654915
    // Broiler-Falsified-If: a snapshot taken during submission omits the Sending state, so a crash during SMTP reopens the draft as Editing
    // Broiler-Human:        PENDING
    private DraftSnapshot? Snapshot() => _seed is null ? null : new()
    {
        Draft = _seed with { Subject = Subject, PlainText = PlainText },
        ToText = To, CcText = Cc, BccText = Bcc, State = SubmissionState,
        SentCopy = SentCopy, SentCopyFolder = SentCopyFolder,
    };
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=D6ED46
    // Broiler-Falsified-If: Persist hands the journal a snapshot with saving disabled, so an edit is never written
    // Broiler-Human:        PENDING
    private void Persist() => _journal.Update(Snapshot());
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=276C32
    // Broiler-Falsified-If: Changed is raised on the draft journal worker thread instead of through the UI dispatcher
    // Broiler-Human:        PENDING
    private void OnStorageChanged(object? sender, EventArgs args)
    {
        try { _dispatcher.Post(() => { if (!_disposed) Notify(); }); }
        catch (ObjectDisposedException) { }
    }
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=542ACA
    // Broiler-Falsified-If: an exception thrown by the posted action is swallowed and the awaiting SendAsync continues as if the update ran
    // Broiler-Human:        PENDING
    private Task OnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { _dispatcher.Post(() => { try { action(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } }); }
        catch (ObjectDisposedException error) { completion.SetException(error); }
        return completion.Task;
    }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=E8AC04
    // Broiler-Falsified-If: Changed is raised after Dispose
    // Broiler-Human:        PENDING
    private void Notify() { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=56EEDB
    // Broiler-Falsified-If: a draft write completing after Dispose still raises Changed on the disposed view model
    // Broiler-Human:        PENDING
    public void Dispose() { _disposed = true; _journal.Changed -= OnStorageChanged; }
}
