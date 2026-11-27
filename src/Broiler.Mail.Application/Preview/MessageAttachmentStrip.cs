// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   50
// Annotated:        0/50
// Exempt:           29
// Human-reviewed:   0/50
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       50
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;
using System.Security.Cryptography;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>
/// The open message's attachments, inline above its text: each one's name and size with Save as…, and Save all… for
/// several. Nothing is read, downloaded or opened until the person chooses a save. The bytes come once per strip from
/// <see cref="IMessageAttachmentReader"/> (a verified saved original, else one read-only download), are checked against
/// their length and SHA-256 while they are written, and are published only through the picked location's atomic write,
/// which never replaces a file. Saves never touch the profile's attachment-exports or source-exports folders.
/// The file dialogs a save opens are the documented exemption to the reader's no-modal rule: they open only when the
/// person asks for them.
/// </summary>
public sealed class MessageAttachmentStrip : UiElement
{
    /// <summary>Why the save buttons are disabled where this device has no file picker or no way to read the original.</summary>
    public const string NotAvailableText = "Saving attachments is not available on this device yet.";
    /// <summary>Shown when the original's parts no longer match the list, or the part cannot be extracted on its own.</summary>
    public const string UnsupportedText = "This attachment can't be extracted directly (unsupported or changed MIME structure). Save the original .eml instead.";
    /// <summary>Shown when the original is neither saved nor reachable.</summary>
    public const string OfflineText = "Connect to download this attachment, or save the original .eml while online.";
    /// <summary>Why the saves are disabled where the list is incomplete (more than 32 attachments, or a cut-off MIME
    /// structure): the original cannot give its parts one by one then, so every save would fail.</summary>
    public const string IncompleteText = "Not every attachment could be listed (more than 32, or an incomplete MIME structure), so they can't be saved one by one. Save the original .eml instead.";
    /// <summary>How many rows are listed before the rest move into Show all.</summary>
    public const int ShownRows = 3;

    private readonly AccountProfile? _account;
    private readonly MailMessageKey _key;
    private readonly IMessageAttachmentReader? _reader;
    private readonly IMailFilePicker _files;
    private readonly Func<bool> _current;
    private readonly Action _focusFallback;
    private readonly Action? _revealOriginal;
    private readonly StandardPanel _layout = new() { Spacing = 2 };
    private readonly StandardButton _saveAll = new() { Text = "Save all…" };
    private readonly StandardButton _cancel = new() { Text = "Cancel saving", AccessibleName = "Cancel saving attachments", Visibility = UiVisibility.Collapsed };
    private readonly StandardButton _original = new() { Text = "Original message (.eml)…", Visibility = UiVisibility.Collapsed };
    private readonly StandardToolbar _actions = new() { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8, Visibility = UiVisibility.Collapsed };
    private readonly InlineFeedback _feedback = new();
    private readonly List<StandardButton> _saveButtons = [];
    private readonly ToolbarDisclosure? _more;
    private CancellationTokenSource? _operation;
    private IMailAttachmentSource? _source;
    private bool _writing, _offerOriginal;
    private readonly bool _incomplete;
    private int _generation;

    public MessageAttachmentStrip(AccountProfile? account, MailMessageBody body, IMessageAttachmentReader? reader, IMailFilePicker files,
        Func<bool> current, Action focusFallback, Action? revealOriginal)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(focusFallback);
        if (account is not null && body.Key.AccountId != account.Id) throw new ArgumentException("The attachments belong to another account.", nameof(account));
        (_account, _key, _reader, _files, _current, _focusFallback, _revealOriginal) = (account, body.Key, reader, files, current, focusFallback, revealOriginal);
        Attachments = body.Attachments is { } listed ? Array.AsReadOnly(listed.ToArray()) : [];
        int count = Attachments.Count;
        _incomplete = body.IsAttachmentListIncomplete;
        string plus = _incomplete ? "+" : "";
        string total = MailByteSize.Format(Attachments.Sum(attachment => (long)attachment.DecodedBytes));
        AccessibleName = _layout.AccessibleName = $"Attachments ({count}{plus})";
        Visibility = count > 0 ? UiVisibility.Visible : UiVisibility.Collapsed;

        var header = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8, AccessibleName = "Attachment summary" };
        header.AddChild(new StandardLabel
        {
            Text = $"{count}{plus} attachment{(count == 1 && plus.Length == 0 ? "" : "s")} · {total}",
            AccessibleName = $"{count}{plus} attachment{(count == 1 && plus.Length == 0 ? "" : "s")}, total {total}",
            UseMnemonic = false, Wrapping = UiTextWrapping.Wrap,
        });
        // Save all names, and is offered for, only the parts it writes: an attached container is never saved.
        _saveAll.AccessibleName = $"Save all {Count(SavableCount, "attachment")} to a folder…";
        if (SavableCount > 1) header.AddChild(_saveAll);
        _layout.AddChild(header);

        StandardPanel? rest = null;
        for (int i = 0; i < count; i++)
        {
            var attachment = Attachments[i];
            string size = MailByteSize.Format(attachment.DecodedBytes);
            var row = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8, AccessibleName = attachment.FileName };
            row.AddChild(new StandardLabel { Text = $"{attachment.FileName} · {size}", UseMnemonic = false, Wrapping = UiTextWrapping.Wrap });
            var save = new StandardButton
            {
                Text = "Save as…",
                AccessibleName = $"Save {attachment.FileName} as…",
                ToolTipText = attachment.IsContainer ? "Save the original .eml instead"
                    : CanSaveFiles ? $"Save {attachment.FileName} ({size}) to a location you choose" : Unavailable,
            };
            save.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await SaveAsync(attachment));
            row.AddChild(save);
            _saveButtons.Add(save);
            if (i < ShownRows) _layout.AddChild(row);
            else
            {
                if (rest is null)
                {
                    _more = new ToolbarDisclosure(new StandardButton { Text = $"Show all {count} attachments" });
                    rest = _more.Content;
                }
                rest.AddChild(row);
            }
        }
        if (_more is { } more)
        {
            _layout.AddChild(more);
            var toggleRow = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8 };
            toggleRow.AddChild(more.Button);
            _layout.AddChild(toggleRow);
            // Raised after the disclosure has toggled itself.
            more.Button.Clicked += (_, _) => UpdateDisclosure();
        }
        _actions.AddChild(_cancel);
        _actions.AddChild(_original);
        _layout.AddChild(_actions);
        _layout.AddChild(_feedback);
        AddChild(_layout);
        _saveAll.ToolTipText = CanSaveAll ? "Save every attachment into a folder you choose; existing files are never replaced" : Unavailable;
        // An incomplete list says so up front and offers the original, instead of offering saves that cannot succeed.
        if (_incomplete && count > 0)
        {
            _feedback.Set(IncompleteText, FeedbackKind.Information);
            _offerOriginal = true;
        }
        _saveAll.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await SaveAllAsync());
        _cancel.Clicked += (_, _) => Cancel();
        _original.Clicked += (_, _) => _revealOriginal?.Invoke();
        UpdateControls();
    }

    /// <summary>Raised after the strip's state changes, such as when a save starts or ends.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>The attachments the reader listed for this message, in order.</summary>
    public IReadOnlyList<MailAttachmentDescriptor> Attachments { get; }

    /// <summary>Whether a save runs, including while its file dialog is open.</summary>
    public bool IsBusy => _operation is not null;

    public string Status => _feedback.Message;

    /// <summary>Whether this device can save at least one of the attachments: a file picker, the original and a part to save.</summary>
    public bool CanSave => CanSaveFiles && Attachments.Any(attachment => !attachment.IsContainer);

    /// <summary>How many of the listed attachments a save can write: every one but an attached container.</summary>
    public int SavableCount => Attachments.Count(attachment => !attachment.IsContainer);

    // Without a complete list the original's parts are never extracted, so no save is offered.
    private bool CanRead => _reader is not null && _account is not null && !_incomplete;
    private string Unavailable => _incomplete ? IncompleteText : NotAvailableText;
    private bool CanSaveFiles => CanRead && (_files.Capabilities & MailFilePickerCapabilities.SaveFile) != 0;
    private bool CanSaveAll => CanRead && (_files.Capabilities & MailFilePickerCapabilities.PickDirectory) != 0 && Attachments.Any(attachment => !attachment.IsContainer);

    /// <summary>Cancels the running save. A file being written is removed; files already saved stay.</summary>
    public void Cancel()
    {
        if (!IsDisposed) _operation?.Cancel();
    }

    /// <summary>Moves focus to the first Save button that can be used, as the reader's More message actions asks.</summary>
    public void FocusFirst()
    {
        if (IsDisposed || Session is not { } session) return;
        UiElement? target = _saveButtons.FirstOrDefault(button => button.IsEnabled) ?? (_saveAll.Parent is not null && _saveAll.IsEnabled ? _saveAll : null);
        target ??= _original.Visibility == UiVisibility.Visible && _original.IsEnabled ? _original : null;
        target ??= _more?.Button;
        if (target is null) return;
        if (_more is { } more && target.IsDescendantOf(more)) { more.Expand(); UpdateDisclosure(); }
        session.RenderFrame();
        if (target.CanFocus) FocusNavigation.FocusAndReveal(session, target);
    }

    /// <summary>Cancels a save of a message that is no longer the one shown, and keeps the buttons current.</summary>
    public void RefreshContext()
    {
        if (IsDisposed) return;
        if (!_current())
        {
            Interlocked.Increment(ref _generation);
            _operation?.Cancel();
        }
        UpdateControls();
    }

    /// <summary>Asks where to save <paramref name="attachment"/>, then writes its exact decoded bytes there.</summary>
    public async Task SaveAsync(MailAttachmentDescriptor attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        int index = IndexOf(attachment);
        if (IsDisposed || IsBusy || index < 0 || attachment.IsContainer || !CanSaveFiles || !_current()) return;
        var trigger = _saveButtons[index];
        var (generation, cancellation) = Begin();
        var token = cancellation.Token;
        Outcome? outcome = null;
        bool pickCanceled = false;
        try
        {
            // A source already read that cannot give this part says so before any dialog opens.
            if (_source is { } known && Match(known, attachment) is null) throw new UnsupportedAttachmentException();
            var pick = await _files.PickSaveFileAsync(new MailFileRequest(MailFilePurpose.SaveAttachment, "Save attachment")
            {
                SuggestedFileName = attachment.FileName,
                Types = TypesFor(attachment.FileName),
                AcceptText = "Save",
            }, token);
            token.ThrowIfCancellationRequested();
            if (pick.Status == MailFilePickStatus.Canceled) { pickCanceled = true; return; }
            if (pick.Status != MailFilePickStatus.Picked || pick.Item is not { } target)
            {
                outcome = new(pick.Message ?? "No file was chosen.", FeedbackKind.Warning);
                return;
            }
            StartWriting($"Saving {attachment.FileName}…");
            var source = await OpenSourceAsync(generation, cancellation, token);
            var received = Match(source, attachment) ?? throw new UnsupportedAttachmentException();
            var written = await target.WriteNewAsync((stream, t) => CopyVerifiedAsync(source, received, stream, generation, t), token);
            outcome = new($"Saved {written.DisplayName} ({MailByteSize.Format(received.Bytes)}). The file was not opened.", FeedbackKind.Success);
        }
        catch (Exception error) when (IsCancellation(error, cancellation)) { outcome = new("Saving canceled. No partial file was kept.", FeedbackKind.Information); }
        catch (Exception error) when (Describe(error) is { } described) { outcome = described; }
        finally { Complete(cancellation, generation, outcome, trigger, pickCanceled); }
    }

    /// <summary>Asks for a folder, then saves every attachment into it under unique names, stopping at the first failure.</summary>
    public async Task SaveAllAsync()
    {
        if (IsDisposed || IsBusy || !CanSaveAll || !_current()) return;
        var savable = Attachments.Where(attachment => !attachment.IsContainer).ToArray();
        var (generation, cancellation) = Begin();
        var token = cancellation.Token;
        Outcome? outcome = null;
        bool pickCanceled = false;
        int saved = 0;
        string? currentName = null;
        bool writing = false;
        try
        {
            if (_source is { } known && savable.Any(attachment => Match(known, attachment) is null)) throw new UnsupportedAttachmentException();
            var pick = await _files.PickDirectoryAsync(new MailFileRequest(MailFilePurpose.SaveAttachment, "Save all attachments") { AcceptText = "Save here" }, token);
            token.ThrowIfCancellationRequested();
            if (pick.Status == MailFilePickStatus.Canceled) { pickCanceled = true; return; }
            if (pick.Status != MailFilePickStatus.Picked || pick.Item is not { } folder)
            {
                outcome = new(pick.Message ?? "No folder was chosen.", FeedbackKind.Warning);
                return;
            }
            StartWriting($"Saving {savable.Length} attachments…");
            var source = await OpenSourceAsync(generation, cancellation, token);
            var parts = savable.Select(attachment => (Attachment: attachment, Received: Match(source, attachment))).ToArray();
            if (parts.Any(part => part.Received is null)) throw new UnsupportedAttachmentException();
            int renamed = 0;
            writing = true;
            foreach (var (attachment, received) in parts)
            {
                currentName = attachment.FileName;
                if (!IsDisposed) _feedback.Set($"Saving {saved + 1} of {parts.Length}: {attachment.FileName}…", FeedbackKind.Progress);
                var written = await folder.WriteNewFileAsync(attachment.FileName, (stream, t) => CopyVerifiedAsync(source, received!, stream, generation, t), token);
                saved++;
                if (!string.Equals(written.DisplayName, attachment.FileName, StringComparison.Ordinal)) renamed++;
            }
            outcome = new($"Saved all {Count(parts.Length, "attachment")} to {folder.DisplayName}." +
                (renamed > 0 ? $" {renamed} renamed to avoid replacing existing files." : "") + " The files were not opened.", FeedbackKind.Success);
        }
        catch (Exception error) when (IsCancellation(error, cancellation))
        {
            outcome = new(writing ? $"Canceled after saving {saved} of {savable.Length}. Saved files remain; the unfinished file was removed." : "Saving canceled. No partial file was kept.",
                FeedbackKind.Information);
        }
        catch (Exception error) when (Describe(error) is { } described)
        {
            outcome = writing && currentName is not null ? described with { Message = $"Saved {saved} of {savable.Length}. {currentName}: {described.Message}" } : described;
        }
        finally { Complete(cancellation, generation, outcome, _saveAll, pickCanceled); }
    }

    private int IndexOf(MailAttachmentDescriptor attachment)
    {
        for (int i = 0; i < Attachments.Count; i++) if (ReferenceEquals(Attachments[i], attachment)) return i;
        for (int i = 0; i < Attachments.Count; i++) if (Attachments[i] == attachment) return i;
        return -1;
    }

    private (int Generation, CancellationTokenSource Cancellation) Begin()
    {
        var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        _offerOriginal = false;
        UpdateControls();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        return (Volatile.Read(ref _generation), cancellation);
    }

    private void StartWriting(string progress)
    {
        if (IsDisposed) return;
        bool focusInside = Session?.FocusedElement is { } focused && (focused == this || focused.IsDescendantOf(this));
        _writing = true;
        _feedback.Set(progress, FeedbackKind.Progress);
        UpdateControls();
        if (focusInside && Session is { } session) FocusNavigation.FocusAndReveal(session, _cancel);
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Complete(CancellationTokenSource cancellation, int generation, Outcome? outcome, StandardButton trigger, bool pickCanceled)
    {
        if (!ReferenceEquals(_operation, cancellation)) { cancellation.Dispose(); return; }
        _operation = null; _writing = false; cancellation.Dispose();
        if (IsDisposed) { DropSource(); return; }
        var focused = Session?.FocusedElement;
        bool restoreFocus = pickCanceled || focused is null || focused == _cancel || (focused.IsDescendantOf(this) && !focused.CanFocus);
        if (generation == Volatile.Read(ref _generation) && _current() && outcome is { } shown)
        {
            _feedback.Set(shown.Message, shown.Kind);
            _offerOriginal = shown.OfferOriginal;
        }
        UpdateControls();
        if (restoreFocus && Session is { } session && (pickCanceled || focused is not null))
        {
            if (trigger.CanFocus) session.SetFocus(trigger);
            else if (focused == _cancel || focused?.IsDescendantOf(this) == true)
            {
                if (_original.CanFocus) session.SetFocus(_original);
                else if (_saveButtons.FirstOrDefault(button => button.CanFocus) is { } first) session.SetFocus(first);
                else _focusFallback();
            }
        }
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IMailAttachmentSource> OpenSourceAsync(int generation, CancellationTokenSource cancellation, CancellationToken token)
    {
        if (_source is { } known) return known;
        if (_reader is null || _account is null) throw new OpenFailureException(NotAvailableText, false);
        if (!IsDisposed) _feedback.Set("Reading the original message…", FeedbackKind.Progress);
        IMailAttachmentSource opened;
        try { opened = await _reader.OpenAsync(_account, _key, Report, token); }
        catch (Exception error) when (error is not OperationCanceledException && !token.IsCancellationRequested)
        {
            throw error switch
            {
                InvalidDataException => new OpenFailureException(UnsupportedText, true),
                InvalidOperationException => new OpenFailureException(OfflineText, true),
                MailConnectionException { Failure: MailConnectionFailure.Unreachable or MailConnectionFailure.Setup } => new OpenFailureException(OfflineText, true),
                MailConnectionException connection => new OpenFailureException("Download did not finish. " + connection.Message, false),
                IOException or UnauthorizedAccessException or ArgumentException => new OpenFailureException("Download did not finish. " + error.Message, false),
                _ => error,
            };
        }
        if (IsDisposed || token.IsCancellationRequested || generation != Volatile.Read(ref _generation))
        {
            opened.Dispose();
            throw new OperationCanceledException(token);
        }
        if (opened.Key != _key) { opened.Dispose(); throw new OpenFailureException(UnsupportedText, true); }
        _source = opened;
        return opened;

        void Report(int transferred, int total)
        {
            void Show()
            {
                if (IsDisposed || !ReferenceEquals(_operation, cancellation) || token.IsCancellationRequested || !_current()) return;
                _feedback.Set(string.Create(CultureInfo.InvariantCulture, $"Downloading original message: {transferred:N0} of {total:N0} bytes…"), FeedbackKind.Progress);
            }
            try { if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(Show); else Show(); }
            catch (ObjectDisposedException) { }
        }
    }

    // The part the reader listed, in the original now read: the same part number, media type, name and decoded size.
    private static ReceivedAttachment? Match(IMailAttachmentSource source, MailAttachmentDescriptor attachment) =>
        attachment.IsContainer ? null : source.Attachments.FirstOrDefault(received => received.Key == source.Key &&
            received.PartNumber == attachment.PartNumber &&
            string.Equals(received.MediaType, attachment.MediaType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(received.FileName, attachment.FileName, StringComparison.Ordinal) &&
            received.Bytes == attachment.DecodedBytes);

    private async Task CopyVerifiedAsync(IMailAttachmentSource source, ReceivedAttachment received, Stream destination, int generation, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var hashing = new HashingStream(destination, hash);
        await source.CopyToAsync(received, hashing, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (hashing.Written != received.Bytes ||
            !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), received.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved bytes did not verify.");
        // A message changed while the bytes were written is never published.
        if (generation != Volatile.Read(ref _generation) || IsDisposed) throw new OperationCanceledException(token);
    }

    private static bool IsCancellation(Exception error, CancellationTokenSource cancellation) =>
        error is OperationCanceledException || (cancellation.IsCancellationRequested && error is ObjectDisposedException or IOException);

    private static Outcome? Describe(Exception error) => error switch
    {
        OpenFailureException open => new(open.Message, open.OfferOriginal ? FeedbackKind.Warning : FeedbackKind.Error, open.OfferOriginal),
        UnsupportedAttachmentException => new(UnsupportedText, FeedbackKind.Warning, true),
        MailConnectionException connection => new("Download did not finish. " + connection.Message, FeedbackKind.Error),
        InvalidDataException => new("Saved bytes did not verify; nothing was kept.", FeedbackKind.Error),
        IOException or UnauthorizedAccessException => new("Could not write the file. " + error.Message + " Choose another location.", FeedbackKind.Error),
        ArgumentException or InvalidOperationException => new("Saving did not finish. " + error.Message, FeedbackKind.Error),
        _ => null,
    };

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // The type list of a save: the attachment's own extension first, then every file.
    private static IReadOnlyList<MailFileType> TypesFor(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        if (extension.Length is < 2 or > 16 || !extension.Skip(1).All(char.IsAsciiLetterOrDigit)) return [MailFileType.AllFiles];
        if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase)) return [MailFileType.Email, MailFileType.AllFiles];
        return [new MailFileType($"{extension[1..].ToUpperInvariant()} file (*{extension})", "*" + extension, extension), MailFileType.AllFiles];
    }

    private void UpdateDisclosure()
    {
        if (_more is { } more) more.Button.Text = more.IsExpanded ? "Show fewer attachments" : $"Show all {Attachments.Count} attachments";
    }

    private void UpdateControls()
    {
        if (IsDisposed) return;
        bool current = _current();
        // While a file dialog is open the buttons stay enabled, so the dialog can give focus back to the one that
        // opened it; a second save cannot start while one runs.
        bool canStart = current && !_writing;
        bool canFiles = CanSaveFiles;
        for (int i = 0; i < _saveButtons.Count; i++)
            _saveButtons[i].IsEnabled = canFiles && !Attachments[i].IsContainer && canStart;
        _saveAll.IsEnabled = CanSaveAll && canStart;
        _cancel.Visibility = _writing ? UiVisibility.Visible : UiVisibility.Collapsed;
        _cancel.IsEnabled = _writing;
        _original.Visibility = _offerOriginal && _revealOriginal is not null && !_writing ? UiVisibility.Visible : UiVisibility.Collapsed;
        _original.IsEnabled = current;
        _actions.Visibility = _cancel.Visibility == UiVisibility.Visible || _original.Visibility == UiVisibility.Visible ? UiVisibility.Visible : UiVisibility.Collapsed;
    }

    private void DropSource()
    {
        _source?.Dispose();
        _source = null;
    }

    protected override BSize MeasureCore(BSize availableSize) => _layout.Measure(availableSize);
    protected override void ArrangeCore(BRect finalRect) => _layout.Arrange(finalRect);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _generation);
            _operation?.Cancel();
            // A running save drops the original when it ends; otherwise it goes now.
            if (_operation is null) DropSource();
        }
        base.Dispose(disposing);
    }

    private sealed record Outcome(string Message, FeedbackKind Kind, bool OfferOriginal = false);

    private sealed class OpenFailureException(string message, bool offerOriginal) : Exception(message)
    {
        public bool OfferOriginal { get; } = offerOriginal;
    }

    private sealed class UnsupportedAttachmentException() : Exception(UnsupportedText);

    /// <summary>Writes through to the destination while hashing and counting every byte.</summary>
    private sealed class HashingStream(Stream inner, IncrementalHash hash) : Stream
    {
        public long Written { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            hash.AppendData(buffer);
            Written += buffer.Length;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer.Span);
            Written += buffer.Length;
        }
    }
}
