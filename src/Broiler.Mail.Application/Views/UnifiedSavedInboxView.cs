using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Optional local multi-account navigation without changing the active account or pinned draft.</summary>
public sealed class UnifiedSavedInboxView : UiElement
{
    private readonly IMailCache? _cache;
    private readonly StandardPanel _layout = new() { Spacing = 8 }, _details = new() { Spacing = 4 };
    private readonly StandardButton _refresh = new() { Text = "Refresh unified saved inbox" }, _read = new() { Text = "Read selected saved text" }, _cancel = new() { Text = "Cancel unified operation" };
    private readonly StandardComboBox _picker = new() { AccessibleName = "Unified saved INBOX messages with account attribution" };
    private readonly InlineFeedback _feedback = new();
    private AccountProfile[] _accounts = []; private bool _active; private int _generation; private CancellationTokenSource? _operation;
    private IReadOnlyList<UnifiedSavedInboxRow> _rows = [];
    public IReadOnlyList<UnifiedSavedInboxRow> Rows => _rows;
    public string Status => _feedback.Message;
    public string PreviewText { get; private set; } = "";
    public bool IsBusy => _operation is not null;
    public UnifiedSavedInboxView(IMailCache? cache)
    {
        _cache = cache; AddChild(new BoundedScrollArea(_layout, 1, "Unified saved inbox"));
        ConfigurationForm.AddText(_layout, "Optional offline view of configured accounts, including disabled accounts. Refresh explicitly: up to 50 newest saved INBOX UIDs per account (32 accounts/1600 candidates), sorted by received date, then account/UID. This is a bounded saved-header view, not a complete or synchronized server inbox. Every row and reading preview identifies its account. Choose a row, then Read selected saved text. No receiving, flag changes or sending. Use the account's ordinary inbox for HTML and mail actions.");
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        actions.AddChild(_refresh); actions.AddChild(_read); actions.AddChild(_cancel); _layout.AddChild(actions);
        _layout.AddChild(_picker); _layout.AddChild(_feedback); _layout.AddChild(_details);
        _refresh.Clicked += async (_, _) => await RefreshAsync(); _read.Clicked += async (_, _) => { if (Selected() is {} row) await ReadAsync(row); };
        _cancel.Clicked += (_, _) => Cancel();
        _picker.SelectionChanged += (_, _) => { ClearPreview(); if (IsBusy) { ++_generation; _operation?.Cancel(); } UpdateControls(); };
        _feedback.Set(cache is null ? "Unified saved inbox is unavailable in this host." : "Configure accounts, then open and Refresh explicitly. No account is read automatically.", FeedbackKind.Information); UpdateControls();
    }
    public void SetAccounts(IReadOnlyList<AccountProfile> accounts)
    {
        if (IsDisposed || _accounts.SequenceEqual(accounts)) return; _accounts = accounts.ToArray(); Reset("Configured accounts changed. Refresh saved mail explicitly.");
    }
    public void SetActive(bool active)
    { if (IsDisposed || _active == active) return; _active = active; if (!active) Reset("Unified inbox closed. Refresh explicitly when reopened."); else UpdateControls(); }
    private UnifiedSavedInboxRow? Selected() => _picker.SelectedIndex >= 0 && _picker.SelectedIndex < _rows.Count ? _rows[_picker.SelectedIndex] : null;
    private void ClearPreview()
    { PreviewText = ""; foreach (var child in _details.Children.ToArray()) { _details.RemoveChild(child); child.Dispose(); } }
    private void Reset(string message)
    {
        ++_generation; _operation?.Cancel(); _operation = null; _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; ClearPreview();
        _feedback.Set(_cache is null ? "Unified saved inbox is unavailable in this host." : message, FeedbackKind.Information); UpdateControls(); RestoreFocus();
    }
    private void RestoreFocus()
    { if (_active && Session is {} session && session.FocusedElement == _cancel && _refresh.CanFocus) FocusNavigation.FocusAndReveal(session, _rows.Count > 0 ? _picker : _refresh); }
    public void Cancel() { if (!IsDisposed) _operation?.Cancel(); }
    public Task RefreshAsync() => RunAsync(null);
    public Task ReadAsync(UnifiedSavedInboxRow row) => RunAsync(row);
    private async Task RunAsync(UnifiedSavedInboxRow? selected)
    {
        if (IsDisposed || IsBusy || !_active || _cache is null || _accounts.Length == 0 || selected is not null && !_rows.Contains(selected)) return;
        if (selected is not null) _picker.SelectedIndex = _rows.ToList().FindIndex(row => row == selected);
        ClearPreview(); var accounts = _accounts; int generation = ++_generation;
        if (selected is null) { _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; }
        var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set(selected is null ? "Reading saved INBOX headers across configured accounts…" : "Reading the selected account's cached text…", FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session, _cancel);
        UnifiedSavedInboxSnapshot? snapshot = null; Broiler.Mail.Core.Messages.MailMessageBody? body = null; string? error = null;
        try
        {
            if (selected is null) snapshot = await UnifiedSavedInbox.ReadAsync(_cache, accounts, token).ConfigureAwait(false);
            else body = await UnifiedSavedInbox.ReadBodyAsync(_cache, selected, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { error = "Unified saved-mail operation canceled or timed out. Refresh or read explicitly to retry."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "Unified saved mail could not be read. " + failure.GetType().Name; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation, cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _operation = null; cancellation.Dispose();
            if (generation != _generation || !_active || !ReferenceEquals(accounts, _accounts))
            { ClearPreview(); UpdateControls(); RestoreFocus(); return; }
            if (error is not null || canceled) _feedback.Set(error ?? "Unified operation canceled.", FeedbackKind.Information);
            else if (snapshot is not null)
            {
                _rows = snapshot.Rows; _picker.SetItems(_rows.Select(row => new UiComboBoxItem(RowId(row),
                    $"{OneLine(row.Account.DisplayName, 50)} <{OneLine(row.Account.EmailAddress, 80)}> [{row.Account.Id}] · {row.Message.ReceivedAt:u} · {OneLine(row.Message.Sender, 80)} · {OneLine(row.Message.Subject, 160)}"))); _picker.SelectedIndex = -1;
                string failures = string.Join("; ", snapshot.Failures.Select(row => $"{OneLine(row.Account.DisplayName, 50)} [{row.Account.Id}]: {row.Code}"));
                _feedback.Set($"{_rows.Count} saved headers across {snapshot.Accounts} configured accounts. At most 50 newest saved INBOX UIDs per account; no bodies were downloaded." + (failures.Length == 0 ? "" : " Accounts unavailable: " + failures), FeedbackKind.Information);
            }
            else if (selected is not null && Selected() == selected)
            {
                if (body is null) _feedback.Set($"{selected.Account.DisplayName} <{selected.Account.EmailAddress}> [{selected.Account.Id}]: this body is not downloaded. Use its ordinary account inbox to receive it explicitly.", FeedbackKind.Information);
                else
                {
                    PreviewText = $"{selected.Account.DisplayName} <{selected.Account.EmailAddress}>\nAccount {selected.Account.Id}; INBOX; UIDVALIDITY {body.Key.UidValidity}; UID {body.Key.Uid}\n" +
                        $"Saved text preview; {(body.IsTruncated ? "truncated" : "supported decoded body")}{(body.IsHtmlFallback ? "; extracted from HTML" : "")}. No HTML or remote resources are opened here.\n\n" + body.PlainText;
                    var preview = new ScrollableMessageText { Text = PreviewText }; preview.Editor.AccessibleName = "Unified saved text with account and message identity";
                    var frame = new BoundedScrollArea(preview, 0.5, "Unified saved message text"); _details.AddChild(frame);
                    if (Session is not null) StandardThemeController.ApplyToSubtree(frame, StandardControlPaint.GetTheme(this));
                    _feedback.Set("Saved text read locally for the attributed account. The active account and any pinned draft remain unchanged.", FeedbackKind.Success);
                }
            }
            UpdateControls(); RestoreFocus();
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { if (ReferenceEquals(_operation, cancellation)) _operation = null; cancellation.Dispose(); }
    }
    private static string OneLine(string value, int limit) => new(value.Take(limit).Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
    private static string RowId(UnifiedSavedInboxRow row) => $"{row.Account.Id}:{row.Message.Key.UidValidity}:{row.Message.Key.Uid}";
    private void UpdateControls()
    {
        if (IsDisposed) return; bool available = _active && _cache is not null && _accounts.Length > 0 && !IsBusy;
        _refresh.IsEnabled = available; _picker.IsEnabled = available && _rows.Count > 0; _read.IsEnabled = available && Selected() is not null; _cancel.IsEnabled = IsBusy;
    }
    protected override BSize MeasureCore(BSize size) => Children[0].Measure(size);
    protected override void ArrangeCore(BRect rect) => Children[0].Arrange(rect);
    protected override void Dispose(bool disposing) { if (disposing) { ++_generation; _operation?.Cancel(); _operation = null; _rows = []; ClearPreview(); } base.Dispose(disposing); }
}
