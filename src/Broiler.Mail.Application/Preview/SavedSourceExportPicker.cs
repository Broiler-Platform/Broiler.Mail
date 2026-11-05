using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Explicit local receipt listing and verified activation for one current reader.</summary>
public sealed class SavedSourceExportPicker : UiElement
{
    private readonly MailMessageKey _key; private readonly IMessageSourceExportCatalog? _catalog; private readonly IVerifiedMessageSourceExports? _reader;
    private readonly Func<bool> _current, _available; private readonly Action<MessageSourceExport?> _activate;
    private readonly StandardPanel _layout = new() { Spacing = 4 };
    private readonly StandardButton _list = new() { Text = "List saved originals" }, _use = new() { Text = "Use selected saved original" }, _cancel = new() { Text = "Cancel saved-original lookup" };
    private readonly StandardComboBox _picker = new() { AccessibleName = "Saved originals for this message" };
    private readonly InlineFeedback _feedback = new();
    private CancellationTokenSource? _operation; private int _generation;
    public IReadOnlyList<SavedMessageSourceExport> Exports { get; private set; } = [];
    public bool IsBusy => _operation is not null;
    public string Status => _feedback.Message;
    public SavedSourceExportPicker(MailMessageKey key, IMessageSourceExportCatalog? catalog, IVerifiedMessageSourceExports? reader, Func<bool> current, Func<bool> available, Action<MessageSourceExport?> activate)
    {
        (_key,_catalog,_reader,_current,_available,_activate) = (key,catalog,reader,current,available,activate);
        AddChild(_layout); Visibility = catalog is not null && reader is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
        ConfigurationForm.AddText(_layout, "List local receipts for this exact message, including after restart. Choose one, then Use to verify its saved bytes before source preview or attachment extraction. No server request or file opening. At most 64 receipts across the profile. Older uncataloged exports are kept but not guessed. Export files and their catalog require separate backups.");
        var toolbar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        toolbar.AddChild(_list); toolbar.AddChild(_use); toolbar.AddChild(_cancel); _layout.AddChild(toolbar); _layout.AddChild(_picker); _layout.AddChild(_feedback);
        _list.Clicked += async (_,_) => await ListAsync(); _use.Clicked += async (_,_) => { if (Selected() is {} selected) await UseAsync(selected); };
        _cancel.Clicked += (_,_) => Cancel(); _picker.SelectionChanged += (_,_) => UpdateControls();
        _feedback.Set("No saved-original list loaded. Listing reads receipt metadata only; Use verifies the selected file.", FeedbackKind.Information); UpdateControls();
    }
    private SavedMessageSourceExport? Selected() => _picker.SelectedIndex >= 0 && _picker.SelectedIndex < Exports.Count ? Exports[_picker.SelectedIndex] : null;
    private void Clear() { Exports = []; _picker.SetItems([]); _picker.SelectedIndex = -1; }
    public void Reset()
    {
        if (IsDisposed) return; ++_generation; Cancel(); Clear(); _activate(null);
        _feedback.Set("Saved-original lookup reset. List explicitly to choose a saved receipt.", FeedbackKind.Information); UpdateControls();
    }
    public void RefreshContext()
    { if (IsDisposed) return; if (!_current()) Reset(); UpdateControls(); }
    public void Cancel() { if (!IsDisposed) _operation?.Cancel(); }
    public Task ListAsync() => RunAsync(null);
    public Task UseAsync(SavedMessageSourceExport selected) => RunAsync(selected);
    private async Task RunAsync(SavedMessageSourceExport? selected)
    {
        if (IsDisposed || IsBusy || !_current() || !_available() || _catalog is null || _reader is null || selected is not null && !Exports.Contains(selected)) return;
        _activate(null); if (selected is null) Clear(); int generation = ++_generation;
        var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set(selected is null ? "Reading local saved-original receipts…" : "Verifying the selected saved original…", FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session,_cancel);
        IReadOnlyList<SavedMessageSourceExport>? rows = null; MessageSourceExport? verified = null; string? error = null;
        try
        {
            if (selected is null)
            {
                rows = await _catalog.ListSavedExportsAsync(_key,token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                if (rows.Count > 64 || rows.Any(row => row.Export.Key != _key) || rows.Select(row => row.Export.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
                    throw new InvalidDataException("Saved-original list returned invalid or mismatched receipts.");
            }
            else
            {
                var source = await _reader.ReadVerifiedAsync(selected.Export,token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                if (source.Key != _key || source.Bytes.Length != selected.Export.Bytes) throw new InvalidDataException("Saved-original verification returned another identity or size.");
                verified = selected.Export;
            }
        }
        catch (OperationCanceledException) { error = "Saved-original lookup canceled. No file was opened or downloaded."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "Saved-original lookup did not finish. " + failure.Message; }
        if (IsDisposed) { cancellation.Dispose(); return; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation,cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _operation = null; cancellation.Dispose();
            if (generation == _generation && _current() && _available())
            {
                if (error is null && !canceled && rows is not null)
                {
                    Exports = Array.AsReadOnly(rows.ToArray());
                    _picker.SetItems(Exports.Select(row => new UiComboBoxItem(row.Export.Path,$"{row.SavedAt:O} · {row.Export.Bytes} bytes · {System.IO.Path.GetFileName(row.Export.Path)}"))); _picker.SelectedIndex = -1;
                    _feedback.Set($"{rows.Count} saved receipts found for this message. Choose one, then Use. File bytes have not been verified by listing.",FeedbackKind.Information);
                }
                else if (error is null && !canceled && verified is not null)
                { _activate(verified); _feedback.Set("Saved original verified locally. Source preview and attachment controls can now use it. Nothing was downloaded or opened.",FeedbackKind.Success); }
                else { Clear(); _feedback.Set(error ?? "Saved-original lookup canceled. List again to retry.",FeedbackKind.Information); }
            }
            UpdateControls(); if (Session is {} active && active.FocusedElement == _cancel) FocusNavigation.FocusAndReveal(active,Exports.Count > 0 ? _picker : _list);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
    }
    private void UpdateControls()
    {
        if (IsDisposed) return; bool enabled = _current() && _available() && _catalog is not null && _reader is not null;
        _list.IsEnabled = enabled && !IsBusy; _picker.IsEnabled = enabled && !IsBusy && Exports.Count > 0; _use.IsEnabled = enabled && !IsBusy && Selected() is not null; _cancel.IsEnabled = IsBusy;
    }
    protected override BSize MeasureCore(BSize size) => _layout.Measure(size);
    protected override void ArrangeCore(BRect rect) => _layout.Arrange(rect);
    protected override void Dispose(bool disposing) { if (disposing) { _operation?.Cancel(); _operation?.Dispose(); _operation = null; Exports = []; } base.Dispose(disposing); }
}
