// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        5/5
// Exempt:           2
// Human-reviewed:   0/5
// IP risk:          Low
// Security risk:    Medium
// Criteria:         4/0
// Resource impact:  7/10 max
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.UI.Forms;
using Broiler.UI;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Validation;

namespace Broiler.Mail.Application.ViewModels;

/// <summary>Publishes save results on the UI thread and keeps failures visible to the user.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=A6ED99
// Broiler-Falsified-If: a save runs while loadError is set, overwriting a configuration file that failed to load
// Broiler-Human:        PENDING
public abstract class SaveViewModel(IUiDispatcher dispatcher, string? loadError)
{
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=8C487F
    // Broiler-Human:        PENDING
    public event EventHandler? Changed;
    public bool IsBusy { get; private set; }
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=0E18E7
    // Broiler-Falsified-If: CanSave is true while loadError is set
    // Broiler-Human:        PENDING
    public bool CanSave => !IsBusy && loadError is null;
    public string Status { get; private set; } = loadError ?? string.Empty;
    public FeedbackKind StatusKind { get; private set; } = loadError is null ? FeedbackKind.Information : FeedbackKind.Error;
    public string? ValidationField { get; private set; }
    public string? ValidationMessage { get; private set; }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=80FC04
    // Broiler-Falsified-If: a save whose operation throws still runs its commit action, so the view model shows values that never reached the store
    // Broiler-Human:        PENDING
    protected Task SaveAsync(Func<Task> save, Action commit, string successMessage) =>
        RunAsync(save, commit, "Saving…", successMessage, "Not saved", "Save canceled.");

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=3B4A10
    // Broiler-Falsified-If: a second call made while the first operation is still running starts its operation instead of returning
    // Broiler-Human:        PENDING
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
