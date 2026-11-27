// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   57
// Annotated:        0/57
// Exempt:           28
// Human-reviewed:   0/57
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       57
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button;
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

/// <summary>
/// Explicit local .eml viewing and import, with no invented server message identity. Files up to 12 MiB open read-only
/// and show the decoded message by default; keeping a copy in the profile stays limited to 2 MiB per file.
/// The Open .eml file… and Export … as .eml… file dialogs are user-initiated: they are the documented exemption to the
/// no-modal-while-reading rule. A device whose picker cannot choose files keeps the typed path fields instead.
/// </summary>
public sealed class LocalOriginalsView : UiElement
{
    public const string OpenButtonText = "Open .eml file…";
    public const string ExportButtonText = "Export selected import as .eml…";
    public const string ShowRawText = "Show raw source (first 64 KiB)";
    public const string ShowDecodedText = "Show decoded message";
    public const string NotKeptText = "Files over 2 MiB can be viewed but not kept in this profile.";
    private const string ImportTip = "Keep an exact copy of the opened file in this profile (up to 2 MiB).";
    private readonly ILocalMessageOriginals? _store;
    private readonly ILocalOriginalManagement? _management;
    private readonly ILocalOriginalHtmlHost? _htmlHost;
    private readonly StandardPanel _layout = new() { Spacing = 6 };
    // Only a device whose picker cannot choose files has these typed path fields.
    private readonly StandardEdit? _path, _destination;
    private readonly StandardButton? _openTyped, _exportTyped;
    private readonly StandardButton _open = new() { Text = OpenButtonText, ToolTipText = "Choose an .eml file to view. The file is opened read-only and is not changed." },
        _import = new() { Text = "Import opened original", ToolTipText = ImportTip },
        _list = new() { Text = "List local imports" }, _read = new() { Text = "Open selected import" }, _cancel = new() { Text = "Cancel local-original operation" };
    private readonly StandardButton _view = new() { Text = ShowRawText },
        _html = new() { Text = "Show local HTML" },
        _export = new() { Text = ExportButtonText }, _remove = new() { Text = "Remove selected import…" },
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
    private bool _raw;
    private UiElement? _htmlViewport;
    private AccountProfile? _account; private bool _active; private int _generation; private CancellationTokenSource? _operation; private string? _running;
    private LocalOriginalContent? _opened;
    private IReadOnlyList<LocalOriginalReceipt> _rows = [];
    public bool IsBusy => _operation is not null;
    public IReadOnlyList<LocalOriginalReceipt> Imports => _rows;
    public string PreviewText => _preview?.Text ?? "";
    public string Status => _feedback.Message;
    /// <summary>
    /// Why a picked file cannot open here now, or null when it can: a removal waits for Keep or Remove, or another
    /// operation than an open runs (a newer open replaces an older one).
    /// </summary>
    public string? OpenBlockedReason =>
        IsDisposed || !_active ? null :
        _pendingRemoval is not null ? "Choose Keep imported original or Remove import first. Nothing was opened." :
        IsBusy && _running is not ("open" or "open-picked") ? "Wait for the current local-original operation to finish, or cancel it, then open the file again. Nothing was opened." :
        null;
    /// <summary>Shows <see cref="OpenBlockedReason"/> as this view's feedback. False when a picked file can open.</summary>
    public bool ExplainOpenBlocked()
    {
        if (OpenBlockedReason is not { } reason) return false;
        _feedback.Set(reason, FeedbackKind.Warning); return true;
    }
    /// <summary>The decoded message is shown, rather than the raw source.</summary>
    public bool ShowsDecoded => !_raw && _decoded is not null && _currentContent is not null;
    /// <summary>Chooses files for this view; an unavailable picker when none was given, so views keep their typed paths.</summary>
    internal IMailFilePicker Files { get; }
    private bool CanPickOpen => Files.Capabilities.HasFlag(MailFilePickerCapabilities.OpenFile);
    private bool CanPickSave => Files.Capabilities.HasFlag(MailFilePickerCapabilities.SaveFile);
    private StandardButton OpenAction => _openTyped ?? _open;
    /// <summary>What Open .eml file… asks the picker for.</summary>
    public static MailFileRequest OpenRequest { get; } = new(MailFilePurpose.OpenOriginal, "Open .eml file")
    { Types = [MailFileType.Email, MailFileType.AllFiles], AcceptText = "Open" };
    public LocalOriginalsView(ILocalMessageOriginals? store, ILocalOriginalHtmlHost? htmlHost = null, IMailFilePicker? files = null)
    {
        Files = files ?? UnavailableMailFilePicker.NotOnThisDeviceYet;
        _store = store; _management = store as ILocalOriginalManagement; _htmlHost = htmlHost; AddChild(_layout);
        string unavailable = Files is UnavailableMailFilePicker none ? none.Reason : "Choosing files is not available on this device yet.";
        ConfigurationForm.AddText(_layout, CanPickOpen
            ? "Open an .eml file to read it here (up to 12 MiB). Keep a copy in this profile for files up to 2 MiB."
            : "Type the full path of an .eml file to read it here (up to 12 MiB). Keep a copy in this profile for files up to 2 MiB.");
        if (!CanPickOpen)
        {
            _path = new() { MaxLength = 32767, AccessibleName = "Local .eml file path" };
            _openTyped = new() { Text = "Open typed .eml path" };
            _open.IsEnabled = false; _open.ToolTipText = unavailable;
            ConfigurationForm.AddLabeledControl(_layout, "Absolute .eml file path", _path);
        }
        var toolbar = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        foreach (var button in new[] { _open, _openTyped, _import, _list, _read, _view, _html, _cancel }) if (button is not null) toolbar.AddChild(button);
        _layout.AddChild(toolbar); _layout.AddChild(_picker);
        if (!CanPickSave)
        {
            _destination = new() { MaxLength = 32767, AccessibleName = "New local .eml export path" };
            _exportTyped = new() { Text = "Export to typed path" };
            _export.IsEnabled = false; _export.ToolTipText = unavailable;
            ConfigurationForm.AddLabeledControl(_layout, "New export .eml path (existing directory)", _destination);
        }
        var management = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        foreach (var button in new[] { _export, _exportTyped, _remove, _keep, _confirm }) if (button is not null) management.AddChild(button);
        _layout.AddChild(management); _layout.AddChild(_feedback);
        _layout.AddChild(_htmlArea); _layout.AddChild(_previewArea);
        _open.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await OpenWithPickerAsync());
        if (_openTyped is not null) _openTyped.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await OpenFileAsync(_path!.Text));
        _import.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await ImportOpenedAsync()); _list.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await ListAsync());
        _read.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { if (Selected() is {} row) await ReadAsync(row); });
        _view.Clicked += (_, _) => { if (_raw) ShowDecoded(); else ShowSource(); };
        _html.Clicked += (_, _) => ToggleHtml();
        _export.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { if (Selected() is {} row) await ExportWithPickerAsync(row); });
        if (_exportTyped is not null) _exportTyped.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { if (Selected() is {} row) await ExportAsync(row, _destination!.Text); });
        _remove.Clicked += (_, _) => { if (Selected() is {} row) RequestRemoval(row); };
        _keep.Clicked += (_, _) => Keep(); _confirm.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await ConfirmRemovalAsync());
        if (_destination is not null) _destination.TextChanged += (_, _) => { if (IsBusy) { ++_generation; _operation?.Cancel(); } _pendingRemoval = null; UpdateControls(); };
        _cancel.Clicked += (_, _) => Cancel();
        if (_path is not null) _path.TextChanged += (_, _) => InvalidatePreview();
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
        _decoded = null; _raw = false;
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
        if (!_html.IsEnabled || _raw || _htmlHost is null || _decoded?.HtmlText is not { } html) return;
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
    /// <summary>Shows the opened bytes as the decoded message or as raw source, from memory only.</summary>
    private void Render()
    {
        if (_currentContent is not { } content) return;
        if (_raw || _decoded is not { } decoded)
        {
            CloseHtml();
            var shown = OriginalSourceText.Format(content.Bytes);
            ShowPreview($"{content.FileName} — {shown.BytesShown} of {shown.TotalBytes} source bytes (ASCII byte view; not decoded MIME).\n" + shown.Text);
        }
        else ShowPreview(DecodedText(content, decoded), "Local original decoded message");
        UpdateControls();
    }
    internal static string DecodedText(LocalOriginalContent content, LocalOriginalPreview decoded)
    {
        var text = new StringBuilder();
        text.Append(content.FileName).Append(" · ").Append(MailByteSize.Format(content.Bytes.Length)).Append('\n');
        text.Append("Subject: ").Append(decoded.Subject).Append('\n');
        text.Append("From: ").Append(decoded.Sender).Append('\n');
        if (decoded.To is { Length: > 0 } to) text.Append("To: ").Append(to).Append('\n');
        if (decoded.Date is { } date) text.Append("Date: ").Append(date.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)).Append('\n');
        if (decoded.Attachments.Count > 0)
            text.Append("Attachments: ").Append(string.Join(", ", decoded.Attachments.Select(part => $"{part.FileName} ({MailByteSize.Format(part.DecodedBytes)})")))
                .Append(decoded.IsAttachmentListIncomplete ? ", …" : "").Append('\n');
        text.Append("Decoded text; ").Append(decoded.IsTruncated ? "truncated at the reader limit" : "bounded supported body")
            .Append(decoded.IsHtmlFallback ? "; extracted from HTML" : "").Append(decoded.HasIncompleteParts ? "; MIME structure is incomplete/limited" : "").Append(".\n\n");
        return text.Append(decoded.PlainText).ToString();
    }
    private void InvalidatePreview()
    {
        if (IsDisposed) return;
        if (IsBusy) { ++_generation; _operation?.Cancel(); }
        _pendingRemoval = null; ClearPreview(); UpdateControls();
    }
    private void Reset()
    {
        ++_generation; _operation?.Cancel(); _operation = null; _running = null; _rows = []; _picker.SetItems([]); _picker.SelectedIndex = -1;
        if (_path is not null) _path.Text = ""; if (_destination is not null) _destination.Text = ""; _pendingRemoval = null; ClearPreview();
        _feedback.Set(_store is null ? "Local original import is unavailable in this host." : "Context changed. Open a file or List this account's imports explicitly.", FeedbackKind.Information); UpdateControls(); RestoreActionFocus();
    }
    private void RestoreActionFocus()
    {
        if (_active && Session is {} session && (session.FocusedElement == _cancel || session.FocusedElement == _keep || session.FocusedElement == _confirm) && OpenAction.CanFocus)
            FocusNavigation.FocusAndReveal(session, _rows.Count > 0 ? _picker : OpenAction);
    }
    public void Cancel() { if (!IsDisposed) _operation?.Cancel(); }
    /// <summary>Opens a typed path; the picker flow uses <see cref="OpenPickedAsync"/>.</summary>
    public Task OpenFileAsync(string path) => RunAsync("open", path);
    /// <summary>Opens a file the picker returned. A newer open replaces one that is still reading.</summary>
    public Task OpenPickedAsync(IMailReadableFile file) => RunAsync("open-picked", picked: file ?? throw new ArgumentNullException(nameof(file)));
    /// <summary>Asks the picker for an .eml file and opens it. Canceling keeps the current preview.</summary>
    public async Task<bool> OpenWithPickerAsync()
    {
        if (IsDisposed || IsBusy || !_active || _store is null || _pendingRemoval is not null || !CanPickOpen) return false;
        int generation = _generation;
        var pick = await Files.PickOpenFileAsync(OpenRequest);
        if (IsDisposed || generation != _generation || !_active) return false;
        switch (pick.Status)
        {
            case MailFilePickStatus.Picked when pick.Item is { } file:
                await OpenPickedAsync(file); return true;
            case MailFilePickStatus.Rejected or MailFilePickStatus.Unavailable:
                _feedback.Set((pick.Message ?? "That file cannot be opened.") + " Nothing was opened.", FeedbackKind.Warning); break;
            default:
                _feedback.Set("No file chosen. Nothing was opened.", FeedbackKind.Information); break;
        }
        if (Session is { } session && OpenAction.CanFocus) FocusNavigation.FocusAndReveal(session, OpenAction);
        return false;
    }
    public Task ImportOpenedAsync() => RunAsync("import");
    public Task ListAsync() => RunAsync("list");
    public Task ReadAsync(LocalOriginalReceipt receipt) => RunAsync("read", receipt: receipt);
    public Task DecodeAsync() => RunAsync("decode");
    /// <summary>Exports to a typed path; the picker flow uses <see cref="ExportWithPickerAsync"/>.</summary>
    public Task ExportAsync(LocalOriginalReceipt receipt, string destination) => RunAsync("export", destination, receipt);
    /// <summary>Asks the picker for a new .eml file and writes the verified import there. Nothing is ever overwritten.</summary>
    public async Task ExportWithPickerAsync(LocalOriginalReceipt receipt)
    {
        if (IsDisposed || IsBusy || !_active || _account is null || _store is null || _management is null || _pendingRemoval is not null || !CanPickSave || !_rows.Contains(receipt)) return;
        int generation = _generation;
        var pick = await Files.PickSaveFileAsync(new(MailFilePurpose.ExportOriginal, "Export as .eml")
        { Types = [MailFileType.Email], SuggestedFileName = receipt.FileName, AcceptText = "Export" });
        if (IsDisposed || generation != _generation || !_active || !_rows.Contains(receipt)) return;
        switch (pick.Status)
        {
            case MailFilePickStatus.Picked when pick.Item is { } target:
                if (target.LocalPath is { } path && string.Equals(Path.GetExtension(path), ".eml", StringComparison.OrdinalIgnoreCase)) await RunAsync("export", path, receipt);
                else await RunAsync("export-picked", receipt: receipt, target: target);
                return;
            case MailFilePickStatus.Rejected or MailFilePickStatus.Unavailable:
                _feedback.Set((pick.Message ?? "That file cannot be used.") + " Nothing was saved.", FeedbackKind.Warning); break;
            default:
                _feedback.Set("Export canceled. Nothing was saved.", FeedbackKind.Information); break;
        }
        if (Session is { } session && _export.CanFocus) FocusNavigation.FocusAndReveal(session, _export);
    }
    /// <summary>Shows the opened file's raw source, from memory. Calling it again changes nothing.</summary>
    public void ShowSource()
    {
        if (IsDisposed || IsBusy || !_active || _pendingRemoval is not null || _currentContent is null) return;
        _raw = true; Render();
    }
    /// <summary>Shows the decoded message again, from memory; nothing is read or decoded.</summary>
    public void ShowDecoded()
    {
        if (IsDisposed || IsBusy || !_active || _pendingRemoval is not null || _currentContent is null || _decoded is null) return;
        _raw = false; Render();
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
    private async Task<LocalOriginalContent> ReadPickedAsync(IMailReadableFile file, CancellationToken token)
    {
        if (_store is ILocalOriginalStreamSource streams)
        {
            await using var stream = await file.OpenReadAsync(token).ConfigureAwait(false);
            return await streams.OpenStreamAsync(file.DisplayName, stream, token).ConfigureAwait(false);
        }
        if (file.LocalPath is { Length: > 0 } path) return await _store!.OpenFileAsync(path, token).ConfigureAwait(false);
        throw new ArgumentException("This host cannot open picked files.");
    }
    private async Task<LocalOriginalExport> WritePickedAsync(LocalOriginalReceipt receipt, IMailWritableFile target, CancellationToken token)
    {
        var original = await _store!.ReadImportAsync(receipt, token).ConfigureAwait(false);
        if (original.Bytes is null || original.Bytes.Length != receipt.Bytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(original.Bytes)), receipt.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The import no longer matches its checksum. Nothing was saved.");
        var written = await target.WriteNewAsync((stream, t) => stream.WriteAsync(original.Bytes, t).AsTask(), token).ConfigureAwait(false);
        if (written.Bytes != original.Bytes.Length) throw new InvalidDataException("The exported file has another size.");
        return new(written.LocalPath ?? written.DisplayName, original.Bytes.Length, receipt.Sha256);
    }
    private async Task RunAsync(string operation, string? path = null, LocalOriginalReceipt? receipt = null, IMailReadableFile? picked = null, IMailWritableFile? target = null)
    {
        bool opening = operation is "open" or "open-picked";
        // Opening and viewing need no account; keeping, listing and changing imports do.
        bool needsAccount = operation is "import" or "list" or "read" or "export" or "export-picked" or "remove";
        // A newer open replaces one that is still reading; the older result is then never shown.
        if (IsBusy && opening && _running is "open" or "open-picked" && !IsDisposed && _active && _pendingRemoval is null)
        { ++_generation; _operation?.Cancel(); _operation = null; _running = null; }
        if (IsDisposed || IsBusy || !_active || _store is null || needsAccount && _account is null ||
            operation == "import" && _opened is null || operation == "decode" && (_management is null || _currentContent is null) ||
            operation is "read" or "export" or "export-picked" or "remove" && (receipt is null || !_rows.Contains(receipt)) ||
            operation is "export" or "export-picked" or "remove" && _management is null || operation == "remove" && (_management?.CanRemove != true || _pendingRemoval != receipt) ||
            _pendingRemoval is not null && operation != "remove") return;
        var account = _account;
        var opened = _opened; var currentContent = _currentContent; ClearPreview();
        if (operation is "read" or "export" or "export-picked" or "remove") _picker.SelectedIndex = _rows.ToList().FindIndex(row => row == receipt);
        int generation = ++_generation; var cancellation = _operation = new CancellationTokenSource(); var token = cancellation.Token; _running = operation;
        _feedback.Set(opening ? "Opening the local .eml file…" : "Working with local original bytes…", FeedbackKind.Progress); UpdateControls();
        if (Session is {} session) FocusNavigation.FocusAndReveal(session, _cancel);
        LocalOriginalContent? content = null; LocalOriginalReceipt? imported = null; IReadOnlyList<LocalOriginalReceipt>? rows = null; LocalOriginalPreview? decoded = null; LocalOriginalExport? exported = null; LocalOriginalRemoval? removed = null; string? error = null;
        bool decodeFailed = false;
        try
        {
            switch (operation)
            {
                case "open": content = await Task.Run(() => _store.OpenFileAsync(path!, token), token).ConfigureAwait(false); break;
                case "open-picked": content = await Task.Run(() => ReadPickedAsync(picked!, token), token).ConfigureAwait(false); break;
                case "import": imported = await Task.Run(() => _store.ImportAsync(account!.Id, opened!, token), token).ConfigureAwait(false); break;
                case "list": rows = await Task.Run(() => _store.ListImportsAsync(account!.Id, token), token).ConfigureAwait(false); break;
                case "decode": decoded = await Task.Run(() => _management!.DecodeAsync(currentContent!, token), token).ConfigureAwait(false); break;
                case "export": exported = await Task.Run(() => _management!.ExportAsync(receipt!, path!, token), token).ConfigureAwait(false); break;
                case "export-picked": exported = await Task.Run(() => WritePickedAsync(receipt!, target!, token), token).ConfigureAwait(false); break;
                case "remove": removed = await Task.Run(() => _management!.RemoveAsync(receipt!, token), token).ConfigureAwait(false); break;
                case "read": content = await Task.Run(() => _store.ReadImportAsync(receipt!, token), token).ConfigureAwait(false); break;
            }
            token.ThrowIfCancellationRequested();
            int limit = operation == "read" ? LocalOriginalLimits.ImportBytes : LocalOriginalLimits.OpenBytes;
            if (content is not null && (content.Bytes is null || content.Bytes.Length < 1 || content.Bytes.Length > limit)) throw new InvalidDataException("Invalid local original size.");
            // An opened or read file shows the decoded message by default; a file that is not MIME still opens raw.
            if (content is not null && _management is not null)
            {
                try { decoded = await Task.Run(() => _management.DecodeAsync(content, token), token).ConfigureAwait(false); }
                catch (Exception failure) when (failure is InvalidDataException or ArgumentException or FormatException) { decodeFailed = true; }
                token.ThrowIfCancellationRequested();
            }
            if (rows is not null && (rows.Count > 16 || rows.Any(row => row.AccountId != account!.Id) || rows.Select(row => row.Id).Distinct().Count() != rows.Count)) throw new InvalidDataException("Mismatched local-original inventory.");
            if (imported is not null && imported.AccountId != account!.Id) throw new InvalidDataException("Import returned another account.");
            if (removed is not null && removed.Removed != receipt) throw new InvalidDataException("Removal returned another import identity.");
            if (exported is not null && (exported.Bytes != receipt!.Bytes || !string.Equals(exported.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Export returned another import's checksum or size.");
            if (decoded is not null && (decoded.PlainText is null || decoded.PlainText.Length > 32000 || decoded.Subject is null || decoded.Subject.Length > 512 || decoded.Sender is null || decoded.Sender.Length > 512 ||
                decoded.To?.Length > 512 || decoded.Attachments is null || decoded.Attachments.Count > Broiler.Mail.Core.Messages.MailAttachmentDescriptor.MaximumCount || decoded.HtmlText?.Length > LocalOriginalPreview.MaximumHtmlCharacters))
                throw new InvalidDataException("Decoded original exceeds its display bounds.");
        }
        catch (OperationCanceledException) { error = "Operation canceled. An export/removal may already have committed and a recovery backup may remain. List imports and check the destination/backups before retrying. No server action occurred."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = (operation == "open-picked" ? "The file could not be opened. " : "Local-original operation did not finish. ") + failure.Message; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation, cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _operation = null; _running = null; _pendingRemoval = null; cancellation.Dispose();
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
            else if (content is not null)
            {
                // One array serves the view and a later import; the import copies it before storing.
                _currentContent = content; _opened = opening ? content : null;
                _decoded = decoded; _raw = decoded is null; Render();
                string message = !opening ? "Imported original verified locally. No server identity or request."
                    : decoded is not null ? $"Opened {content.FileName} ({MailByteSize.Format(content.Bytes.Length)}). Showing the decoded message; the file was not changed."
                    : decodeFailed ? $"Opened {content.FileName}. It could not be read as an email message, so its raw source is shown."
                    : "File opened read-only. Import explicitly to keep these exact bytes; the original file is unchanged.";
                if (opening && content.Bytes.Length > LocalOriginalLimits.ImportBytes) message += " " + NotKeptText;
                if (decoded?.HtmlExceedsLimit == true) message += " HTML exceeds the local preview limit.";
                _feedback.Set(message, decodeFailed ? FeedbackKind.Information : FeedbackKind.Success);
            }
            else if (decoded is not null)
            {
                _currentContent = currentContent; _opened = opened;
                _decoded = decoded; _raw = false; Render();
                _feedback.Set(decoded.HtmlExceedsLimit ? "Decoded into selectable text. HTML exceeds the local preview limit." : "Decoded locally into selectable text.", FeedbackKind.Success);
            }
            else if (rows is not null)
            {
                _rows = Array.AsReadOnly(rows.ToArray()); _picker.SetItems(_rows.Select(row => new UiComboBoxItem(row.Id.ToString(), $"{row.FileName} · {row.Bytes} bytes · {row.ImportedAt:O}"))); _picker.SelectedIndex = -1;
                _feedback.Set($"{rows.Count} local originals for {account!.DisplayName} <{account.EmailAddress}> ({account.Id}). Choose and Open explicitly.", FeedbackKind.Information);
            }
            else if (imported is not null) _feedback.Set($"Imported {imported.FileName}; local ID {imported.Id}; account {imported.AccountId}; {imported.Bytes} bytes; SHA-256 {imported.Sha256}. Included in profile backups. List imports to open it again.", FeedbackKind.Success);
            UpdateControls();
            if (Session is {} current && current.FocusedElement == _cancel)
                FocusNavigation.FocusAndReveal(current, content is not null && _view.CanFocus ? _view : _rows.Count > 0 ? _picker : OpenAction);
        }
        try { if (Session?.Dispatcher is {} dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { if (ReferenceEquals(_operation, cancellation)) { _operation = null; _running = null; } cancellation.Dispose(); }
    }
    private void UpdateControls()
    {
        if (IsDisposed) return;
        bool idle = _active && _store is not null && !IsBusy && _pendingRemoval is null;
        bool available = idle && _account is not null;
        _open.IsEnabled = idle && CanPickOpen;
        if (_path is not null) _path.IsEnabled = idle;
        if (_openTyped is not null) _openTyped.IsEnabled = idle;
        bool tooLarge = _opened is { } opened && opened.Bytes.Length > LocalOriginalLimits.ImportBytes;
        _import.IsEnabled = available && _opened is not null && !tooLarge;
        _import.ToolTipText = tooLarge ? NotKeptText : ImportTip;
        if (_destination is not null) _destination.IsEnabled = available;
        _view.Visibility = _management is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        _view.Text = _raw ? ShowDecodedText : ShowRawText;
        _view.IsEnabled = idle && _currentContent is not null && _decoded is not null;
        _html.Visibility = _htmlHost is not null && !_raw && _decoded?.HtmlText is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
        _html.IsEnabled = idle && _html.Visibility == UiVisibility.Visible;
        _export.IsEnabled = available && CanPickSave && _management is not null && Selected() is not null;
        if (_exportTyped is not null) _exportTyped.IsEnabled = available && _management is not null && Selected() is not null;
        _remove.IsEnabled = available && _management?.CanRemove == true && Selected() is not null;
        _keep.Visibility = _confirm.Visibility = _pendingRemoval is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        _keep.IsEnabled = _confirm.IsEnabled = _active && !IsBusy && _pendingRemoval is not null;
        _list.IsEnabled = available; _read.IsEnabled = available && Selected() is not null; _picker.IsEnabled = available && _rows.Count > 0; _cancel.IsEnabled = IsBusy;
    }
    protected override BSize MeasureCore(BSize size) => _layout.Measure(size);
    protected override void ArrangeCore(BRect rect) => _layout.Arrange(rect);
    protected override void Dispose(bool disposing) { if (disposing) { ++_generation; _operation?.Cancel(); _operation = null; _running = null; ClearPreview(); _rows = []; } base.Dispose(disposing); }
}
