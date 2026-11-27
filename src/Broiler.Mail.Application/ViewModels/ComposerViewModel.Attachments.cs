// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   31
// Annotated:        1/31
// Exempt:           4
// Human-reviewed:   0/31
// IP risk:          Low
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  6/10 max
// Unverified:       31
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI.Forms;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class ComposerViewModel
{
    /// <summary>How much of a picked file is read at a time.</summary>
    internal const int AttachmentReadChunkBytes = 256 * 1024;

    private CancellationTokenSource? _attachmentRead;
    public IReadOnlyList<DraftAttachment> Attachments => _seed?.Attachments ?? [];
    public bool IsAddingAttachment => _attachmentRead is not null;
    /// <summary>What the running read has done, such as "Reading report.pdf: 262,144 of 1,258,291 bytes."</summary>
    public string AttachmentReadProgress { get; private set; } = "Reading attachment…";
    public bool CanAddAttachment => CanEdit && Attachments.Count < DraftAttachment.MaximumCount && AttachmentRoom > 0;

    /// <summary>The bytes the open draft's attachments take.</summary>
    public int AttachmentBytes => Attachments.Sum(part => part.Content.Length);

    /// <summary>How many more bytes the open draft can take: its own 8 MiB, or less when saved drafts use the profile quota.</summary>
    public int AttachmentRoomBytes => HasDraft ? Math.Max(0, AttachmentRoom) : 0;

    /// <summary>The quota line, such as "1.2 / 8.0 MiB · 1/4 files", with what saved drafts leave when that is less.</summary>
    public string AttachmentQuotaText
    {
        get
        {
            int bytes = AttachmentBytes;
            string text = $"{MailByteSize.Mebibytes(bytes)} / {MailByteSize.Mebibytes(DraftAttachment.MaximumBytes)} MiB · " +
                $"{Attachments.Count}/{DraftAttachment.MaximumCount} files";
            int profileRoom = Math.Max(0, ProfileAttachmentRoom);
            return profileRoom < DraftAttachment.MaximumBytes - bytes ? $"{text} · {MailByteSize.Format(profileRoom)} left for saved drafts" : text;
        }
    }

    /// <summary>Why no file can be added to the editable draft, or null when one can (or there is nothing to edit).</summary>
    public string? AttachmentLimitReason =>
        !CanEdit ? null
        : Attachments.Count >= DraftAttachment.MaximumCount ? $"This draft has {DraftAttachment.MaximumCount} files, the most a draft can have. Remove one to attach another."
        : DraftAttachment.MaximumBytes - AttachmentBytes <= 0 ? "This draft's attachments use all of its 8 MiB. Remove one to attach another."
        : ProfileAttachmentRoom <= 0 ? "Saved drafts use all of the 16 MiB attachment quota. Remove attachments from a saved draft to attach more."
        : null;

    private int AttachmentRoom => Math.Min(DraftAttachment.MaximumBytes - AttachmentBytes, ProfileAttachmentRoom);

    private int ProfileAttachmentRoom
    {
        get
        {
            int savedBytes = _savedDrafts.Sum(row => row.Draft.Attachments?.Sum(part => part.Content.Length) ?? 0);
            return DraftAttachment.MaximumProfileBytes - savedBytes - AttachmentBytes;
        }
    }

    /// <summary>
    /// Copies a typed file path into the draft, for devices without a file picker. The path must be fully qualified
    /// and name a regular local file: not a network share, folder, link or device.
    /// </summary>
    public Task AddAttachmentAsync(string path)
    {
        if (!CanAddAttachment || _seed is null) return Task.CompletedTask;
        if (!IsLocalRegularFile(path))
        {
            string name = DraftAttachment.SafeName(path?.Trim());
            Refuse($"Could not add {name}. Type the full path of a regular file on this device.");
            return Task.CompletedTask;
        }
        return AddAttachmentAsync(new LocalMailFile(path));
    }

    /// <summary>
    /// Copies a picked file into the draft. A file larger than the room left, or empty, is refused before it is read;
    /// otherwise it is read in chunks, never more than one byte past the room left, and added under a safe name.
    /// The draft is unchanged when reading fails, is canceled, or the draft changes meanwhile.
    /// </summary>
    public async Task AddAttachmentAsync(IMailReadableFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!CanAddAttachment || _seed is not { } draft) return;
        int remaining = AttachmentRoom;
        string name = DraftAttachment.SafeName(file.DisplayName);
        long? length;
        try { length = file.Length; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException) { length = null; }
        if (length > remaining) { Refuse(TooLarge(name, MailByteSize.Format(length.Value), remaining)); return; }
        if (length == 0) { Refuse($"{name} is empty."); return; }

        using var cancellation = new CancellationTokenSource();
        _attachmentRead = cancellation;
        AttachmentReadProgress = $"Reading {name}…";
        IsBusy = true;
        Notify();

        DraftAttachment? part = null;
        string? error = null;
        try
        {
            part = await Task.Run(() => ReadAttachmentAsync(file, name, length, remaining, Report, cancellation.Token)).ConfigureAwait(false);
            DraftAttachment.Validate([.. (draft.Attachments ?? []), part]);
        }
        catch (OperationCanceledException) { error = "Attachment not added; reading was canceled."; }
        catch (AttachmentRefusedException refused) { error = refused.Message; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { error = $"Could not read {name}. Check that the file exists and you can open it."; }

        try
        {
            await OnUiAsync(() =>
            {
                _attachmentRead = null;
                IsBusy = false;
                if (_disposed) return;
                if (cancellation.IsCancellationRequested) error = "Attachment not added; reading was canceled.";
                if (_profileStorageBusy || !ReferenceEquals(_seed, draft)) error = "The draft context changed while reading the attachment.";
                if (part is not null && error is null)
                {
                    _seed = draft with { Attachments = Array.AsReadOnly<DraftAttachment>([.. Attachments, part]) };
                    SubmissionState = DraftSubmissionState.Editing;
                    _sendConfirmationPending = null;
                    Persist();
                }
                Status = error ?? $"{part!.FileName} added to the draft.";
                StatusKind = error is null ? FeedbackKind.Information : FeedbackKind.Warning;
                Notify();
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed) { }

        void Report(long read, long? total)
        {
            try
            {
                _dispatcher.Post(() =>
                {
                    if (_disposed || !ReferenceEquals(_attachmentRead, cancellation) || cancellation.IsCancellationRequested) return;
                    AttachmentReadProgress = total is { } known
                        ? string.Format(CultureInfo.InvariantCulture, "Reading {0}: {1:N0} of {2:N0} bytes.", name, read, known)
                        : string.Format(CultureInfo.InvariantCulture, "Reading {0}: {1:N0} bytes.", name, read);
                    Notify();
                });
            }
            catch (ObjectDisposedException) { }
        }
    }

    public void CancelAttachment() => _attachmentRead?.Cancel();

    /// <summary>Removes <paramref name="attachment"/>, this very entry of the editable draft, and saves the draft.</summary>
    public void RemoveAttachment(DraftAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (!CanEdit || _seed is not { } draft) return;
        var parts = Attachments;
        int index = -1;
        for (int position = 0; position < parts.Count; position++)
            if (ReferenceEquals(parts[position], attachment)) { index = position; break; }
        if (index < 0) return;
        var remaining = parts.Where((_, position) => position != index).ToArray();
        _seed = draft with { Attachments = remaining.Length == 0 ? null : Array.AsReadOnly(remaining) };
        SubmissionState = DraftSubmissionState.Editing;
        _sendConfirmationPending = null;
        Status = $"{attachment.FileName} removed from the draft.";
        StatusKind = FeedbackKind.Information;
        Persist();
        Notify();
    }

    public void RemoveAttachment(int index)
    {
        if (index >= 0 && index < Attachments.Count) RemoveAttachment(Attachments[index]);
    }

    /// <summary>
    /// The files of the message a forward started from, offered for inclusion. Never persisted: parking, resuming or
    /// discarding the draft, or starting another one, ends it. Containers (multipart parts) are never offered.
    /// </summary>
    public sealed record ForwardAttachmentOffer(Guid DraftId, MailMessageKey Source, IReadOnlyList<MailAttachmentDescriptor> Parts)
    {
        /// <summary>The address of the account the original was read from, named when another account is selected.</summary>
        public string AccountName { get; init; } = "";

        /// <summary>The decoded size of every offered part together.</summary>
        public long TotalBytes => Parts.Sum(part => (long)part.DecodedBytes);

        /// <summary>Such as "Original message has 1 attachment (2.4 MiB)."</summary>
        public string Text => Parts.Count == 1
            ? $"Original message has 1 attachment ({MailByteSize.Format(TotalBytes)})."
            : $"Original message has {Parts.Count} attachments ({MailByteSize.Format(TotalBytes)}).";
    }

    /// <summary>The text shown when the original no longer holds the parts the forward listed.</summary>
    public const string ForwardAttachmentsChangedText = "The original message changed. Open it again to forward its attachments.";

    private ForwardAttachmentOffer? _forwardOffer;

    /// <summary>The pending offer of the forwarded message's files, while its draft is open and editable; otherwise null.</summary>
    public ForwardAttachmentOffer? ForwardAttachments => _forwardOffer is { } offer && offer.DraftId == _seed?.Id && CanEdit ? offer : null;

    /// <summary>Whether Include original attachments can run now; <see cref="ForwardAttachmentsUnavailableReason"/> says why not.</summary>
    public bool CanIncludeForwardAttachments => ForwardAttachments is { } offer && AttachmentReader is not null && !IsBusy &&
        _account is { IsEnabled: true } account && account.Id == offer.Source.AccountId &&
        Attachments.Count + offer.Parts.Count <= DraftAttachment.MaximumCount && offer.TotalBytes <= AttachmentRoomBytes;

    /// <summary>The first rule that keeps the offered files out of the draft, or null when they fit (or nothing is offered).</summary>
    public string? ForwardAttachmentsUnavailableReason
    {
        get
        {
            if (ForwardAttachments is not { } offer) return null;
            if (Attachments.Count + offer.Parts.Count > DraftAttachment.MaximumCount)
                return $"Including them would exceed {DraftAttachment.MaximumCount} files per draft ({Attachments.Count} + {offer.Parts.Count}).";
            if (offer.TotalBytes > AttachmentRoomBytes)
                return $"They need {MailByteSize.Format(offer.TotalBytes)}; this draft has {MailByteSize.Format(AttachmentRoomBytes)} left.";
            if (AttachmentReader is null) return "Original attachments can't be included in this mode.";
            if (_account is not { IsEnabled: true } account || account.Id != offer.Source.AccountId)
                return $"Switch back to {(offer.AccountName.Length > 0 ? offer.AccountName : "the original account")} to include them.";
            return null;
        }
    }

    /// <summary>
    /// Reads the forwarded message's offered files and adds them all, or none: every part must still be in the
    /// original with the same part number, media type and size, and together they must fit the draft. Cancel and
    /// failures keep the offer; success ends it.
    /// </summary>
    // Hairs-AI:           Origin=AI; IP=Low; Security=Medium; Resources=6; Fingerprint=946488
    // Hairs-Falsified-If: a forward adds only some of the offered parts, or adds parts whose bytes differ in part number, media type or size from the offer
    // Hairs-Human:        PENDING
    public async Task IncludeForwardAttachmentsAsync()
    {
        if (!CanIncludeForwardAttachments || ForwardAttachments is not { } offer || _seed is not { } draft) return;
        var account = _account!;
        var reader = AttachmentReader!;
        using var cancellation = new CancellationTokenSource();
        _attachmentRead = cancellation;
        AttachmentReadProgress = "Downloading original attachments…";
        IsBusy = true;
        Notify();

        DraftAttachment[]? parts = null;
        string? error = null;
        try
        {
            parts = await Task.Run(async () =>
            {
                using var source = await reader.OpenAsync(account, offer.Source, Report, cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                if (source.Key != offer.Source) throw new ForwardChangedException();
                var included = new DraftAttachment[offer.Parts.Count];
                for (int index = 0; index < included.Length; index++)
                {
                    var wanted = offer.Parts[index];
                    var received = source.Attachments.FirstOrDefault(item => item.Key == source.Key && item.PartNumber == wanted.PartNumber &&
                        string.Equals(item.MediaType, wanted.MediaType, StringComparison.OrdinalIgnoreCase) && item.Bytes == wanted.DecodedBytes)
                        ?? throw new ForwardChangedException();
                    var bytes = source.Read(received);
                    if (bytes.Length != wanted.DecodedBytes) throw new ForwardChangedException();
                    included[index] = new(DraftAttachment.SafeName(wanted.FileName), bytes.ToArray());
                }
                return included;
            }, cancellation.Token).ConfigureAwait(false);
            DraftAttachment.Validate([.. (draft.Attachments ?? []), .. parts]);
        }
        catch (OperationCanceledException) { error = "Original attachments not included; downloading was canceled."; }
        catch (ForwardChangedException) { error = ForwardAttachmentsChangedText; }
        catch (InvalidDataException) { error = ForwardAttachmentsChangedText; }
        catch (MailConnectionException offline) when (offline.Failure is MailConnectionFailure.Unreachable or MailConnectionFailure.Setup)
        { error = "Connect to download the original attachments, then try again."; }
        catch (MailConnectionException connection) { error = "Download did not finish. " + connection.Message; }
        catch (InvalidOperationException) { error = "Connect to download the original attachments, then try again."; }
        catch (ArgumentException refused) when (parts is not null) { error = "The original attachments don't fit this draft. " + refused.Message; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        { error = "Download did not finish. " + failure.Message; }

        try
        {
            await OnUiAsync(() =>
            {
                _attachmentRead = null;
                IsBusy = false;
                if (_disposed) return;
                if (cancellation.IsCancellationRequested) error = "Original attachments not included; downloading was canceled.";
                if (_profileStorageBusy || !ReferenceEquals(_seed, draft) || !ReferenceEquals(_forwardOffer, offer))
                    error = "The draft context changed while downloading the original attachments.";
                else if (error is null && parts is not null && parts.Sum(part => part.Content.Length) > AttachmentRoom)
                    error = $"They need {MailByteSize.Format(offer.TotalBytes)}; this draft has {MailByteSize.Format(AttachmentRoomBytes)} left.";
                if (parts is not null && error is null)
                {
                    _seed = draft with { Attachments = Array.AsReadOnly<DraftAttachment>([.. Attachments, .. parts]) };
                    _forwardOffer = null;
                    SubmissionState = DraftSubmissionState.Editing;
                    _sendConfirmationPending = null;
                    Persist();
                }
                Status = error ?? (parts!.Length == 1
                    ? $"Included 1 original attachment. {AttachmentQuotaText}"
                    : $"Included {parts.Length} original attachments. {AttachmentQuotaText}");
                StatusKind = error is null ? FeedbackKind.Information : FeedbackKind.Warning;
                Notify();
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed) { }

        void Report(int read, int total)
        {
            try
            {
                _dispatcher.Post(() =>
                {
                    if (_disposed || !ReferenceEquals(_attachmentRead, cancellation) || cancellation.IsCancellationRequested) return;
                    AttachmentReadProgress = string.Format(CultureInfo.InvariantCulture, "Downloading original attachments: {0:N0} of {1:N0} bytes.", read, total);
                    Notify();
                });
            }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Hides the forward offer; the draft keeps only the files already in it.</summary>
    public void DismissForwardAttachments()
    {
        if (_forwardOffer is null) return;
        _forwardOffer = null;
        Notify();
    }

    private sealed class ForwardChangedException : Exception;

    /// <summary>
    /// Whether a typed path names a regular file on this device: fully qualified, not a network share, and not a folder,
    /// reparse point (link) or device. Never throws.
    /// </summary>
    internal static bool IsLocalRegularFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        if (OperatingSystem.IsWindows() && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))) return false;
        if (Path.GetFileName(path) is not { Length: > 0 } name || name.Contains(':')) return false;
        try
        {
            return File.Exists(path) &&
                (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private void Refuse(string message)
    {
        Status = message;
        StatusKind = FeedbackKind.Warning;
        Notify();
    }

    private static string TooLarge(string name, string size, int room) =>
        $"{name} is {size}; this draft has {MailByteSize.Format(Math.Max(0, room))} left (8 MiB per draft).";

    private static async Task<DraftAttachment> ReadAttachmentAsync(IMailReadableFile file, string name, long? length, int remaining,
        Action<long, long?> report, CancellationToken token)
    {
        await using var stream = await file.OpenReadAsync(token).ConfigureAwait(false);
        // Never more than one byte past the room left: that byte only tells that the file is too large.
        int limit = remaining + 1;
        var bytes = new byte[length is { } known ? (int)Math.Min(known, limit) : Math.Min(AttachmentReadChunkBytes, limit)];
        int read = 0;
        while (read < limit)
        {
            token.ThrowIfCancellationRequested();
            if (read == bytes.Length)
            {
                if (length is not null)
                {
                    // A file of known length is read into a buffer of exactly that size; one more byte means it grew.
                    read += await stream.ReadAsync(new byte[1], token).ConfigureAwait(false);
                    break;
                }
                Array.Resize(ref bytes, (int)Math.Min((long)bytes.Length * 2, limit));
            }
            int count = await stream.ReadAsync(bytes.AsMemory(read, Math.Min(AttachmentReadChunkBytes, bytes.Length - read)), token).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
            report(read, length);
        }
        if (read > remaining)
            throw new AttachmentRefusedException(length is null
                ? $"{name} is larger than the {MailByteSize.Format(remaining)} this draft has left (8 MiB per draft)."
                : TooLarge(name, $"more than {MailByteSize.Format(remaining)}", remaining));
        if (read == 0) throw new AttachmentRefusedException($"{name} is empty.");
        // A file that changed size while it was read is not copied half-way.
        if (length is { } expected && read != expected) throw new IOException("The file changed while it was read.");
        return new(name, read == bytes.Length ? bytes : bytes.AsMemory(0, read).ToArray());
    }

    private sealed class AttachmentRefusedException(string message) : Exception(message);
}
