using Broiler.Mail.Core.Messages;
using Broiler.UI.Forms;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class ComposerViewModel
{
    private CancellationTokenSource? _attachmentRead;
    public IReadOnlyList<DraftAttachment> Attachments => _seed?.Attachments ?? [];
    public bool IsAddingAttachment => _attachmentRead is not null;
    public string AttachmentReadProgress { get; private set; } = "Reading attachment…";
    public bool CanAddAttachment => CanEdit && Attachments.Count < DraftAttachment.MaximumCount && AttachmentRoom > 0;

    private int AttachmentRoom
    {
        get
        {
            int activeBytes = Attachments.Sum(part => part.Content.Length);
            int savedBytes = _savedDrafts.Sum(row => row.Draft.Attachments?.Sum(part => part.Content.Length) ?? 0);
            return Math.Min(DraftAttachment.MaximumBytes - activeBytes, DraftAttachment.MaximumProfileBytes - savedBytes - activeBytes);
        }
    }

    public async Task AddAttachmentAsync(string path)
    {
        if (!CanAddAttachment || _seed is not { } draft) return;
        int remaining = AttachmentRoom;
        using var cancellation = new CancellationTokenSource();
        _attachmentRead = cancellation;
        AttachmentReadProgress = "Reading attachment…";
        IsBusy = true;
        Notify();

        DraftAttachment? part = null;
        string? error = null;
        try
        {
            part = await Task.Run(() => ReadAttachmentAsync(path, remaining, Report, cancellation.Token)).ConfigureAwait(false);
            DraftAttachment.Validate([.. (draft.Attachments ?? []), part]);
        }
        catch (OperationCanceledException) { error = "Attachment read canceled."; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { error = "Could not add this file. Use an absolute path to a readable, non-empty regular file within the attachment quota."; }

        try
        {
            await OnUiAsync(() =>
            {
                _attachmentRead = null;
                IsBusy = false;
                if (_disposed) return;
                if (cancellation.IsCancellationRequested) error = "Attachment read canceled.";
                if (_profileStorageBusy || !ReferenceEquals(_seed, draft)) error = "The draft context changed while reading the attachment.";
                if (part is not null && error is null)
                {
                    _seed = draft with { Attachments = Array.AsReadOnly<DraftAttachment>([.. Attachments, part]) };
                    SubmissionState = DraftSubmissionState.Editing;
                    Persist();
                }
                Status = error ?? "Attachment added to the draft.";
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
                    AttachmentReadProgress = $"Reading attachment: {read:N0} of {total:N0} bytes.";
                    Notify();
                });
            }
            catch (ObjectDisposedException) { }
        }
    }

    public void CancelAttachment() => _attachmentRead?.Cancel();

    public void RemoveAttachment(int index)
    {
        if (!CanEdit || _seed is not { } draft || index < 0 || index >= Attachments.Count) return;
        var remaining = Attachments.Where((_, position) => position != index).ToArray();
        _seed = draft with { Attachments = remaining.Length == 0 ? null : Array.AsReadOnly(remaining) };
        SubmissionState = DraftSubmissionState.Editing;
        Status = "Attachment removed from the draft.";
        StatusKind = FeedbackKind.Information;
        Persist();
        Notify();
    }

    private static async Task<DraftAttachment> ReadAttachmentAsync(string path, int remaining, Action<int, int> report, CancellationToken token)
    {
        string name = Path.GetFileName(path);
        if (!Path.IsPathFullyQualified(path) || OperatingSystem.IsWindows() && path.StartsWith("\\\\", StringComparison.Ordinal) ||
            name.Contains(':') || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new ArgumentException("Choose an absolute regular file path.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long size = file.Length;
        if (size <= 0 || size > remaining) throw new ArgumentException("Attachment exceeds the remaining quota or is empty.");
        var bytes = new byte[(int)size];
        for (int offset = 0; offset < bytes.Length;)
        {
            int count = Math.Min(256 * 1024, bytes.Length - offset);
            await file.ReadExactlyAsync(bytes.AsMemory(offset, count), token).ConfigureAwait(false);
            offset += count;
            report(offset, bytes.Length);
        }
        if (await file.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0) throw new IOException("File grew while reading.");
        return new(name, bytes);
    }
}
