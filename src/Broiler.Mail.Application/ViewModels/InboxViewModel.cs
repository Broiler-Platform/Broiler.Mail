// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   17
// Annotated:        17/17
// Exempt:           11
// Human-reviewed:   0/17
// IP risk:          Low
// Security risk:    Medium
// Criteria:         15/0
// Resource impact:  7/10 max
// Unverified:       17
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Owns the in-memory inbox. All state changes are published on the UI dispatcher.</summary>
// Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=7; Fingerprint=CAE5A8
// Broiler-Falsified-If: a body fetched for an earlier selection is committed after SelectAsync has picked another message, so Body no longer belongs to SelectedMessage
// Broiler-Human:        PENDING
/// <summary>Which pane an inbox problem belongs to, so its explanation and Retry appear beside it.</summary>
public enum InboxProblemScope { None, List, Message }

/// <summary>Why an inbox operation failed, or that the user canceled it, as its pane explains it.</summary>
public sealed record InboxProblem(string Text, bool IsCancellation);

public sealed class InboxViewModel(IMailReceiver receiver, IUiDispatcher dispatcher) : IDisposable
{
    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=Low; Resources=0; Fingerprint=75CF89
    // Broiler-Falsified-If: PageSize is larger than ImapMailReceiver.MaximumPageSize, so every receive is refused with ArgumentOutOfRangeException
    // Broiler-Human:        PENDING
    public const int PageSize = 50;
    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=None; Security=Medium; Resources=0; Fingerprint=FC3D90
    // Broiler-Falsified-If: repeated Load older leaves more than MaximumLoadedMessages plus one page of summaries in Messages
    // Broiler-Human:        PENDING
    public const int MaximumLoadedMessages = 500;
    private AccountProfile? _account;
    private MailInboxCursor? _older;
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _disposed;
    private bool _loadingPage;
    private InboxProblemScope _running;
    private InboxProblemScope _statusScope;
    private bool _lastPageWasOlder;

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
    public bool ListRetryLoadsOlder => ProblemIsOlderPage && _older is not null && Messages.Count < MaximumLoadedMessages;
    /// <summary>Whether <see cref="RetryAsync()"/> can repeat the operation of the last problem.</summary>
    public bool CanRetry => CanRetryIn(ProblemScope);

    /// <summary>Whether <see cref="RetryAsync(InboxProblemScope)"/> can repeat the operation of the problem of <paramref name="scope"/>.</summary>
    public bool CanRetryIn(InboxProblemScope scope) => !_disposed && !IsBusy && scope switch
    {
        InboxProblemScope.List => ListProblem is not null && CanReceive,
        InboxProblemScope.Message => MessageProblem is not null && SelectedMessage is not null && _account is not null,
        _ => false,
    };
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=A12292
    // Broiler-Falsified-If: CanReceive is true for a saved account whose IsEnabled is false
    // Broiler-Human:        PENDING
    public bool CanReceive => !_disposed && !IsBusy && _account is { IsEnabled: true };
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=C155F8
    // Broiler-Falsified-If: CanLoadOlder stays true after Messages.Count has reached MaximumLoadedMessages
    // Broiler-Human:        PENDING
    public bool CanLoadOlder => CanReceive && _older is not null && Messages.Count < MaximumLoadedMessages;
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=63EC38
    // Broiler-Falsified-If: CanSelect is true while a page load is still running
    // Broiler-Human:        PENDING
    public bool CanSelect => !_disposed && !_loadingPage;

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

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=3; Fingerprint=CC9274
    // Broiler-Falsified-If: a receive started before SetAccount switches to another profile still commits its messages afterwards
    // Broiler-Human:        PENDING
    public void SetAccount(AccountProfile? account)
    {
        if (_disposed || _account == account) return;
        StopPending();
        _account = account;
        Messages = [];
        SelectedMessage = null;
        Body = null;
        _older = null;
        HasLoaded = false;
        ClearProblem();
        _statusScope = InboxProblemScope.None;
        Status = account is { IsEnabled: true }
            ? $"Ready to receive mail for {account.EmailAddress} using the saved profile."
            : "Save and enable an account in the Account tab to receive mail.";
        Notify();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=6EFC99
    // Broiler-Falsified-If: ReceiveAsync starts a fetch while CanReceive is false, for example for a disabled account
    // Broiler-Human:        PENDING
    public Task ReceiveAsync() => CanReceive ? LoadPageAsync(null) : Task.CompletedTask;
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=68C2B0
    // Broiler-Falsified-If: LoadOlderAsync starts a fetch when no older cursor exists or after MaximumLoadedMessages summaries are loaded
    // Broiler-Human:        PENDING
    public Task LoadOlderAsync() => CanLoadOlder ? LoadPageAsync(_older) : Task.CompletedTask;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=7011AE
    // Broiler-Falsified-If: a page that repeats a message key already loaded leaves two entries with that key in Messages
    // Broiler-Human:        PENDING
    private Task LoadPageAsync(MailInboxCursor? cursor)
    {
        var account = _account!;
        _loadingPage = true;
        _lastPageWasOlder = cursor is not null;
        return RunAsync(token => receiver.GetInboxAsync(account, PageSize, cursor, token), page =>
        {
            HasLoaded = true;
            Messages = (cursor is null ? page.Messages : Messages.Concat(page.Messages))
                .DistinctBy(message => message.Key).ToArray();
            _older = page.Older;
            string? reading = cursor is null ? KeepReading(page) : null;
            Status = Messages.Count == 0 ? "The inbox is empty." : SelectedMessage is null
                ? $"{Messages.Count} messages loaded. Select one to read. Reading does not mark messages as read on the server."
                : $"{Messages.Count} messages loaded. Reading does not mark messages as read on the server.";
            if (reading is not null) Status += " " + reading;
            if (Messages.Count >= MaximumLoadedMessages && _older is not null)
                Status += " Session limit reached (500 messages). Receive mail again to return to the newest page.";
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
            return "The server renumbered the inbox, so the open message was closed. Select it again to read it.";
        }
        // Newest pages are contiguous: every message from the oldest fetched UID upward was returned.
        if (fetched.Length == 0 || page.Older is null || open.Key.Uid >= fetched.Min(message => message.Key.Uid))
        {
            SelectedMessage = null;
            Body = null;
            return "The open message is no longer in the inbox. It may have been moved or deleted on the server.";
        }
        return "The open message is older than the newest page and stays open. Use Load older to show it in the list.";
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=89C93F
    // Broiler-Falsified-If: a body whose Key differs from the requested key is committed to Body
    // Broiler-Human:        PENDING
    public Task SelectAsync(MailMessageKey key)
    {
        if (!CanSelect || _account is null) return Task.CompletedTask;
        // The open message may be outside the refreshed page; it can still be read again.
        var message = Messages.FirstOrDefault(item => item.Key == key) ?? (SelectedMessage?.Key == key ? SelectedMessage : null);
        if (message is null) return Task.CompletedTask;
        var account = _account;
        SelectedMessage = message;
        Body = null;
        return RunAsync(async token =>
        {
            var body = await receiver.GetBodyAsync(account, key, token).ConfigureAwait(false);
            if (body.Key != key) throw new InvalidOperationException("The receiver returned a different message.");
            return body;
        }, body =>
        {
            Body = body;
            Status = body.IsHtmlFallback ? "Reading text extracted from HTML. External resources are not loaded." : "Reading plain text. This does not mark the message as read on the server.";
            if (body.IsTruncated) Status += " Preview limited to 32,000 characters.";
        }, "Loading message…", InboxProblemScope.Message);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=5ABAE9
    // Broiler-Falsified-If: a result that completes after Cancel still replaces Messages or Body
    // Broiler-Human:        PENDING
    public void Cancel()
    {
        if (!IsBusy || _disposed) return;
        var scope = _running;
        StopPending();
        Status = "Mail operation canceled. You can retry.";
        SetProblem(scope, scope == InboxProblemScope.Message ? "Loading the message was canceled."
            : _lastPageWasOlder ? "Loading older messages was canceled." : "Receiving was canceled.", canceled: true);
        Notify();
    }

    /// <summary>Repeats the operation that failed or was canceled last: the same page, or the selected message.</summary>
    public Task RetryAsync() => RetryAsync(ProblemScope);

    /// <summary>Repeats the operation of the problem of <paramref name="scope"/>: the same page, or the selected message.</summary>
    public Task RetryAsync(InboxProblemScope scope)
    {
        if (!CanRetryIn(scope)) return Task.CompletedTask;
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

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=7; Fingerprint=687E68
    // Broiler-Falsified-If: a callback from an operation superseded by a later RunAsync or StopPending still commits its result on the dispatcher
    // Broiler-Human:        PENDING
    private async Task RunAsync<T>(Func<CancellationToken, Task<T>> operation, Action<T> commit, string busy, InboxProblemScope scope)
    {
        // A newer selection invalidates callbacks even if a receiver completes after cancellation.
        try { _operation?.Cancel(); } catch (ObjectDisposedException) { }
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        int generation = ++_generation;
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
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { failure = "Mail could not be loaded. Check the saved account and connection, then retry."; }
        try
        {
            dispatcher.Post(() =>
            {
                if (_disposed || generation != _generation) return;
                _operation = null;
                IsBusy = _loadingPage = false;
                if (failure is null) commit(result!);
                else
                {
                    Status = failure + (Messages.Count > 0 ? " The previously loaded inbox is still shown." : "");
                    SetProblem(scope, scope == InboxProblemScope.List && Messages.Count > 0 ? failure + " The messages below are from the last successful receive." : failure,
                        canceled: cancellation.IsCancellationRequested);
                }
                Notify();
            });
        }
        catch (ObjectDisposedException) { /* The native window has closed. */ }
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=1; Fingerprint=571C94
    // Broiler-Falsified-If: StopPending does not advance the generation, so a commit already queued on the dispatcher still runs
    // Broiler-Human:        PENDING
    private void StopPending()
    {
        ++_generation;
        // A dispatcher callback may be queued after the worker disposed its source.
        try { _operation?.Cancel(); } catch (ObjectDisposedException) { }
        _operation = null;
        IsBusy = _loadingPage = false;
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
        StopPending();
    }
}
