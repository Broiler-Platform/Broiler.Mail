using Broiler.UI;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Publishes save results on the UI thread and keeps failures visible to the user.</summary>
public abstract class SaveViewModel(IUiDispatcher dispatcher, string? loadError)
{
    public event EventHandler? Changed;
    public bool IsBusy { get; private set; }
    public bool CanSave => !IsBusy && loadError is null;
    public string Status { get; private set; } = loadError ?? string.Empty;

    protected Task SaveAsync(Func<Task> save, Action commit, string successMessage) =>
        RunAsync(save, commit, "Saving…", successMessage, "Not saved", "Save canceled.");

    protected async Task RunAsync(Func<Task> operation, Action commit, string busyMessage,
        string successMessage, string failurePrefix, string canceledMessage, Action? completed = null)
    {
        if (!CanSave)
            return;
        IsBusy = true;
        Status = busyMessage;
        Changed?.Invoke(this, EventArgs.Empty);

        string status;
        bool succeeded = false;
        try
        {
            await operation().ConfigureAwait(false);
            status = successMessage;
            succeeded = true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or MailConnectionException)
        {
            status = $"{failurePrefix}: {error.Message}";
        }
        catch (OperationCanceledException)
        {
            status = canceledMessage;
        }

        try
        {
            dispatcher.Post(() =>
            {
                if (succeeded)
                    commit();
                completed?.Invoke();
                IsBusy = false;
                Status = status;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (ObjectDisposedException) { completed?.Invoke(); }
    }
}
