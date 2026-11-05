using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Explicit local byte inspection/import, with no invented server message identity.</summary>
public sealed class LocalOriginalsView : UiElement
{
    private readonly ILocalMessageOriginals? _store;
    private readonly ILocalOriginalManagement? _management;
    private readonly ILocalOriginalHtmlHost? _htmlHost;
    private readonly StandardPanel _layout = new() { Spacing = 6 };
    private readonly StandardEdit _path = new() { MaxLength = 32767, AccessibleName = "Local .eml file path" };
    private readonly StandardButton _open = new() { Text = "Open local .eml source" }, _import = new() { Text = "Import opened original" },
        _list = new() { Text = "List local imports" }, _read = new() { Text = "Open selected import" }, _cancel = new() { Text = "Cancel local-original operation" };
    private readonly StandardEdit _destination = new() { MaxLength = 32767, AccessibleName = "New local .eml export path" };
    private readonly StandardButton _decode = new() { Text = "Read decoded text" }, _source = new() { Text = "Show source bytes" },
        _html = new() { Text = "Show local HTML" },
        _export = new() { Text = "Export selected import" }, _remove = new() { Text = "Remove selected import…" },
        _keep = new() { Text = "Keep imported original" }, _confirm = new() { Text = "Remove import after recovery backup" };
    private LocalOriginalContent? _currentContent;
    private LocalOriginalReceipt? _pendingRemoval;
    public bool HasPendingRemoval => _pendingRemoval is not null;
    private readonly StandardComboBox _picker = new() { AccessibleName = "Local imported originals" };
    private readonly InlineFeedback _feedback = new();
    private ScrollableMessageText? _preview;
    private readonly StandardPanel _previewArea = new();
    private readonly StandardPanel _htmlArea = new() { Visibility = UiVisibility.Collapsed };
    private LocalOriginalPreview? _decoded;
    private UiElement? _htmlViewport;
    private AccountProfile? _account; private bool _active; private int _generation; private CancellationTokenSource? _operation;
    private LocalOriginalContent? _opened;
    private IReadOnlyList<LocalOriginalReceipt> _rows = [];
    public bool IsBusy => _operation is not null;
    public IReadOnlyList<LocalOriginalReceipt> Imports => _rows;
    public string PreviewText => _preview?.Text ?? "";
    public string Status => _feedback.Message;
    public LocalOriginalsView(ILocalMessageOriginals? store, ILocalOriginalHtmlHost? htmlHost = null)
    {
        _store = store; _management = store as ILocalOriginalManagement; _htmlHost = htmlHost; AddChild(_layout);
        ConfigurationForm.AddText(_layout, "Local .eml originals: open an absolute file path to inspect source bytes. Import separately to keep an exact copy for this account, including disabled accounts. Up to 2 MiB per file and 16 files or 4 MiB per profile. Imports are included in profile backups. Read decoded text to see the MIME body. Where supported, Show local HTML displays a passive preview with links and images blocked. Export creates an exact new .eml copy. Removal requires confirmation and a verified recovery backup.");
        ConfigurationForm.AddLabeledControl(_layout, "Absolute .eml file path", _path);
        var toolbar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        foreach (var button in new[] { _open, _import, _list, _read, _decode, _html, _source, _cancel }) toolbar.AddChild(button);
        _layout.AddChild(toolbar); _layout.AddChild(_picker);
        ConfigurationForm.AddLabeledControl(_layout, "New export .eml path (existing directory)", _destination);
        var management = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        foreach (var button in new[] { _export, _remove, _keep, _confirm }) management.AddChild(button);
        _layout.AddChild(management); _layout.AddChild(_feedback);
        _layout.AddChild(_htmlArea); _layout.AddChild(_previewArea);
        _open.Clicked += async (_, _) => await OpenFileAsync(_path.Text);
        _import.Clicked += async (_, _) => await ImportOpenedAsync(); _list.Clicked += async (_, _) => await ListAsync();
        _read.Clicked += async (_, _) => { if (Selected() is {} row) await ReadAsync(row); };
        _decode.Clicked += async (_, _) => await DecodeAsync(); _source.Clicked += (_, _) => ShowSource();
        _html.Clicked += (_, _) => ToggleHtml();
        _export.Clicked += async (_, _) => { if (Selected() is {} row) await ExportAsync(row, _destination.Text); };
        _remove.Clicked += (_, _) => { if (Selected() is {} row) RequestRemoval(row); };
        _keep.Clicked += (_, _) => Keep(); _confirm.Clicked += async (_, _) => await ConfirmRemovalAsync();
        _destination.TextChanged += (_, _) => { if (IsBusy) { ++_generation; _operation?.Cancel(); } _pendingRemoval = null; UpdateControls(); };
        _cancel.Clicked += (_, _) => Cancel();
        _path.TextChanged += (_, _) => InvalidatePreview();
        _picker.SelectionChanged += (_, _) => InvalidatePreview();
        _feedback.Set(store is null ? "Local original import is unavailable in this host." : "Open a file or List imports explicitly. Nothing is read automatically.", FeedbackKind.Information);
        UpdateControls();
    }
    public void SetAccount(AccountProfile? account)
    { if (IsDisposed || _account == account) return; _account = account; Reset(); }
    public void SetActive(bool active)
    { if (IsDisposed || _active == active) return; _active = active; if (!active) Reset(); else UpdateControls(); }
    private LocalOriginalReceipt? Selected() => _picker.SelectedIndex >= 0 && _picker.SelectedIndex < _rows.Count ? _rows[_picker.SelectedIndex] : null;
    private void ClearPreview()
    {
        _opened = null; _currentContent = null;
        _decoded = null;
        CloseHtml();
        foreach (var child in _previewArea.Children.ToArray()) { _previewArea.RemoveChild(child); child.Dispose(); }
        _preview = null;
    }

    private void CloseHtml()
    {
        if (_htmlViewport is not { } viewport) return;
        _htmlViewport = null;
        _htmlArea.RemoveChild(viewport);
        _htmlArea.Visibility = UiVisibility.Collapsed;
        viewport.Dispose();
        _html.Text = "Show local HTML";
    }

    private void ToggleHtml()
    {
        if (!_html.IsEnabled || _htmlHost is null || _decoded?.HtmlText is not { } html) return;
        if (_htmlViewport is not null) { CloseHtml(); return; }

        try
        {
            var surface = _htmlHost.CreateLocalOriginalContent(html);
            var viewport = new LocalHtmlViewport(surface);
            _htmlViewport = viewport;
            if (surface is IInlineHtmlReader reader)
            {
                reader.RenderingFailed += (_, _) =>
                {
                    void Fallback()
                    {
                        if (IsDisposed || _htmlViewport != viewport) return;
                        CloseHtml();
                        _feedback.Set("Local HTML could not be rendered. Decoded text is available below.", FeedbackKind.Information);
                    }
                    if (Session is { } session) session.Dispatcher.Post(Fallback); else Fallback();
                };
            }
            _htmlArea.AddChild(viewport);
            _htmlArea.Visibility = UiVisibility.Visible;
            if (Session is not null) StandardThemeController.ApplyToSubtree(viewport, StandardControlPaint.GetTheme(this));
            _html.Text = "Hide local HTML";
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            CloseHtml();
            _feedback.Set("Local HTML could not be rendered. Decoded text is available below.", FeedbackKind.Information);
        }
    }

    private sealed class LocalHtmlViewport : UiElement
    {
        private readonly UiElement _surface;

        public LocalHtmlViewport(UiElement surface)
        {
            _surface = surface;
            AddChild(surface);
        }

        protected override BSize MeasureCore(BSize size) => _surface.Measure(new(size.Width, Math.Min(size.Height, 280)));
        protected override void ArrangeCore(BRect rect) => _surface.Arrange(rect);
    }
    private void ShowPreview(string text, string name = "Local original source byte preview")
    {
        foreach (var child in _previewArea.Children.ToArray()) { _previewArea.RemoveChild(child); child.Dispose(); }
        _preview = new(); _preview.Editor.AccessibleName = name; _preview.Text = text;
        var frame = new BoundedScrollArea(_preview, 0.4, "Local original source preview"); _previewArea.AddChild(frame);
        if (Session is not null) StandardThemeController.ApplyToSubtree(frame, StandardControlPaint.GetTheme(this));
    }
    private void InvalidatePreview()
    {
        if (IsDisposed) return;
        if (IsBusy) { ++_generation; _operation?.Cancel(); }
        _pendingRemoval = null; ClearPreview(); UpdateControls();
    }
    private void Reset()
    {
        ++_generation; _operation?.Cancel(); _operation = null; _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1; _path.Text = ""; _destination.Text = ""; _pendingRemoval = null; ClearPreview();
        _feedback.Set(_store is null ? "Local original import is unavailable in this host." : "Context changed. Open a file or List this account's imports explicitly.", FeedbackKind.Information); UpdateControls(); RestoreActionFocus();
    }
    private void RestoreActionFocus()
    {
        if (_active && Session is {} session && (session.FocusedElement == _cancel || session.FocusedElement == _keep || session.FocusedElement == _confirm) && _open.CanFocus)
            FocusNavigation.FocusAndReveal(session, _rows.Count > 0 ? _picker : _open);
    }
    public void Cancel() { if (!IsDisposed) _operation?.Cancel(); }
    public Task OpenFileAsync(string path) => RunAsync("open", path);
    public Task ImportOpenedAsync() => RunAsync("import");
    public Task ListAsync() => RunAsync("list");
    public Task ReadAsync(LocalOriginalReceipt receipt) => RunAsync("read", receipt: receipt);
    public Task DecodeAsync() => RunAsync("decode");
    public Task ExportAsync(LocalOriginalReceipt receipt, string destination) => RunAsync("export", destination, receipt);
    public void ShowSource()
    {
        if (IsDisposed || IsBusy || !_active || _pendingRemoval is not null || _currentContent is not {} content) return;
        CloseHtml();
        _decoded = null;
        var shown = OriginalSourceText.Format(content.Bytes);
        ShowPreview($"{content.FileName} — {shown.BytesShown} of {shown.TotalBytes} source bytes (ASCII byte view; not decoded MIME).\n" + shown.Text);
        UpdateControls();
    }
    public void RequestRemoval(LocalOriginalReceipt receipt)
    {
        if (IsDisposed || IsBusy || !_active || _management?.CanRemove != true || !_rows.Contains(receipt) || receipt.AccountId != _account?.Id) return;
        _picker.SelectedIndex = _rows.ToList().FindIndex(row => row == receipt); ClearPreview(); _pendingRemoval = receipt;
        _feedback.Set($"Remove {receipt.FileName} (local ID {receipt.Id}) from account {receipt.AccountId}? Keep is the first choice. Removal first creates a verified whole-profile backup, including other accounts' persisted data; external files and server mail remain untouched.", FeedbackKind.Information);
        UpdateControls(); if (Session is {} session) FocusNavigation.FocusAndReveal(session, _keep);
    }
    public void Keep()
    { if (IsDisposed || IsBusy || _pendingRemoval is null) return; _pendingRemoval = null; _feedback.Set("Import kept. No removal or backup was performed.", FeedbackKind.Information); UpdateControls(); RestoreActionFocus(); }
    public Task ConfirmRemovalAsync() => _pendingRemoval is {} receipt ? RunAsync("remove", receipt: receipt) : Task.CompletedTask;
    private async Task RunAsync(string operation, string? path = null, LocalOriginalReceipt? receipt = null)
    {
        if (IsDisposed || IsBusy || !_active || _account is not {} account || _store is null ||
            operation == "import" && _opened is null || operation == "decode" && (_management is null || _currentContent is null) ||
            operation is "read" or "export" or "remove" && (receipt is null || !_rows.Contains(receipt)) ||
            operation is "export" or "remove" && _management is null || operation == "remove" && (_management?.CanRemove != true || _pendingRemoval != receipt) ||
            _pendingRemoval is not null && operation != "remove") return;
        var opened = _opened; var currentContent = _currentContent; ClearPreview();
        if (operation is "read" or "export" or "remove") _picker.SelectedIndex = _rows.ToList().FindIndex(row => row == receipt);
        int generation = ++_generation; var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set("Working with local original bytes…", FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session, _cancel);
        LocalOriginalContent? content = null; LocalOriginalReceipt? imported = null; IReadOnlyList<LocalOriginalReceipt>? rows = null; LocalOriginalPreview? decoded = null; LocalOriginalExport? exported = null; LocalOriginalRemoval? removed = null; string? error = null;
        try
        {
            switch (operation)
            {
                case "open": content = await Task.Run(() => _store.OpenFileAsync(path!, token), token).ConfigureAwait(false); break;
                case "import": imported = await Task.Run(() => _store.ImportAsync(account.Id, opened!, token), token).ConfigureAwait(false); break;
                case "list": rows = await Task.Run(() => _store.ListImportsAsync(account.Id, token), token).ConfigureAwait(false); break;
                case "decode": decoded = await Task.Run(() => _management!.DecodeAsync(currentContent!, token), token).ConfigureAwait(false); break;
                case "export": exported = await Task.Run(() => _management!.ExportAsync(receipt!, path!, token), token).ConfigureAwait(false); break;
                case "remove": removed = await Task.Run(() => _management!.RemoveAsync(receipt!, token), token).ConfigureAwait(false); break;
                case "read": content = await Task.Run(() => _store.ReadImportAsync(receipt!, token), token).ConfigureAwait(false); break;
            }
            token.ThrowIfCancellationRequested();
            if (content is not null && (content.Bytes is null || content.Bytes.Length is < 1 or > MailMessageSource.MaximumBytes)) throw new InvalidDataException("Invalid local original size.");
            if (rows is not null && (rows.Count > 16 || rows.Any(row => row.AccountId != account.Id) || rows.Select(row => row.Id).Distinct().Count() != rows.Count)) throw new InvalidDataException("Mismatched local-original inventory.");
            if (imported is not null && imported.AccountId != account.Id) throw new InvalidDataException("Import returned another account.");
            if (removed is not null && removed.Removed != receipt) throw new InvalidDataException("Removal returned another import identity.");
            if (exported is not null && (exported.Bytes != receipt!.Bytes || !string.Equals(exported.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Export returned another import's checksum or size.");
            if (decoded is not null && (decoded.PlainText is null || decoded.PlainText.Length > 32000 || decoded.Subject is null || decoded.Subject.Length > 512 || decoded.Sender is null || decoded.Sender.Length > 512 || decoded.HtmlText?.Length > LocalOriginalPreview.MaximumHtmlCharacters)) throw new InvalidDataException("Decoded original exceeds its display bounds.");
        }
        catch (OperationCanceledException) { error = "Operation canceled. An export/removal may already have committed and a recovery backup may remain. List imports and check the destination/backups before retrying. No server action occurred."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "Local-original operation did not finish. " + failure.Message; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation, cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _operation = null; _pendingRemoval = null; cancellation.Dispose();
            if (generation != _generation || !_active || _account != account)
            { ClearPreview(); _feedback.Set("Context changed. Open or List explicitly to continue.", FeedbackKind.Information); UpdateControls(); RestoreActionFocus(); return; }
            if (error is not null || canceled) _feedback.Set(error ?? "Operation canceled. List imports to check for an already committed copy.", FeedbackKind.Information);
            else if (removed is not null)
            {
                _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1;
                ShowPreview($"Removed local import {removed.Removed.FileName} ({removed.Removed.Id}).\nVerified recovery backup: {removed.BackupDirectory}\nRestore this backup into a separate profile through Local mail storage if needed.");
                _feedback.Set("Import removed after verified backup. Other imports, external files and server mail were kept. List to refresh.", FeedbackKind.Success);
            }
            else if (exported is not null)
            {
                ShowPreview($"Exported exact original: {exported.Path}\n{exported.Bytes} bytes; SHA-256 {exported.Sha256}");
                _feedback.Set("New .eml copy saved. Nothing was overwritten or launched.", FeedbackKind.Success);
            }
            else if (decoded is not null)
            {
                _currentContent = currentContent; _opened = opened;
                _decoded = decoded;
                ShowPreview($"{currentContent!.FileName}\nSubject: {decoded.Subject}\nFrom: {decoded.Sender}\nDecoded text; {(decoded.IsTruncated ? "truncated at the reader limit" : "bounded supported body")}{(decoded.IsHtmlFallback ? "; extracted from HTML" : "")}{(decoded.HasIncompleteParts ? "; MIME structure is incomplete/limited" : "")}.\n\n" + decoded.PlainText, "Local original decoded text");
                _feedback.Set(decoded.HtmlExceedsLimit ? "Decoded into selectable text. HTML exceeds the local preview limit." : "Decoded locally into selectable text.", FeedbackKind.Success);
            }
            else if (content is not null)
            {
                var preview = OriginalSourceText.Format(content.Bytes); _currentContent = new(content.FileName, content.Bytes.ToArray()); _opened = operation == "open" ? new(content.FileName, content.Bytes.ToArray()) : null;
                ShowPreview($"{content.FileName} — {preview.BytesShown} of {preview.TotalBytes} source bytes (ASCII byte view; not decoded MIME).\n" + preview.Text);
                _feedback.Set(operation == "open" ? "File opened read-only. Import explicitly to keep these exact bytes; the original file is unchanged." : "Imported original verified locally. No server identity or request.", FeedbackKind.Success);
            }
            else if (rows is not null)
            {
                _rows = Array.AsReadOnly(rows.ToArray()); _picker.SetItems(_rows.Select(row => new UiComboBoxItem(row.Id.ToString(), $"{row.FileName} · {row.Bytes} bytes · {row.ImportedAt:O}"))); _picker.SelectedIndex = -1;
                _feedback.Set($"{rows.Count} local originals for {account.DisplayName} <{account.EmailAddress}> ({account.Id}). Choose and Open explicitly.", FeedbackKind.Information);
            }
            else if (imported is not null) _feedback.Set($"Imported {imported.FileName}; local ID {imported.Id}; account {imported.AccountId}; {imported.Bytes} bytes; SHA-256 {imported.Sha256}. Included in profile backups. List imports to open it again.", FeedbackKind.Success);
            UpdateControls();
            if (Session is {} current && current.FocusedElement == _cancel) FocusNavigation.FocusAndReveal(current, _rows.Count > 0 ? _picker : _open);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { if (ReferenceEquals(_operation, cancellation)) _operation = null; cancellation.Dispose(); }
    }
    private void UpdateControls()
    {
        if (IsDisposed) return; bool available = _active && _account is not null && _store is not null && !IsBusy && _pendingRemoval is null;
        _path.IsEnabled = available; _open.IsEnabled = available; _import.IsEnabled = available && _opened is not null;
        _destination.IsEnabled = available; _decode.Visibility = _management is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        _html.Visibility = _htmlHost is not null && _decoded?.HtmlText is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
        _html.IsEnabled = available && _html.Visibility == UiVisibility.Visible;
        _decode.IsEnabled = available && _management is not null && _currentContent is not null; _source.IsEnabled = available && _currentContent is not null;
        _export.IsEnabled = available && _management is not null && Selected() is not null; _remove.IsEnabled = available && _management?.CanRemove == true && Selected() is not null;
        _keep.Visibility = _confirm.Visibility = _pendingRemoval is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        _keep.IsEnabled = _confirm.IsEnabled = _active && !IsBusy && _pendingRemoval is not null;
        _list.IsEnabled = available; _read.IsEnabled = available && Selected() is not null; _picker.IsEnabled = available && _rows.Count > 0; _cancel.IsEnabled = IsBusy;
    }
    protected override BSize MeasureCore(BSize size) => _layout.Measure(size);
    protected override void ArrangeCore(BRect rect) => _layout.Arrange(rect);
    protected override void Dispose(bool disposing) { if (disposing) { ++_generation; _operation?.Cancel(); _operation = null; ClearPreview(); _rows = []; } base.Dispose(disposing); }
}
