using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Explicit local inspection of one current reader's verified export receipt.</summary>
public sealed class MessageSourceInspection : UiElement
{
    private readonly MailMessageKey _key;
    private readonly IVerifiedMessageSourceExports? _reader;
    private readonly Func<bool> _current;
    private readonly StandardPanel _layout = new() { Spacing = 4 };
    private readonly StandardButton _toggle = new() { Text = "Show saved source preview" }, _copy = new() { Text = "Copy displayed source preview" }, _cancel = new() { Text = "Cancel source preview" };
    private readonly InlineFeedback _feedback = new();
    private readonly ScrollableMessageText _preview = new();
    private readonly PreviewFrame _frame;
    private MessageSourceExport? _receipt;
    private OriginalSourceTextPreview? _shown;
    private CancellationTokenSource? _read;
    private int _generation;
    public string PreviewText => _shown?.Text ?? "";
    public bool IsPreviewVisible => _shown is not null;
    public bool IsBusy => _read is not null;
    public string Status => _feedback.Message;
    public MessageSourceInspection(MailMessageKey key, IVerifiedMessageSourceExports? reader, Func<bool> current)
    {
        _key = key; _reader = reader; _current = current; _frame = new(_preview) { Visibility = UiVisibility.Collapsed };
        AddChild(_layout); Visibility = UiVisibility.Collapsed; AccessibleName = "Saved original source inspection";
        _preview.Editor.AccessibleName = "Original source byte preview";
        ConfigurationForm.AddText(_layout, "Local ASCII byte view of the first 64 KiB only. CR/LF are normalized, backslashes doubled, and non-ASCII/control bytes shown as \\xNN. MIME, encoded words and attachments are not decoded; markup is plain text. The .eml file stays unchanged. No server request or file launch.");
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        actions.AddChild(_toggle); actions.AddChild(_copy); actions.AddChild(_cancel); _layout.AddChild(actions); _layout.AddChild(_feedback); _layout.AddChild(_frame);
        _toggle.Clicked += async (_, _) => { if (_shown is null) await ReadPreviewAsync(); else HidePreview(); };
        _copy.Clicked += (_, _) => CopyPreview(); _cancel.Clicked += (_, _) => Cancel(); UpdateControls();
    }
    public void SetExport(MessageSourceExport? receipt)
    {
        if (IsDisposed) return;
        if (receipt is not null && receipt.Key != _key) throw new ArgumentException("Source inspection receipt belongs to another message.");
        HidePreview(); _receipt = receipt; Visibility = receipt is not null && _reader is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
        _feedback.Set("Show reads and verifies this saved export locally; it never downloads source again.", FeedbackKind.Information); UpdateControls();
    }
    public void RefreshContext()
    {
        if (IsDisposed) return;
        if (!_current()) { HidePreview(); Visibility = UiVisibility.Collapsed; }
        UpdateControls();
    }
    public void Cancel() { if (!IsDisposed) _read?.Cancel(); }
    public void HidePreview()
    {
        if (IsDisposed) return;
        ++_generation; _read?.Cancel(); _shown = null; _preview.Text = ""; _frame.Visibility = UiVisibility.Collapsed;
        UpdateControls();
        if (_current() && Session is { } session && session.FocusedElement is { } focused && (focused == _preview || focused.IsDescendantOf(_preview))) FocusNavigation.FocusAndReveal(session, _toggle);
    }
    public async Task ReadPreviewAsync()
    {
        if (IsDisposed || IsBusy || !_current() || _receipt is not { } receipt || _reader is null) return;
        _shown = null; _preview.Text = ""; _frame.Visibility = UiVisibility.Collapsed;
        int generation = ++_generation; var cancellation = _read = new CancellationTokenSource(); var token = cancellation.Token;
        _feedback.Set("Reading and verifying the saved source export locally…", FeedbackKind.Progress); UpdateControls();
        if (Session is { } session) FocusNavigation.FocusAndReveal(session, _cancel);
        OriginalSourceTextPreview? preview = null; string? error = null;
        try
        {
            var source = await _reader.ReadVerifiedAsync(receipt, token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            if (source.Key != _key) throw new InvalidDataException("Source inspection returned another message identity.");
            preview = OriginalSourceText.Format(source.Bytes);
        }
        catch (OperationCanceledException) { error = "Source preview canceled. No source was downloaded or copied."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "Saved source preview unavailable. " + failure.Message; }
        if (IsDisposed) { cancellation.Dispose(); return; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_read, cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _read = null; cancellation.Dispose();
            if (generation == _generation && _current())
            {
                if (preview is not null && error is null && !canceled)
                {
                    _shown = preview; _preview.Text = preview.Text; _preview.ScrollToStart(); _frame.Visibility = UiVisibility.Visible;
                    _feedback.Set($"Showing {preview.BytesShown}/{preview.TotalBytes} original bytes{(preview.IsTruncated ? " (preview truncated)" : "")}. Normalized/escaped display only; copy does not reproduce the original .eml bytes.", FeedbackKind.Information);
                }
                else _feedback.Set(error ?? "Source preview canceled.", FeedbackKind.Information);
            }
            UpdateControls();
            if (Session is { } currentSession && currentSession.FocusedElement == _cancel) FocusNavigation.FocusAndReveal(currentSession, _shown is not null ? _preview.Editor : _toggle);
        }
        try { if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(Commit); else Commit(); }
        catch (ObjectDisposedException) { _read = null; cancellation.Dispose(); }
    }
    public void CopyPreview()
    {
        if (IsDisposed || !_current() || IsBusy || _shown is not { } preview) return;
        if (Session?.Host is not IUiClipboardHost clipboard) { _feedback.Set("Clipboard unavailable. The selectable source preview was kept.", FeedbackKind.Information); return; }
        try { clipboard.SetText(preview.Text); _feedback.Set("Displayed normalized/escaped source preview copied. This may be truncated; it is not the original .eml bytes.", FeedbackKind.Information); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or UnauthorizedAccessException or IOException or System.Runtime.InteropServices.ExternalException)
        { _feedback.Set("Clipboard copy failed. The source preview and file were kept; try again.", FeedbackKind.Information); }
    }
    private void UpdateControls()
    {
        if (IsDisposed) return;
        _toggle.Text = _shown is null ? "Show saved source preview" : "Hide saved source preview";
        _toggle.IsEnabled = !IsBusy && _reader is not null && _receipt is not null && _current(); _copy.IsEnabled = !IsBusy && _shown is not null && _current(); _cancel.IsEnabled = IsBusy;
    }
    protected override BSize MeasureCore(BSize size) => _layout.Measure(size);
    protected override void ArrangeCore(BRect rect) => _layout.Arrange(rect);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _read?.Cancel(); _read?.Dispose(); _read = null; _shown = null; _receipt = null; }
        base.Dispose(disposing);
    }
    private sealed class PreviewFrame : UiElement
    {
        private readonly ScrollableMessageText text;
        public PreviewFrame(ScrollableMessageText content) { text = content; AddChild(text); }
        protected override BSize MeasureCore(BSize size)
        {
            double height = Math.Min(size.Height, Math.Clamp(text.HeightOfLines(8),120,240));
            var measured = text.Measure(new(size.Width,height)); return new(measured.Width,height);
        }
        protected override void ArrangeCore(BRect rect) => text.Arrange(rect);
    }
}
