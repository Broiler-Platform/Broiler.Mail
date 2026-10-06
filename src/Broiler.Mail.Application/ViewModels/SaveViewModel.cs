// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   11
// Annotated:        5/11
// Exempt:           9
// Human-reviewed:   0/11
// IP risk:          Low
// Security risk:    Medium
// Criteria:         4/0
// Resource impact:  7/10 max
// Unverified:       11
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
    /// <summary>A file that failed to load is never overwritten, not even by background saves.</summary>
    protected bool HasLoadError => loadError is not null;

    /// <summary>Publishes a state change that did not come from a save operation.</summary>
    protected void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs <paramref name="action"/> on the UI dispatcher; ignored once the window has closed.</summary>
    protected void Post(Action action)
    {
        try { dispatcher.Post(action); }
        catch (ObjectDisposedException) { }
    }
    public string Status { get; private set; } = loadError ?? string.Empty;
    public FeedbackKind StatusKind { get; private set; } = loadError is null ? FeedbackKind.Information : FeedbackKind.Error;
    public string? ValidationField { get; private set; }
    public string? ValidationMessage { get; private set; }
    /// <summary>The status without its details, for places that point to them, such as the shell footer.</summary>
    public string StatusSummary { get; private set; } = loadError is null ? string.Empty : "The saved data could not be loaded.";

    /// <summary>
    /// How long a success confirmation stays. What was saved stays visible elsewhere (the applied
    /// settings, the setup checklist), so the confirmation may go; failures, cancellations, and
    /// progress stay until the next operation replaces them.
    /// </summary>
    public static readonly TimeSpan SuccessDisplayTime = TimeSpan.FromSeconds(6);

    /// <summary>The clock that times success confirmations.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    private ITimer? _successTimer;
    private int _statusVersion;

    private void SetStatus(string status, FeedbackKind kind, string? summary = null)
    {
        Status = status;
        StatusKind = kind;
        StatusSummary = summary ?? status;
        int version = ++_statusVersion;
        _successTimer?.Dispose();
        _successTimer = kind != FeedbackKind.Success ? null : Clock.CreateTimer(_ => Post(() =>
        {
            // A newer status, even an identical one, has its own timer.
            if (version != _statusVersion) return;
            SetStatus("", FeedbackKind.Information);
            Changed?.Invoke(this, EventArgs.Empty);
        }), null, SuccessDisplayTime, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Reports an operation refused before it began, such as a test while edits are unsaved. Nothing ran, so
    /// nothing failed: the status says what to do first, an invalid field is marked, and earlier results stand.
    /// </summary>
    protected void Refuse(Exception refusal, string summary)
    {
        // Cleared first, as an operation clears it when it starts, so a repeated refusal is announced again
        // and its field can take focus again.
        SetStatus("", FeedbackKind.Information);
        ValidationField = ValidationMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);
        SetStatus(refusal.Message, FeedbackKind.Error, summary);
        var validation = refusal as ConfigurationValidationException;
        ValidationField = validation?.Field;
        ValidationMessage = validation?.Message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=80FC04
    // Broiler-Falsified-If: a save whose operation throws still runs its commit action, so the view model shows values that never reached the store
    // Broiler-Human:        PENDING
    protected Task SaveAsync(Func<Task> save, Action commit, string successMessage) =>
        RunAsync(save, commit, "Saving…", successMessage, "Not saved", "Save canceled.");

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=7; Fingerprint=21F7C0
    // Broiler-Falsified-If: a second call made while the first operation is still running starts its operation instead of returning
    // Broiler-Human:        PENDING
    protected async Task RunAsync(Func<Task> operation, Action commit, string busyMessage,
        string successMessage, string failurePrefix, string canceledMessage, Action<string?>? completed = null)
    {
        if (!CanSave)
            return;
        IsBusy = true;
        SetStatus(busyMessage, FeedbackKind.Progress);
        ValidationField = ValidationMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);

        string status, summary;
        bool succeeded = false;
        string? validationField = null, validationMessage = null;
        var kind = FeedbackKind.Success;
        try
        {
            await operation().ConfigureAwait(false);
            status = summary = successMessage;
            succeeded = true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or MailConnectionException)
        {
            status = $"{failurePrefix}: {error.Message}";
            summary = failurePrefix + ".";
            kind = FeedbackKind.Error;
            if (error is ConfigurationValidationException validation)
            { validationField = validation.Field; validationMessage = validation.Message; }
        }
        catch (OperationCanceledException)
        {
            status = summary = canceledMessage;
            kind = FeedbackKind.Information;
        }

        try
        {
            dispatcher.Post(() =>
            {
                if (succeeded)
                    commit();
                // The failure text, or null after success, lets callers record what went wrong.
                completed?.Invoke(succeeded ? null : status);
                IsBusy = false;
                SetStatus(status, kind, summary);
                ValidationField = validationField;
                ValidationMessage = validationMessage;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (ObjectDisposedException) { completed?.Invoke(succeeded ? null : status); }
    }
}
