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

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=8C487F
    // Broiler-Human:        PENDING
    public event EventHandler? Changed;
    public IReadOnlyList<MailMessageSummary> Messages { get; private set; } = [];
    public MailMessageSummary? SelectedMessage { get; private set; }
    public MailMessageBody? Body { get; private set; }
    public string Status { get; private set; } = "Save an account in the Account tab to receive mail.";
    public bool IsBusy { get; private set; }
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
        return RunAsync(token => receiver.GetInboxAsync(account, PageSize, cursor, token), page =>
        {
            Messages = (cursor is null ? page.Messages : Messages.Concat(page.Messages))
                .DistinctBy(message => message.Key).ToArray();
            _older = page.Older;
            if (cursor is null)
            {
                SelectedMessage = null;
                Body = null;
            }
            Status = Messages.Count == 0 ? "The inbox is empty." :
                $"{Messages.Count} messages loaded. Select one to read. Server read/unread flags are unchanged.";
            if (Messages.Count >= MaximumLoadedMessages && _older is not null)
                Status += " Session limit reached (500 messages). Receive mail again to return to the newest page.";
        }, cursor is null ? "Receiving newest messages…" : "Loading older messages…");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=89C93F
    // Broiler-Falsified-If: a body whose Key differs from the requested key is committed to Body
    // Broiler-Human:        PENDING
    public Task SelectAsync(MailMessageKey key)
    {
        if (!CanSelect || _account is null) return Task.CompletedTask;
        var message = Messages.FirstOrDefault(item => item.Key == key);
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
            Status = body.IsHtmlFallback ? "Reading text extracted from HTML. External resources are not loaded." : "Reading plain text. Server flags are unchanged.";
            if (body.IsTruncated) Status += " Preview limited to 32,000 characters.";
        }, "Loading message…");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=3; Fingerprint=5ABAE9
    // Broiler-Falsified-If: a result that completes after Cancel still replaces Messages or Body
    // Broiler-Human:        PENDING
    public void Cancel()
    {
        if (!IsBusy || _disposed) return;
        StopPending();
        Status = "Mail operation canceled. You can retry.";
        Notify();
    }

    // Broiler-AI:           Origin=AI; Spec=ADR-0003; IP=Low; Security=Medium; Resources=7; Fingerprint=687E68
    // Broiler-Falsified-If: a callback from an operation superseded by a later RunAsync or StopPending still commits its result on the dispatcher
    // Broiler-Human:        PENDING
    private async Task RunAsync<T>(Func<CancellationToken, Task<T>> operation, Action<T> commit, string busy)
    {
        // A newer selection invalidates callbacks even if a receiver completes after cancellation.
        try { _operation?.Cancel(); } catch (ObjectDisposedException) { }
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        int generation = ++_generation;
        IsBusy = true;
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
                else Status = failure + (Messages.Count > 0 ? " The previously loaded inbox is still shown." : "");
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
