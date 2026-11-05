using Broiler.UI;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Local opt-in suggestions; late loads never cross account boundaries.</summary>
public sealed class RecipientBookViewModel(IRecipientBook store, IUiDispatcher dispatcher) : IDisposable
{
    private AccountId? _account;
    private CancellationTokenSource _context = new();
    private long _generation;
    private bool _disposed;
    public event EventHandler? Changed;
    public IReadOnlyList<SavedRecipient> Recipients { get; private set; } = [];
    public bool IsBusy { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsError { get; private set; }
    public string Status { get; private set; } = "Save recipients explicitly. Addresses are never collected from mail or Bcc.";
    public Task LoadTask { get; private set; } = Task.CompletedTask;
    public void SetAccount(AccountId? account)
    {
        if (_disposed || _account == account) return;
        _context.Cancel(); _context.Dispose(); _context = new(); _generation++;
        _account = account; Recipients = []; IsReady = false; IsBusy = false; IsError = false;
        Status = "Saved recipients belong to the draft's sender account. Nothing is collected automatically.";
        if (account is not null) LoadTask = ReloadAsync(); else Changed?.Invoke(this, EventArgs.Empty);
    }
    public IReadOnlyList<SavedRecipient> Suggest(string prefix)
    {
        if (!IsReady || prefix is null || prefix.Length > 320 || prefix.Any(char.IsControl)) return [];
        prefix = prefix.Trim();
        return Array.AsReadOnly(Recipients.Where(row => row.Address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || row.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(10).ToArray());
    }
    public Task ReloadAsync() => RunAsync(null, "Loading saved recipients…");
    public Task SaveAsync(string address, string name) => RunAsync((account, token) => store.SaveAsync(SavedRecipient.Create(account, address, name), token), "Saving recipient…");
    public Task RemoveAsync(string address) => RunAsync((account, token) => store.RemoveAsync(account, address, token), "Removing saved recipient…");
    private async Task RunAsync(Func<AccountId, CancellationToken, Task>? change, string busy)
    {
        if (_disposed || IsBusy || _account is not { } account || change is not null && !IsReady) return;
        long generation = _generation; var token = _context.Token;
        IsBusy = true; IsError = false; Status = busy; Changed?.Invoke(this, EventArgs.Empty);
        IReadOnlyList<SavedRecipient>? rows = null; string? error = null;
        try
        {
            if (change is not null) await change(account, token).ConfigureAwait(false);
            rows = await store.LoadAsync(account, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = failure.Message; }
        if (_disposed || generation != _generation) return;
        try
        {
            dispatcher.Post(() =>
            {
                if (_disposed || generation != _generation) return;
                IsBusy = false; IsError = error is not null;
                if (rows is not null) { Recipients = rows; IsReady = true; }
                Status = error is null ? $"{Recipients.Count} saved recipients for this sender account. Showing at most 10 prefix matches; choose explicitly. Nothing is sent." : "Saved recipients could not be updated: " + error;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (ObjectDisposedException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _context.Cancel(); _context.Dispose(); }
}
