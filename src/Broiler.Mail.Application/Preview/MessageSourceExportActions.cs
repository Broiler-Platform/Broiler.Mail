using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>One message's explicit original-source download/export, canceled when its reader is replaced.</summary>
public sealed class MessageSourceExportActions : UiElement
{
    private readonly FormSection _section = new("Original message (.eml)", "Download and save an exact .eml file, including attachments. Supported connections stream up to 12 MiB per message. Exports stay outside the inbox cache and profile backups.", collapsible: true, expanded: false);
    private readonly AccountProfile _account; private readonly MailMessageKey _key; private readonly IMailSourceReceiver _receiver; private readonly IMessageSourceExports _exports;
    private readonly Func<bool> _current; private readonly Func<bool> _available;
    private readonly StandardButton _save = new() { Text = "Download original .eml" }, _cancel = new() { Text = "Cancel original source export" };
    private readonly InlineFeedback _feedback = new();
    private readonly StandardRichEdit _path = new() { IsReadOnly = true, AccessibleName = "Saved original source path and checksum", Wrapping = RichEditWrapping.Wrap, VerticalScrollPolicy = RichEditScrollPolicy.Never, HorizontalScrollPolicy = RichEditScrollPolicy.Never };
    private readonly MessageSourceInspection _inspection;
    private readonly MessageAttachmentActions _attachments;
    private readonly SavedSourceExportPicker _saved;
    private CancellationTokenSource? _operation;
    public bool IsBusy => _operation is not null;
    public string Status => _feedback.Message;
    public MessageSourceExportActions(AccountProfile account, MailMessageKey key, IMailSourceReceiver receiver, IMessageSourceExports exports, Func<bool> current, Func<bool> available)
    {
        if (key.AccountId != account.Id) throw new ArgumentException("Source export account does not match the message.");
        _account = account; _key = key; _receiver = receiver; _exports = exports; _current = current; _available = available;
        AddChild(_section); _section.Content.AccessibleName = "Original source export controls";
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 }; actions.AddChild(_save); actions.AddChild(_cancel);
        _inspection = new MessageSourceInspection(key, exports as IVerifiedMessageSourceExports, current);
        _attachments = new MessageAttachmentActions(key, exports as IMessageAttachmentExports, current);
        _saved = new SavedSourceExportPicker(key, exports as IMessageSourceExportCatalog, exports as IVerifiedMessageSourceExports, current, () => !IsBusy, ActivateSavedExport);
        _section.Content.AddChild(actions); _section.Content.AddChild(_feedback); _section.Content.AddChild(_path); _section.Content.AddChild(_saved); _section.Content.AddChild(_inspection); _section.Content.AddChild(_attachments);
        _save.Clicked += async (_, _) => await ExportAsync(); _cancel.Clicked += (_, _) => Cancel();
        _feedback.Set("No original source downloaded by this control. Exporting does not mark mail read. Up to 64 export files / 128 MiB; no automatic eviction.", FeedbackKind.Information);
        RefreshAvailability();
    }
    public void RefreshAvailability()
    {
        if (IsDisposed) return;
        bool available = _current() && _available();
        if (!available) _operation?.Cancel();
        _save.IsEnabled = available && !IsBusy; _cancel.IsEnabled = IsBusy; _inspection.RefreshContext(); _attachments.RefreshContext(); _saved.RefreshContext();
    }
    public void Cancel()
    {
        if (IsDisposed) return; _operation?.Cancel();
    }
    public async Task ExportAsync()
    {
        if (IsDisposed || IsBusy || !_current() || !_available()) return;
        var cancellation = new CancellationTokenSource(); var token = cancellation.Token; _operation = cancellation;
        _saved.Reset(); _attachments.SetExport(null); _inspection.SetExport(null); _path.SetPlainText(""); _feedback.Set("Downloading original message source…", FeedbackKind.Progress); RefreshAvailability();
        if (Session is { } session) FocusNavigation.FocusAndReveal(session, _cancel);
        MessageSourceExport? exported = null; string? error = null;
        try
        {
            if (_receiver is IStreamingMailSourceReceiver streaming && _exports is IStreamingMessageSourceExports files)
            {
                exported = await files.SaveStreamAsync(_key, async (destination, maximum, ct) =>
                {
                    int size = await streaming.CopySourceAsync(_account, _key, destination, maximum, Report, ct).ConfigureAwait(false);
                    if (!_current() || !_available()) throw new OperationCanceledException(ct);
                    return size;
                }, token).ConfigureAwait(false);
            }
            else
            {
                var source = await _receiver.GetSourceAsync(_account, _key, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!_current() || !_available()) throw new OperationCanceledException(token);
                if (source.Key != _key) throw new InvalidDataException("Original source returned another message identity.");
                exported = await _exports.SaveAsync(source, token).ConfigureAwait(false);
            }
            if (exported.Key != _key) throw new InvalidDataException("Source export returned another message identity.");
        }
        catch (OperationCanceledException) { error = "Export cancellation requested. A file completed just before cancellation can remain in the source-exports folder; no file was opened."; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or MailConnectionException)
        { error = "Original source export did not finish. " + failure.Message; }
        void Report(int transferred, int total)
        {
            void Update()
            {
                if (IsDisposed || !ReferenceEquals(_operation, cancellation) || token.IsCancellationRequested || !_current()) return;
                _feedback.Set($"Downloading original message source: {transferred:N0} of {total:N0} bytes.", FeedbackKind.Progress);
            }
            try { if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(Update); else Update(); }
            catch (ObjectDisposedException) { }
        }
        if (IsDisposed) { cancellation.Dispose(); return; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation, cancellation)) { cancellation.Dispose(); return; }
            bool canceled = cancellation.IsCancellationRequested; _operation = null; cancellation.Dispose();
            if (_current())
            {
                if (exported is not null && error is null && !canceled)
                {
                    _attachments.SetExport(exported); _inspection.SetExport(exported);
                    _path.SetPlainText(exported.Path + "\n" + exported.Bytes + " bytes; SHA-256 " + exported.Sha256);
                    _feedback.Set("Original bytes saved as .eml. The file was not opened. Save a separate copy with your own backups; profile snapshots exclude source exports.", FeedbackKind.Success);
                }
                else _feedback.Set(error ?? (canceled ? "Original source export canceled. A completed file and receipt may remain; list saved originals explicitly to reuse it." : "Original source export did not finish."), FeedbackKind.Information);
            }
            RefreshAvailability();
            if (Session is { } currentSession && currentSession.FocusedElement == _cancel) FocusNavigation.FocusAndReveal(currentSession, _save);
        }
        try { if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(Commit); else Commit(); } catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
    }
    private void ActivateSavedExport(MessageSourceExport? receipt)
    {
        if (IsDisposed) return;
        _attachments.SetExport(receipt); _inspection.SetExport(receipt);
        _path.SetPlainText(receipt is null ? "" : receipt.Path + "\n" + receipt.Bytes + " bytes; SHA-256 " + receipt.Sha256);
        _feedback.Set(receipt is null ? "No saved original is active. Choose and verify a local receipt, or download a new original explicitly." :
            "Saved original verified locally. No source was downloaded or opened. Back up export files and their catalog separately.", FeedbackKind.Information);
    }
    protected override BSize MeasureCore(BSize size) => _section.Measure(size);
    protected override void ArrangeCore(BRect rect) => _section.Arrange(rect);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _operation?.Cancel(); _operation?.Dispose(); _operation = null; }
        base.Dispose(disposing);
    }
}
