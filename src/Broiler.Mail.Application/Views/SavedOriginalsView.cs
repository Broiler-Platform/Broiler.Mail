using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Read-only account-scoped catalog browsing independent of inbox/body availability.</summary>
public sealed class SavedOriginalsView : UiElement
{
    private readonly IAccountSourceExportCatalog? _catalog;
    private readonly IVerifiedMessageSourceExports? _reader;
    private readonly IMessageAttachmentExports? _attachments;
    private readonly StandardPanel _layout = new() { Spacing = 8 }, _details = new() { Spacing = 4 };
    private readonly StandardButton _list = new() { Text = "List account saved originals" }, _use = new() { Text = "Verify selected original" }, _cancel = new() { Text = "Cancel saved-original operation" };
    private readonly StandardComboBox _picker = new() { AccessibleName = "Saved originals for current account" };
    private readonly InlineFeedback _feedback = new();
    private readonly StandardRichEdit _identity = new() { IsReadOnly = true, AccessibleName = "Saved-original account identity path and checksum", Wrapping = RichEditWrapping.Wrap, VerticalScrollPolicy = RichEditScrollPolicy.Never, HorizontalScrollPolicy = RichEditScrollPolicy.Never };
    private AccountProfile? _account; private bool _active; private int _generation; private CancellationTokenSource? _operation;
    private IReadOnlyList<SavedMessageSourceExport> _rows = [];
    public IReadOnlyList<SavedMessageSourceExport> Exports => _rows;
    public LocalOriginalsView LocalImports { get; }
    public bool IsBusy => _operation is not null;
    public string Status => _feedback.Message;
    public SavedOriginalsView(IMessageSourceExports? exports, ILocalOriginalHtmlHost? htmlHost = null)
    {
        LocalImports = new(exports as ILocalMessageOriginals, htmlHost);
        _catalog = exports as IAccountSourceExportCatalog; _reader = exports as IVerifiedMessageSourceExports; _attachments = exports as IMessageAttachmentExports;
        var frame = new BoundedScrollArea(_layout,1,"Saved originals library"); AddChild(frame);
        ConfigurationForm.AddText(_layout,"Saved originals for the selected account, including disabled accounts. This library works without an inbox body or cached headers. List explicitly, choose a receipt, then Verify. Preview source and save attachments separately. No receiving, sending or automatic file opening. Local file opening/import has separate controls below. Up to 64 receipted originals across the profile; export files/catalog require separate backups. Uncataloged files are kept but not guessed.");
        var toolbar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 }; toolbar.AddChild(_list); toolbar.AddChild(_use); toolbar.AddChild(_cancel);
        _layout.AddChild(toolbar); _layout.AddChild(_picker); _layout.AddChild(_feedback); _layout.AddChild(_identity); _layout.AddChild(_details); _layout.AddChild(LocalImports);
        _list.Clicked += async (_,_) => await ListAsync(); _use.Clicked += async (_,_) => { if (Selected() is {} row) await VerifyAsync(row); };
        _cancel.Clicked += (_,_) => Cancel(); _picker.SelectionChanged += (_,_) => { if (!IsBusy) { ClearDetails(); _feedback.Set("Selection changed. Verify the selected original explicitly; no original is active.",FeedbackKind.Information); UpdateControls(); } }; UpdateControls();
        _feedback.Set(Guidance("Select an account and open Saved originals, then List explicitly. No inventory is loaded automatically."),FeedbackKind.Information);
    }
    private string Guidance(string message) => _catalog is null || _reader is null ? "Saved-original library is unavailable in this host." : _account is null ? "Select a configured account, then List saved originals explicitly." : message;
    public void SetAccount(AccountProfile? account)
    {
        if (IsDisposed || _account == account) return; LocalImports.SetAccount(account); _account = account; Reset("Account changed. List its saved originals explicitly.");
    }
    public void SetActive(bool active)
    {
        if (IsDisposed || _active == active) return; LocalImports.SetActive(active); _active = active;
        if (!active) Reset("Saved originals closed. Reopen and List explicitly."); else UpdateControls();
    }
    private SavedMessageSourceExport? Selected() => _picker.SelectedIndex >= 0 && _picker.SelectedIndex < _rows.Count ? _rows[_picker.SelectedIndex] : null;
    private void ClearDetails()
    {
        foreach (var child in _details.Children.ToArray()) { _details.RemoveChild(child); child.Dispose(); }
        _identity.SetPlainText("");
    }
    private void Reset(string message)
    {
        ++_generation; _operation?.Cancel(); _operation = null; _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; ClearDetails();
        _feedback.Set(Guidance(message),FeedbackKind.Information); UpdateControls();
        if (Session is {} session && session.FocusedElement is {} focused && !focused.CanFocus && _list.CanFocus) FocusNavigation.FocusAndReveal(session,_list);
    }
    public void Cancel() { if (!IsDisposed) _operation?.Cancel(); }
    public Task ListAsync() => RunAsync(null);
    public Task VerifyAsync(SavedMessageSourceExport row) => RunAsync(row);
    private async Task RunAsync(SavedMessageSourceExport? selected)
    {
        if (IsDisposed || IsBusy || !_active || _account is not {} account || _catalog is null || _reader is null || selected is not null && !_rows.Contains(selected)) return;
        if (selected is not null) _picker.SelectedIndex = _rows.ToList().FindIndex(row => row == selected);
        ClearDetails(); int generation = ++_generation;
        if (selected is null) { _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; }
        var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set(selected is null ? "Reading local account receipts…" : "Verifying selected original bytes…",FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session,_cancel);
        IReadOnlyList<SavedMessageSourceExport>? rows = null; MessageSourceExport? verified = null; string? error = null;
        try
        {
            if (selected is null)
            {
                rows = await _catalog.ListAccountExportsAsync(account.Id,token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                if (rows.Count > 64 || rows.Any(row => row.Export.Key.AccountId != account.Id) || rows.Select(row => row.Export.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
                    throw new InvalidDataException("Account catalog returned mismatched or duplicate receipts.");
            }
            else
            {
                var source = await _reader.ReadVerifiedAsync(selected.Export,token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                if (source.Key != selected.Export.Key || source.Key.AccountId != account.Id || source.Bytes.Length != selected.Export.Bytes)
                    throw new InvalidDataException("Saved-original verification returned another identity or size.");
                verified = selected.Export;
            }
        }
        catch (OperationCanceledException) { error = "Saved-original operation canceled. No file was opened or downloaded."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "Saved-original operation did not finish. " + failure.Message; }
        if (IsDisposed) { cancellation.Dispose(); return; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation,cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _operation = null; cancellation.Dispose();
            if (generation == _generation && _active && _account == account)
            {
                if (error is null && !canceled && rows is not null)
                {
                    _rows = Array.AsReadOnly(rows.ToArray()); _picker.SetItems(_rows.Select(row => new UiComboBoxItem(row.Export.Path,
                        $"{row.SavedAt:O} · {row.Export.Key.MailboxId} · epoch {row.Export.Key.UidValidity}/UID {row.Export.Key.Uid} · {row.Export.Bytes} bytes"))); _picker.SelectedIndex = -1;
                    _feedback.Set($"{rows.Count} receipts for {account.DisplayName} <{account.EmailAddress}> ({account.Id}). Choose one and Verify; listing does not verify file bytes.",FeedbackKind.Information);
                }
                else if (error is null && !canceled && verified is not null && Selected() == selected)
                {
                    bool Current() => !IsDisposed && _active && generation == _generation && _account == account && Selected()?.Export == verified;
                    _identity.SetPlainText($"{account.DisplayName} <{account.EmailAddress}>\nAccount {account.Id}\nFolder {verified.Key.MailboxId}; UIDVALIDITY {verified.Key.UidValidity}; UID {verified.Key.Uid}\n{verified.Path}\n{verified.Bytes} bytes; SHA-256 {verified.Sha256}");
                    var inspection = new MessageSourceInspection(verified.Key,_reader,Current); inspection.SetExport(verified); _details.AddChild(inspection);
                    var attachments = new MessageAttachmentActions(verified.Key,_attachments,Current); attachments.SetExport(verified); _details.AddChild(attachments);
                    _feedback.Set("Original verified locally. Show source or read attachments explicitly; nothing was downloaded or opened.",FeedbackKind.Success);
                }
                else { _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; _feedback.Set(error ?? "Saved-original operation canceled. List again to retry.",FeedbackKind.Information); }
            }
            UpdateControls(); if (Session is {} current && current.FocusedElement == _cancel) FocusNavigation.FocusAndReveal(current,_rows.Count > 0 ? _picker : _list);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
    }
    private void UpdateControls()
    {
        if (IsDisposed) return; bool available = _active && _account is not null && _catalog is not null && _reader is not null;
        _list.IsEnabled = available && !IsBusy; _picker.IsEnabled = available && !IsBusy && _rows.Count > 0; _use.IsEnabled = available && !IsBusy && Selected() is not null; _cancel.IsEnabled = IsBusy;
    }
    protected override BSize MeasureCore(BSize size) => Children[0].Measure(size);
    protected override void ArrangeCore(BRect rect) => Children[0].Arrange(rect);
    protected override void Dispose(bool disposing) { if (disposing) { ++_generation; _operation?.Cancel(); _operation?.Dispose(); _operation = null; _rows = []; } base.Dispose(disposing); }
}
