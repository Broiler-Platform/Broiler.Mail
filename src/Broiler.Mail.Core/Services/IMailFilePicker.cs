// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   21
// Annotated:        0/21
// Exempt:           28
// Human-reviewed:   0/21
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       21
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Services;

/// <summary>What a <see cref="IMailFilePicker"/> can do on this device.</summary>
[Flags]
public enum MailFilePickerCapabilities
{
    /// <summary>The picker cannot choose anything here; every pick returns <see cref="MailFilePickStatus.Unavailable"/>.</summary>
    None = 0,
    /// <summary>Choosing an existing file to read.</summary>
    OpenFile = 1,
    /// <summary>Choosing a new file to write.</summary>
    SaveFile = 2,
    /// <summary>Choosing an existing folder.</summary>
    PickDirectory = 4,
    /// <summary>Picked items carry a <see cref="IMailPickedLocation.LocalPath"/> on this device's file system.</summary>
    LocalPaths = 8,
}

/// <summary>Why a file is being chosen. A picker remembers the last folder of each purpose separately.</summary>
public enum MailFilePurpose
{
    Attachment,
    OpenOriginal,
    ExportOriginal,
    SaveAttachment,
    SaveMessage,
    BackupLocation,
    RestoreBackup,
    RestoreLocation,
}

/// <summary>One entry of a picker's file type list.</summary>
/// <param name="Name">The text the list shows, such as "Email message (*.eml)".</param>
/// <param name="Pattern">The name pattern the listing is filtered with, such as "*.eml".</param>
/// <param name="DefaultExtension">Appended to a saved name typed without an extension, with its dot; empty for none.</param>
public sealed record MailFileType(string Name, string Pattern, string DefaultExtension = "")
{
    /// <summary>Email messages saved as .eml files.</summary>
    public static MailFileType Email { get; } = new("Email message (*.eml)", "*.eml", ".eml");

    /// <summary>Every file.</summary>
    public static MailFileType AllFiles { get; } = new("All files (*.*)", "*");
}

/// <summary>What to ask the person for.</summary>
/// <param name="Purpose">Why the file is chosen; selects the remembered start folder.</param>
/// <param name="Title">The dialog title, such as "Attach file".</param>
public sealed record MailFileRequest(MailFilePurpose Purpose, string Title)
{
    /// <summary>The file types offered, the first selected. Ignored when choosing a folder.</summary>
    public IReadOnlyList<MailFileType> Types { get; init; } = [MailFileType.AllFiles];

    /// <summary>The proposed name of a file to save, already sanitized by the caller. Ignored otherwise.</summary>
    public string? SuggestedFileName { get; init; }

    /// <summary>A folder to start in, on this device. A picker without local paths ignores it.</summary>
    public string? InitialDirectory { get; init; }

    /// <summary>The accept button's text, such as "Attach" or "Use this folder"; null for the picker's own.</summary>
    public string? AcceptText { get; init; }
}

/// <summary>How a pick ended.</summary>
public enum MailFilePickStatus
{
    /// <summary>The person chose an item, which <see cref="MailFilePick{T}.Item"/> holds.</summary>
    Picked,
    /// <summary>The person closed the picker without choosing, or the request was cancelled.</summary>
    Canceled,
    /// <summary>The choice cannot be used, such as an existing file chosen to save to; <see cref="MailFilePick{T}.Message"/> says why.</summary>
    Rejected,
    /// <summary>No picker can be shown here or now; <see cref="MailFilePick{T}.Message"/> says why.</summary>
    Unavailable,
}

/// <summary>The outcome of one pick.</summary>
public sealed record MailFilePick<T>(MailFilePickStatus Status, T? Item = null, string? Message = null) where T : class
{
    /// <summary>The person closed the picker without choosing.</summary>
    public static MailFilePick<T> Canceled { get; } = new(MailFilePickStatus.Canceled);

    /// <summary>The person chose <paramref name="item"/>.</summary>
    public static MailFilePick<T> Picked(T item) =>
        new(MailFilePickStatus.Picked, item ?? throw new ArgumentNullException(nameof(item)));

    /// <summary>The choice cannot be used, for the reason <paramref name="message"/> gives.</summary>
    public static MailFilePick<T> Rejected(string message) => new(MailFilePickStatus.Rejected, null, message);

    /// <summary>No picker could be shown, for the reason <paramref name="message"/> gives.</summary>
    public static MailFilePick<T> Unavailable(string message) => new(MailFilePickStatus.Unavailable, null, message);
}

/// <summary>A picked file or folder.</summary>
public interface IMailPickedLocation
{
    /// <summary>A name to show, such as the file name.</summary>
    string DisplayName { get; }

    /// <summary>The full path on this device, or null for a location that has none, such as a document provider's.</summary>
    string? LocalPath { get; }
}

/// <summary>A picked existing file.</summary>
public interface IMailReadableFile : IMailPickedLocation
{
    /// <summary>The size in bytes, when known.</summary>
    long? Length { get; }

    /// <summary>Opens the file for reading. The caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(CancellationToken token = default);
}

/// <summary>A picked new file, not yet written.</summary>
public interface IMailWritableFile : IMailPickedLocation
{
    /// <summary>Runs <paramref name="write"/> against a temporary file beside the target, flushes it to disk and then
    /// publishes it under the picked name without replacing anything. Throws <see cref="IOException"/> when the target
    /// appeared in the meantime. The temporary file is deleted when the write throws or is cancelled.</summary>
    Task<MailWrittenFile> WriteNewAsync(Func<Stream, CancellationToken, Task> write, CancellationToken token = default);
}

/// <summary>A picked existing folder.</summary>
public interface IMailPickedDirectory : IMailPickedLocation
{
    /// <summary>Writes a new file named <paramref name="fileName"/> in the folder the same way
    /// <see cref="IMailWritableFile.WriteNewAsync"/> does. When the name is taken it uses "name (2).ext" through
    /// "name (99).ext", then a unique suffix; it never replaces a file.</summary>
    Task<MailWrittenFile> WriteNewFileAsync(string fileName, Func<Stream, CancellationToken, Task> write, CancellationToken token = default);
}

/// <summary>A file a picker handle wrote.</summary>
/// <param name="DisplayName">The name it was published under.</param>
/// <param name="LocalPath">Its full path on this device, or null when it has none.</param>
/// <param name="Bytes">How many bytes were written.</param>
public sealed record MailWrittenFile(string DisplayName, string? LocalPath, long Bytes);

/// <summary>
/// Lets the person choose files and folders. Called on the UI thread, from a UI async handler.
/// Cancelling and unavailability are results, never exceptions: only a malformed request throws.
/// A cancelled token closes any open picker and the call returns <see cref="MailFilePickStatus.Canceled"/>.
/// </summary>
public interface IMailFilePicker
{
    /// <summary>What this picker can do here.</summary>
    MailFilePickerCapabilities Capabilities { get; }

    /// <summary>Asks for an existing file to read.</summary>
    Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default);

    /// <summary>Asks for a new file to write. An existing file is never offered for replacement.</summary>
    Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default);

    /// <summary>Asks for an existing folder.</summary>
    Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default);
}
