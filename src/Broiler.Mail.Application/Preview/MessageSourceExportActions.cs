// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   31
// Annotated:        0/31
// Exempt:           27
// Human-reviewed:   0/31
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       31
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Security.Cryptography;
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
    private readonly StandardButton _saveAs = new() { Text = SaveAsText, ToolTipText = SaveAsTip };
    private readonly IMailFilePicker _files; private readonly string _suggestedFileName;
    private bool _picking;
    /// <summary>The first action: saves an exact copy to a file the person chooses.</summary>
    public const string SaveAsText = "Save as .eml…";
    public const string SaveAsTip = "Saves an exact copy of the message to a file you choose. It does not use a saved-originals slot and does not mark the message read.";
    public const string SaveAsCanceledText = "Save canceled. No partial file was left.";
    public const string SaveAsChangedText = "The message changed while you chose a file. Nothing was saved.";
    /// <summary>Save as .eml… can run now: a picker that saves, the message still open and its source reachable.</summary>
    public bool CanSaveAs => !IsDisposed && !IsBusy && !_picking && _files.Capabilities.HasFlag(MailFilePickerCapabilities.SaveFile) && _current() && _available();
    private readonly InlineFeedback _feedback = new();
    private readonly StandardRichEdit _path = new() { IsReadOnly = true, AccessibleName = "Saved original source path and checksum", Wrapping = RichEditWrapping.Wrap, VerticalScrollPolicy = RichEditScrollPolicy.Never, HorizontalScrollPolicy = RichEditScrollPolicy.Never };
    private readonly MessageSourceInspection _inspection;
    private readonly MessageAttachmentActions _attachments;
    private readonly SavedSourceExportPicker _saved;
    private CancellationTokenSource? _operation;
    public bool IsBusy => _operation is not null;
    public string Status => _feedback.Message;
    /// <param name="files">Chooses the file Save as .eml… writes; an unavailable picker disables it with the picker's reason.</param>
    /// <param name="suggestedFileName">The name Save as .eml… proposes, such as <see cref="EmlFileName.Suggest"/>'s.</param>
    public MessageSourceExportActions(AccountProfile account, MailMessageKey key, IMailSourceReceiver receiver, IMessageSourceExports exports, Func<bool> current, Func<bool> available,
        IMailFilePicker? files = null, string? suggestedFileName = null)
    {
        if (key.AccountId != account.Id) throw new ArgumentException("Source export account does not match the message.");
        _account = account; _key = key; _receiver = receiver; _exports = exports; _current = current; _available = available;
        _files = files ?? UnavailableMailFilePicker.NotOnThisDeviceYet; _suggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? EmlFileName.Suggest(null, null) : suggestedFileName;
        if (!_files.Capabilities.HasFlag(MailFilePickerCapabilities.SaveFile))
            _saveAs.ToolTipText = _files is UnavailableMailFilePicker none ? none.Reason : "Choosing files is not available on this device yet.";
        AddChild(_section); _section.Content.AccessibleName = "Original source export controls";
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 }; actions.AddChild(_saveAs); actions.AddChild(_save); actions.AddChild(_cancel);
        _inspection = new MessageSourceInspection(key, exports as IVerifiedMessageSourceExports, current);
        _attachments = new MessageAttachmentActions(key, exports as IMessageAttachmentExports, current);
        _saved = new SavedSourceExportPicker(key, exports as IMessageSourceExportCatalog, exports as IVerifiedMessageSourceExports, current, () => !IsBusy, ActivateSavedExport);
        _section.Content.AddChild(actions); _section.Content.AddChild(_feedback); _section.Content.AddChild(_path); _section.Content.AddChild(_saved); _section.Content.AddChild(_inspection); _section.Content.AddChild(_attachments);
        _save.Clicked += async (_, _) => await ExportAsync(); _cancel.Clicked += (_, _) => Cancel();
        _saveAs.Clicked += async (_, _) => await SaveAsAsync();
        _feedback.Set("No original source downloaded by this control. Exporting does not mark mail read. Up to 64 export files / 128 MiB; no automatic eviction.", FeedbackKind.Information);
        RefreshAvailability();
    }
    /// <summary>Shows the export controls and focuses the first one available, as the reader's More message actions menu asks.</summary>
    public void Reveal()
    {
        if (IsDisposed) return;
        _section.Expand();
        if (Session is { } session) FocusNavigation.FocusAndReveal(session, _saveAs.CanFocus ? _saveAs : _save.CanFocus ? _save : _section.Toggle!);
    }
    public void RefreshAvailability()
    {
        if (IsDisposed) return;
        bool available = _current() && _available();
        if (!available) _operation?.Cancel();
        _save.IsEnabled = available && !IsBusy && !_picking; _cancel.IsEnabled = IsBusy; _saveAs.IsEnabled = CanSaveAs; _inspection.RefreshContext(); _attachments.RefreshContext(); _saved.RefreshContext();
    }
    public void Cancel()
    {
        if (IsDisposed) return; _operation?.Cancel();
    }
    /// <summary>The file the last Save as .eml… wrote and the SHA-256 of its bytes, or null.</summary>
    public (MailWrittenFile File, string Sha256)? LastSaved { get; private set; }
    /// <summary>
    /// Asks for a new file and streams the message's exact original octets into it, up to 12 MiB, without a saved-originals
    /// receipt and without marking the message read. The file dialog is the documented exemption to the no-modal-while-reading
    /// rule: the person asked for it. Nothing is written when the picker is canceled, the message changes or the save is canceled.
    /// </summary>
    public async Task<bool> SaveAsAsync()
    {
        if (!CanSaveAs) return false;
        _section.Expand();
        _picking = true; RefreshAvailability();
        MailFilePick<IMailWritableFile> pick;
        try
        {
            pick = await _files.PickSaveFileAsync(new(MailFilePurpose.SaveMessage, "Save message as .eml")
            { Types = [MailFileType.Email], SuggestedFileName = _suggestedFileName, AcceptText = "Save" });
        }
        finally { _picking = false; }
        if (IsDisposed) return false;
        RefreshAvailability();
        void Done(string message, FeedbackKind kind)
        {
            _feedback.Set(message, kind);
            if (Session is { } session && _saveAs.CanFocus) FocusNavigation.FocusAndReveal(session, _saveAs);
        }
        if (pick.Status != MailFilePickStatus.Picked || pick.Item is not { } target)
        {
            if (pick.Status is MailFilePickStatus.Rejected or MailFilePickStatus.Unavailable) Done((pick.Message ?? "That file cannot be used.") + " Nothing was saved.", FeedbackKind.Warning);
            else Done("No file chosen. Nothing was saved.", FeedbackKind.Information);
            return false;
        }
        if (!_current()) { Done(SaveAsChangedText, FeedbackKind.Information); return false; }
        if (!_available() || IsBusy) { Done("The original message cannot be downloaded now. Nothing was saved.", FeedbackKind.Information); return false; }
        var cancellation = new CancellationTokenSource(); var token = cancellation.Token; _operation = cancellation;
        _feedback.Set("Saving the message as .eml…", FeedbackKind.Progress); RefreshAvailability();
        if (Session is { } running) FocusNavigation.FocusAndReveal(running, _cancel);
        MailWrittenFile? written = null; string? sha256 = null; string? error = null; bool changed = false;
        void Report(int transferred, int total)
        {
            void Update()
            {
                if (IsDisposed || !ReferenceEquals(_operation, cancellation) || token.IsCancellationRequested || !_current()) return;
                _feedback.Set($"Saving the message: {transferred:N0} of {total:N0} bytes.", FeedbackKind.Progress);
            }
            try { if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(Update); else Update(); }
            catch (ObjectDisposedException) { }
        }
        try
        {
            written = await target.WriteNewAsync(async (destination, ct) =>
            {
                // Disposed on every way out (saved, too large, cancelled, refused); that frees its hash, never the destination.
                await using var hashing = new HashingStream(destination, IStreamingMessageSourceExports.MaximumBytes);
                if (_receiver is IStreamingMailSourceReceiver streaming)
                {
                    int size = await streaming.CopySourceAsync(_account, _key, hashing, IStreamingMessageSourceExports.MaximumBytes, Report, ct).ConfigureAwait(false);
                    if (size != hashing.Length) throw new InvalidDataException("The original message arrived with another size.");
                }
                else
                {
                    var source = await _receiver.GetSourceAsync(_account, _key, ct).ConfigureAwait(false);
                    if (source.Key != _key) throw new InvalidDataException("Original source returned another message identity.");
                    await hashing.WriteAsync(source.Bytes, ct).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
                if (!_current() || !_available()) { changed = true; throw new OperationCanceledException(ct); }
                if (hashing.Length == 0) throw new InvalidDataException("The original message is empty.");
                await hashing.FlushAsync(ct).ConfigureAwait(false);
                sha256 = hashing.Finish();
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { error = changed ? SaveAsChangedText : SaveAsCanceledText; }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or MailConnectionException)
        { error = "Message not saved. " + failure.Message; }
        // A file that was published before a late cancellation is kept and reported; a save that failed left nothing.
        bool saved = written is not null && sha256 is not null && error is null;
        if (saved) LastSaved = (written!, sha256!);
        if (IsDisposed) { cancellation.Dispose(); return saved; }
        void Commit()
        {
            if (IsDisposed || !ReferenceEquals(_operation, cancellation)) { cancellation.Dispose(); return; }
            _operation = null; cancellation.Dispose();
            // Availability first: a changed message resets the saved-originals picker, whose text this outcome replaces.
            RefreshAvailability();
            if (saved) _feedback.Set($"Message saved as {written!.DisplayName}. The file was not opened.", FeedbackKind.Success);
            else if (_current() || changed) _feedback.Set(error ?? SaveAsCanceledText, FeedbackKind.Information);
            if (Session is { } currentSession && currentSession.FocusedElement == _cancel && _saveAs.CanFocus) FocusNavigation.FocusAndReveal(currentSession, _saveAs);
        }
        try { if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(Commit); else Commit(); } catch (ObjectDisposedException) { _operation = null; cancellation.Dispose(); }
        return saved;
    }

    /// <summary>
    /// Counts and hashes what passes through it, and refuses to grow past its limit. It owns its hash, which
    /// disposing it frees, but not <paramref name="inner"/>; once disposed it no longer reports that it can write.
    /// </summary>
    private sealed class HashingStream(Stream inner, long maximum) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _disposed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        private long _written;
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Grow(buffer.Length); _hash.AppendData(buffer); inner.Write(buffer);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Grow(buffer.Length); _hash.AppendData(buffer.Span); await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        private void Grow(int count)
        {
            if (count < 0 || _written + count > maximum) throw new IOException("The original message is larger than 12 MiB.");
            _written += count;
        }
        public string Finish() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed) { _disposed = true; _hash.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public async Task ExportAsync()
    {
        if (IsDisposed || IsBusy || _picking || !_current() || !_available()) return;
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
