using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Owns the in-memory inbox. All state changes are published on the UI dispatcher.</summary>
public sealed class InboxViewModel(IMailReceiver receiver, IUiDispatcher dispatcher) : IDisposable
{
    public const int PageSize = 50;
    public const int MaximumLoadedMessages = 500;
    private AccountProfile? _account;
    private MailInboxCursor? _older;
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _disposed;
    private bool _loadingPage;

    public event EventHandler? Changed;
    public IReadOnlyList<MailMessageSummary> Messages { get; private set; } = [];
    public MailMessageSummary? SelectedMessage { get; private set; }
    public MailMessageBody? Body { get; private set; }
    public string Status { get; private set; } = "Save an account in the Account tab to receive mail.";
    public bool IsBusy { get; private set; }
    public bool CanReceive => !_disposed && !IsBusy && _account is { IsEnabled: true };
    public bool CanLoadOlder => CanReceive && _older is not null && Messages.Count < MaximumLoadedMessages;
    public bool CanSelect => !_disposed && !_loadingPage;

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

    public Task ReceiveAsync() => CanReceive ? LoadPageAsync(null) : Task.CompletedTask;
    public Task LoadOlderAsync() => CanLoadOlder ? LoadPageAsync(_older) : Task.CompletedTask;

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

    public void Cancel()
    {
        if (!IsBusy || _disposed) return;
        StopPending();
        Status = "Mail operation canceled. You can retry.";
        Notify();
    }

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

    private void StopPending()
    {
        ++_generation;
        // A dispatcher callback may be queued after the worker disposed its source.
        try { _operation?.Cancel(); } catch (ObjectDisposedException) { }
        _operation = null;
        IsBusy = _loadingPage = false;
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPending();
    }
}
