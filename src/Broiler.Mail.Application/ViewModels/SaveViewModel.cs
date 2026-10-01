using Broiler.UI.Forms.Standard;
using Broiler.UI;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Publishes save results on the UI thread and keeps failures visible to the user.</summary>
public abstract class SaveViewModel(IUiDispatcher dispatcher, string? loadError)
{
    public event EventHandler? Changed;
    public bool IsBusy { get; private set; }
    public bool CanSave => !IsBusy && loadError is null;
    public string Status { get; private set; } = loadError ?? string.Empty;
    public FeedbackKind StatusKind { get; private set; } = loadError is null ? FeedbackKind.Information : FeedbackKind.Error;
    public string? ValidationField { get; private set; }
    public string? ValidationMessage { get; private set; }

    protected Task SaveAsync(Func<Task> save, Action commit, string successMessage) =>
        RunAsync(save, commit, "Saving…", successMessage, "Not saved", "Save canceled.");

    protected async Task RunAsync(Func<Task> operation, Action commit, string busyMessage,
        string successMessage, string failurePrefix, string canceledMessage, Action? completed = null)
    {
        if (!CanSave)
            return;
        IsBusy = true;
        Status = busyMessage;
        StatusKind = FeedbackKind.Progress;
        ValidationField = ValidationMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);

        string status;
        bool succeeded = false;
        string? validationField = null, validationMessage = null;
        var kind = FeedbackKind.Success;
        try
        {
            await operation().ConfigureAwait(false);
            status = successMessage;
            succeeded = true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or MailConnectionException)
        {
            status = $"{failurePrefix}: {error.Message}";
            kind = FeedbackKind.Error;
            if (error is ConfigurationValidationException validation)
            { validationField = validation.Field; validationMessage = validation.Message; }
        }
        catch (OperationCanceledException)
        {
            status = canceledMessage;
            kind = FeedbackKind.Information;
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
                StatusKind = kind;
                ValidationField = validationField;
                ValidationMessage = validationMessage;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (ObjectDisposedException) { completed?.Invoke(); }
    }
}
