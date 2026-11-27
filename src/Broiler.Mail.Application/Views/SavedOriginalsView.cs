// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   34
// Annotated:        0/34
// Exempt:           22
// Human-reviewed:   0/34
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       34
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Security.Cryptography;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Account-scoped catalog browsing independent of inbox/body availability. Removing a saved original asks first, with
/// Keep as the first choice. Save copy as .eml… opens a user-initiated file dialog: the documented exemption to the
/// no-modal-while-reading rule. A device whose picker cannot save files has no Save copy (there was no typed path for it).
/// </summary>
public sealed class SavedOriginalsView : UiElement
{
    public const string GuidanceText = "Saved originals for this account (up to 64 across the profile). List, choose one, then Verify. Remove ones you no longer need to free space.";
    public const string RemoveButtonText = "Remove saved original…";
    public const string SaveCopyButtonText = "Save copy as .eml…";
    public const string KeepButtonText = "Keep saved original";
    public const string DeleteButtonText = "Delete saved original";
    private readonly IAccountSourceExportCatalog? _catalog;
    private readonly IVerifiedMessageSourceExports? _reader;
    private readonly IMessageAttachmentExports? _attachments;
    private readonly ISourceExportCatalogMaintenance? _maintenance;
    private readonly StandardPanel _layout = new() { Spacing = 8 }, _details = new() { Spacing = 4 };
    private readonly StandardPanel _confirmation = new() { Spacing = 4, Visibility = UiVisibility.Collapsed, AccessibleName = "Confirm removing the saved original" };
    private readonly StandardButton _list = new() { Text = "List account saved originals" }, _use = new() { Text = "Verify selected original" }, _cancel = new() { Text = "Cancel saved-original operation" };
    private readonly StandardButton _saveCopy = new() { Text = SaveCopyButtonText, ToolTipText = "Save an exact copy of the verified original to a new .eml file. Nothing is replaced." },
        _remove = new() { Text = RemoveButtonText, ToolTipText = "Delete the selected saved original from this profile after you confirm." },
        _keep = new() { Text = KeepButtonText }, _delete = new() { Text = DeleteButtonText };
    private readonly StandardLabel _confirmationText;
    private readonly StandardComboBox _picker = new() { AccessibleName = "Saved originals for current account" };
    private readonly InlineFeedback _feedback = new();
    private readonly StandardRichEdit _identity = new() { IsReadOnly = true, AccessibleName = "Saved-original account identity path and checksum", Wrapping = RichEditWrapping.Wrap, VerticalScrollPolicy = RichEditScrollPolicy.Never, HorizontalScrollPolicy = RichEditScrollPolicy.Never };
    private AccountProfile? _account; private bool _active; private int _generation; private CancellationTokenSource? _operation;
    private IReadOnlyList<SavedMessageSourceExport> _rows = [];
    // The receipt whose file the last Verify checked; Save copy needs it to be the selected one.
    private MessageSourceExport? _checkedExport;
    private SavedMessageSourceExport? _pendingRemoval;
    public IReadOnlyList<SavedMessageSourceExport> Exports => _rows;
    public LocalOriginalsView LocalImports { get; }
    public bool IsBusy => _operation is not null;
    public string Status => _feedback.Message;
    /// <summary>A removal waits for Keep or Delete.</summary>
    public bool HasPendingRemoval => _pendingRemoval is not null;
    /// <summary>The confirmation's question, or empty when none is shown.</summary>
    public string ConfirmationText => _pendingRemoval is null ? "" : _confirmationText.Text;
    /// <summary>Chooses files for this view; an unavailable picker when none was given, so views keep their typed paths.</summary>
    internal IMailFilePicker Files { get; }
    private bool CanPickSave => Files.Capabilities.HasFlag(MailFilePickerCapabilities.SaveFile);
    public SavedOriginalsView(IMessageSourceExports? exports, ILocalOriginalHtmlHost? htmlHost = null, IMailFilePicker? files = null)
    {
        Files = files ?? UnavailableMailFilePicker.NotOnThisDeviceYet;
        LocalImports = new(exports as ILocalMessageOriginals, htmlHost, Files);
        _catalog = exports as IAccountSourceExportCatalog; _reader = exports as IVerifiedMessageSourceExports; _attachments = exports as IMessageAttachmentExports; _maintenance = exports as ISourceExportCatalogMaintenance;
        var frame = new BoundedScrollArea(_layout,1,"Saved originals library"); AddChild(frame);
        ConfigurationForm.AddText(_layout,GuidanceText);
        if (!CanPickSave) _saveCopy.ToolTipText = Files is UnavailableMailFilePicker none ? none.Reason : "Saving files is not available on this device yet.";
        if (_maintenance is null) _remove.Visibility = UiVisibility.Collapsed;
        var toolbar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 }; foreach (var button in new[] { _list, _use, _saveCopy, _remove, _cancel }) toolbar.AddChild(button);
        _confirmationText = ConfigurationForm.AddText(_confirmation,"");
        var choices = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 }; choices.AddChild(_keep); choices.AddChild(_delete); _confirmation.AddChild(choices);
        _layout.AddChild(toolbar); _layout.AddChild(_picker); _layout.AddChild(_confirmation); _layout.AddChild(_feedback); _layout.AddChild(_identity); _layout.AddChild(_details); _layout.AddChild(LocalImports);
        _list.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_,_) => await ListAsync()); _use.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_,_) => { if (Selected() is {} row) await VerifyAsync(row); });
        _saveCopy.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_,_) => { if (Selected() is {} row) await SaveCopyAsync(row); });
        _remove.Clicked += (_,_) => { if (Selected() is {} row) RequestRemoval(row); };
        _keep.Clicked += (_,_) => Keep(); _delete.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_,_) => await ConfirmRemovalAsync());
        _cancel.Clicked += (_,_) => Cancel(); _picker.SelectionChanged += (_,_) => { if (!IsBusy) { ClearDetails(); _pendingRemoval = null; _feedback.Set("Selection changed. Verify the selected original explicitly; no original is active.",FeedbackKind.Information); UpdateControls(); } }; UpdateControls();
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
        _identity.SetPlainText(""); _checkedExport = null;
    }
    private static UiComboBoxItem Item(SavedMessageSourceExport row) => new(row.Export.Path,
        $"{row.SavedAt:O} · {row.Export.Key.MailboxId} · epoch {row.Export.Key.UidValidity}/UID {row.Export.Key.Uid} · {row.Export.Bytes} bytes");
    private void Reset(string message)
    {
        ++_generation; _operation?.Cancel(); _operation = null; _pendingRemoval = null; _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; ClearDetails();
        _feedback.Set(Guidance(message),FeedbackKind.Information); UpdateControls();
        if (Session is {} session && session.FocusedElement is {} focused && !focused.CanFocus && _list.CanFocus) FocusNavigation.FocusAndReveal(session,_list);
    }
    public void Cancel() { if (!IsDisposed) _operation?.Cancel(); }
    public Task ListAsync() => RunAsync(null);
    public Task VerifyAsync(SavedMessageSourceExport row) => RunAsync(row);
    private async Task RunAsync(SavedMessageSourceExport? selected)
    {
        if (IsDisposed || IsBusy || !_active || _account is not {} account || _catalog is null || _reader is null || _pendingRemoval is not null || selected is not null && !_rows.Contains(selected)) return;
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
                    _rows = Array.AsReadOnly(rows.ToArray()); _picker.SetItems(_rows.Select(Item)); _picker.SelectedIndex = -1;
                    _feedback.Set($"{rows.Count} receipts for {account.DisplayName} <{account.EmailAddress}> ({account.Id}). Choose one and Verify; listing does not verify file bytes.",FeedbackKind.Information);
                }
                else if (error is null && !canceled && verified is not null && Selected() == selected)
                {
                    bool Current() => !IsDisposed && _active && generation == _generation && _account == account && Selected()?.Export == verified;
                    _identity.SetPlainText($"{account.DisplayName} <{account.EmailAddress}>\nAccount {account.Id}\nFolder {verified.Key.MailboxId}; UIDVALIDITY {verified.Key.UidValidity}; UID {verified.Key.Uid}\n{verified.Path}\n{verified.Bytes} bytes; SHA-256 {verified.Sha256}");
                    var inspection = new MessageSourceInspection(verified.Key,_reader,Current); inspection.SetExport(verified); _details.AddChild(inspection);
                    var attachments = new MessageAttachmentActions(verified.Key,_attachments,Current); attachments.SetExport(verified); _details.AddChild(attachments); _checkedExport = verified;
                    _feedback.Set("Original verified locally. Show source or read attachments explicitly; nothing was downloaded or opened.",FeedbackKind.Success);
                }
                else { _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; _feedback.Set(error ?? "Saved-original operation canceled. List again to retry.",FeedbackKind.Information); }
            }
            UpdateControls(); if (Session is {} current && current.FocusedElement == _cancel) FocusNavigation.FocusAndReveal(current,_rows.Count > 0 ? _picker : _list);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
    }
    /// <summary>Asks whether to delete <paramref name="row"/>; Keep has focus first and nothing is deleted yet.</summary>
    public void RequestRemoval(SavedMessageSourceExport row)
    {
        if (IsDisposed || IsBusy || !_active || _account is not {} account || _catalog is null || _reader is null || _maintenance is null || _pendingRemoval is not null ||
            !_rows.Contains(row) || row.Export.Key.AccountId != account.Id) return;
        int index = _rows.ToList().FindIndex(item => item == row); if (_picker.SelectedIndex != index) _picker.SelectedIndex = index;
        ClearDetails(); _pendingRemoval = row; _confirmationText.Text = RemovalQuestion(row);
        _feedback.Set("Keep is the first choice. Nothing is deleted until you choose Delete saved original.",FeedbackKind.Information); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session,_keep);
    }
    internal static string RemovalQuestion(SavedMessageSourceExport row) =>
        $"Delete the saved original from {row.SavedAt.ToLocalTime():g} ({MailByteSize.Format(row.Export.Bytes)}, {row.Export.Key.MailboxId} UID {row.Export.Key.Uid})? " +
        "This frees a slot in the 64-file saved-originals folder. Broiler Mail's file is deleted permanently; the message on the server and any copy you saved elsewhere are kept. Profile backups do not include saved originals.";
    /// <summary>Dismisses a pending removal; nothing is deleted.</summary>
    public void Keep()
    {
        if (IsDisposed || IsBusy || _pendingRemoval is null) return; _pendingRemoval = null;
        _feedback.Set("Saved original kept. Nothing was deleted.",FeedbackKind.Information); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session,_remove.CanFocus ? _remove : _rows.Count > 0 ? _picker : _list);
    }
    /// <summary>Escape inside Saved originals: a pending removal is kept. Returns false when there is none.</summary>
    internal bool HandleEscape() { if (IsDisposed || IsBusy || _pendingRemoval is null) return false; Keep(); return true; }
    /// <summary>Deletes the saved original the confirmation asks about: its file first, then its receipt.</summary>
    public async Task ConfirmRemovalAsync()
    {
        if (IsDisposed || IsBusy || !_active || _account is not {} account || _maintenance is null || _pendingRemoval is not {} row || !_rows.Contains(row)) return;
        int generation = ++_generation; var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set("Deleting the saved original…",FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session,_cancel);
        RemovedMessageSourceExport? removed = null; string? error = null;
        try { removed = await _maintenance.RemoveExportAsync(row,token).ConfigureAwait(false); }
        catch (OperationCanceledException) { error = "Removal canceled. Nothing was deleted."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "The saved original was not deleted. " + failure.Message; }
        if (IsDisposed) { cancellation.Dispose(); return; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation,cancellation)) { cancellation.Dispose(); return; }
            _operation = null; _pendingRemoval = null; cancellation.Dispose(); bool deleted = false;
            if (generation == _generation && _active && _account == account)
            {
                if (removed is not null)
                {
                    // The row goes locally; listing again is not needed.
                    _rows = Array.AsReadOnly(_rows.Where(item => item != row).ToArray()); _picker.SetItems(_rows.Select(Item)); _picker.SelectedIndex = -1; ClearDetails(); deleted = true;
                    int left = _rows.Count;
                    _feedback.Set("Saved original deleted" + (removed.FileWasMissing ? " (its file was already gone; only the receipt was removed)" : "") +
                        $". {left} saved original{(left == 1 ? " remains" : "s remain")} for this account.",FeedbackKind.Success);
                }
                else _feedback.Set(error ?? "Removal canceled. Nothing was deleted.",FeedbackKind.Warning);
            }
            UpdateControls();
            if (Session is {} current && (current.FocusedElement is null || current.FocusedElement == _cancel || !current.FocusedElement.CanFocus))
                FocusNavigation.FocusAndReveal(current,deleted ? (_rows.Count > 0 ? _picker : _list) : _remove.CanFocus ? _remove : _list);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
    }
    /// <summary>Asks for a new .eml file and writes an exact copy of the verified, selected original there.</summary>
    public async Task SaveCopyAsync(SavedMessageSourceExport row)
    {
        if (IsDisposed || IsBusy || !_active || _account is not {} account || _reader is null || _pendingRemoval is not null || !CanPickSave ||
            _checkedExport is not {} verified || Selected() != row || row.Export != verified) return;
        int generation = _generation;
        var pick = await Files.PickSaveFileAsync(new(MailFilePurpose.SaveMessage,"Save copy as .eml")
        { Types = [MailFileType.Email], SuggestedFileName = $"Saved original UID {row.Export.Key.Uid}.eml", AcceptText = "Save" });
        // A result that arrives after the selection or account changed is dropped; nothing was written yet.
        if (IsDisposed || generation != _generation || !_active || _account != account || IsBusy || _pendingRemoval is not null || Selected() != row || _checkedExport != verified) return;
        switch (pick.Status)
        {
            case MailFilePickStatus.Picked when pick.Item is {} target: await WriteCopyAsync(row,target,account,generation); return;
            case MailFilePickStatus.Rejected or MailFilePickStatus.Unavailable:
                _feedback.Set((pick.Message ?? "That file cannot be used.") + " Nothing was saved.",FeedbackKind.Warning); break;
            default: _feedback.Set("No file chosen. Nothing was saved.",FeedbackKind.Information); break;
        }
        if (Session is {} session && _saveCopy.CanFocus) FocusNavigation.FocusAndReveal(session,_saveCopy);
    }
    private async Task WriteCopyAsync(SavedMessageSourceExport row, IMailWritableFile target, AccountProfile account, int generation)
    {
        var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set("Saving a copy of the saved original…",FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session,_cancel);
        MailWrittenFile? written = null; string? error = null;
        try
        {
            // Verify again: the file may have changed since Verify.
            var source = await _reader!.ReadVerifiedAsync(row.Export,token).ConfigureAwait(false);
            if (source.Key != row.Export.Key || source.Bytes is null || source.Bytes.Length != row.Export.Bytes) throw new InvalidDataException("Saved-original verification returned another identity or size. Nothing was saved.");
            written = await target.WriteNewAsync(async (stream, cancel) =>
            {
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(source.Bytes)),row.Export.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The saved original no longer matches its checksum. Nothing was saved.");
                await stream.WriteAsync(source.Bytes,cancel).ConfigureAwait(false);
            },token).ConfigureAwait(false);
            if (written.Bytes != source.Bytes.Length) throw new InvalidDataException("The saved copy has another size.");
        }
        catch (OperationCanceledException) { written = null; error = "Saving the copy was canceled. Nothing was saved."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { written = null; error = "The copy was not saved. " + failure.Message; }
        if (IsDisposed) { cancellation.Dispose(); return; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation,cancellation)) { cancellation.Dispose(); return; }
            _operation = null; cancellation.Dispose();
            if (generation == _generation && _active && _account == account)
                _feedback.Set(written is not null ? $"Saved a copy as {written.LocalPath ?? written.DisplayName} ({MailByteSize.Format(written.Bytes)}). The saved original stays in this profile." : error ?? "Nothing was saved.",
                    written is not null ? FeedbackKind.Success : FeedbackKind.Warning);
            UpdateControls();
            if (Session is {} current && (current.FocusedElement is null || current.FocusedElement == _cancel || !current.FocusedElement.CanFocus))
                FocusNavigation.FocusAndReveal(current,_saveCopy.CanFocus ? _saveCopy : _rows.Count > 0 ? _picker : _list);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
    }
    private void UpdateControls()
    {
        if (IsDisposed) return; bool available = _active && _account is not null && _catalog is not null && _reader is not null, idle = available && !IsBusy && _pendingRemoval is null;
        _list.IsEnabled = idle; _picker.IsEnabled = idle && _rows.Count > 0; _use.IsEnabled = idle && Selected() is not null; _cancel.IsEnabled = IsBusy;
        _remove.IsEnabled = idle && _maintenance is not null && Selected() is not null;
        _saveCopy.IsEnabled = idle && CanPickSave && _checkedExport is {} verified && Selected()?.Export == verified;
        _confirmation.Visibility = _pendingRemoval is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        _keep.IsEnabled = _delete.IsEnabled = available && !IsBusy && _pendingRemoval is not null;
    }
    protected override BSize MeasureCore(BSize size) => Children[0].Measure(size);
    protected override void ArrangeCore(BRect rect) => Children[0].Arrange(rect);
    protected override void Dispose(bool disposing) { if (disposing) { ++_generation; _operation?.Cancel(); _operation?.Dispose(); _operation = null; _rows = []; } base.Dispose(disposing); }
}
