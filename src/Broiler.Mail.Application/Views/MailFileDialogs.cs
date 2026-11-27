// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   55
// Annotated:        0/55
// Exempt:           13
// Human-reviewed:   0/55
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       55
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Window;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// The desktop <see cref="IMailFilePicker"/>: the Broiler-drawn standard file dialog for files and
/// <see cref="MailFolderDialog"/> for folders, each shown modally over whatever window <c>owner</c> names
/// (the open form, or the main window). One dialog at a time. What the person picks is checked after the
/// dialog closes: an open needs an existing regular file, a save never names an existing file or folder.
/// </summary>
/// <remarks>
/// These dialogs are opened only by the person's own command (Attach file…, Save as…, Open .eml…, the
/// backup folder choices), which is the documented exemption to the rule that reading, finding and searching
/// open no modal dialog.
/// </remarks>
public sealed class MailFileDialogs : IMailFilePicker, IDisposable
{
    /// <summary>The most room a file dialog takes; it shrinks to fit a smaller window.</summary>
    public static readonly BSize FileDialogSize = new(820, 520);

    /// <summary>The most room a folder dialog takes; it shrinks to fit a smaller window.</summary>
    public static readonly BSize FolderDialogSize = new(720, 480);

    private const double Margin = 12;
    private readonly Func<UiWindow?> _owner;
    private readonly IUiFileDialogDirectoryProvider _listing;
    private readonly Dictionary<MailFilePurpose, string> _lastDirectory = [];
    private UiDialog? _active;
    private UiWindow? _activeOwner;
    private BSize _activeSize;
    private TaskCompletionSource<UiDialogResult?>? _pending;
    private MailFolderDialog? _activeFolder;
    private bool _disposed;
    /// <summary>True when the dialogs list this device's disk, so folders can be checked there; another provider's are its own.</summary>
    private readonly bool _localListing;

    /// <param name="owner">The window to show a dialog over, asked each time; null when none can be shown.</param>
    /// <param name="listing">What folders are listed with; the local file system, on the calling thread, by default.</param>
    public MailFileDialogs(Func<UiWindow?> owner, IUiFileDialogDirectoryProvider? listing = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _listing = listing ?? UiFileSystemDirectoryProvider.Synchronous;
        _localListing = _listing is UiFileSystemDirectoryProvider;
    }

    public MailFilePickerCapabilities Capabilities =>
        MailFilePickerCapabilities.OpenFile | MailFilePickerCapabilities.SaveFile |
        MailFilePickerCapabilities.PickDirectory | MailFilePickerCapabilities.LocalPaths;

    /// <summary>The open file or folder dialog, or null.</summary>
    public UiDialog? ActiveDialog => _active is { IsDisposed: false, IsResultCompleted: false } active ? active : null;

    /// <summary>
    /// The window the open dialog was shown over (the open form, or the main window), or null. A dialog that broke out
    /// into a window of its own has no <see cref="UiWindow.Owner"/> any more; this still names the window it belongs to.
    /// </summary>
    public UiWindow? ActiveOwner => ActiveDialog is not null ? _activeOwner : null;

    /// <summary>The open folder dialog's content, or null.</summary>
    internal MailFolderDialog? ActiveFolderDialog => ActiveDialog is not null ? _activeFolder : null;

    /// <summary>Keys the open dialog takes before its focused control would, such as Ctrl+Enter in a folder dialog.</summary>
    internal bool PreviewInput(UiInputEvent input) => ActiveFolderDialog?.PreviewInput(input) == true;

    /// <summary>The folder a pick for <paramref name="purpose"/> last ended in, or null.</summary>
    public string? LastDirectory(MailFilePurpose purpose) => _lastDirectory.GetValueOrDefault(purpose);

    public async Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default)
    {
        Validate(request);
        if (Unavailable() is { } reason) return MailFilePick<IMailReadableFile>.Unavailable(reason);
        if (token.IsCancellationRequested) return MailFilePick<IMailReadableFile>.Canceled;
        if (ResolveOwner() is not { } owner) return MailFilePick<IMailReadableFile>.Unavailable(CannotOpenHere);

        var dialog = CreateFileDialog(request, UiFileDialogMode.Open, request.AcceptText ?? "Open");
        var result = await PresentAsync(dialog, owner, FileDialogSize, dialog.FileNameEdit,
            (window, placement) => dialog.ShowOpenModal(window, placement), token);
        if (result is null) return MailFilePick<IMailReadableFile>.Unavailable(CouldNotOpen);
        if (result.Kind != UiDialogResultKind.Accepted || string.IsNullOrWhiteSpace(result.Value)) return MailFilePick<IMailReadableFile>.Canceled;

        string path = Path.GetFullPath(result.Value);
        Remember(request.Purpose, Path.GetDirectoryName(path));
        if (OpenProblem(path) is { } problem) return MailFilePick<IMailReadableFile>.Rejected(problem);
        return MailFilePick<IMailReadableFile>.Picked(new LocalMailFile(path));
    }

    public async Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default)
    {
        Validate(request);
        if (Unavailable() is { } reason) return MailFilePick<IMailWritableFile>.Unavailable(reason);
        if (token.IsCancellationRequested) return MailFilePick<IMailWritableFile>.Canceled;
        if (ResolveOwner() is not { } owner) return MailFilePick<IMailWritableFile>.Unavailable(CannotOpenHere);

        var dialog = CreateFileDialog(request, UiFileDialogMode.Save, request.AcceptText ?? "Save");
        dialog.FileName = request.SuggestedFileName ?? "";
        var result = await PresentAsync(dialog, owner, FileDialogSize, dialog.FileNameEdit,
            (window, placement) => dialog.ShowSaveModal(window, placement), token);
        if (result is null) return MailFilePick<IMailWritableFile>.Unavailable(CouldNotOpen);
        if (result.Kind != UiDialogResultKind.Accepted || string.IsNullOrWhiteSpace(result.Value)) return MailFilePick<IMailWritableFile>.Canceled;

        string path = Path.GetFullPath(result.Value);
        Remember(request.Purpose, Path.GetDirectoryName(path));
        if (SaveProblem(path) is { } problem) return MailFilePick<IMailWritableFile>.Rejected(problem);
        return MailFilePick<IMailWritableFile>.Picked(new LocalMailWritableFile(path));
    }

    public async Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default)
    {
        Validate(request);
        if (Unavailable() is { } reason) return MailFilePick<IMailPickedDirectory>.Unavailable(reason);
        if (token.IsCancellationRequested) return MailFilePick<IMailPickedDirectory>.Canceled;
        if (ResolveOwner() is not { } owner) return MailFilePick<IMailPickedDirectory>.Unavailable(CannotOpenHere);

        var dialog = new StandardDialog
        {
            Title = request.Title, PreferredSize = FolderDialogSize, Padding = 12,
            BreakOutMode = UiWindowBreakOutMode.Automatic,
        };
        var picker = new MailFolderDialog(dialog, _listing, StartDirectory(request), request.AcceptText ?? "Use this folder");
        dialog.AddChild(picker);
        _activeFolder = picker;
        var result = await PresentAsync(dialog, owner, FolderDialogSize, picker.Folders,
            (window, placement) => dialog.ShowModal(window, placement), token);
        if (result is null) return MailFilePick<IMailPickedDirectory>.Unavailable(CouldNotOpen);
        // An Accept without a folder (an Enter the folder dialog did not take) is not a choice.
        if (result.Kind != UiDialogResultKind.Accepted || string.IsNullOrWhiteSpace(result.Value)) return MailFilePick<IMailPickedDirectory>.Canceled;

        string path = Path.GetFullPath(result.Value);
        if (_localListing && !IsUsableDirectory(path))
            return MailFilePick<IMailPickedDirectory>.Rejected($"The folder '{DisplayName(path)}' does not exist any more. Choose another folder.");
        Remember(request.Purpose, path);
        return MailFilePick<IMailPickedDirectory>.Picked(new LocalMailDirectory(path));
    }

    /// <summary>Fits an open dialog that is not in its own window to the window it is over, after a resize.</summary>
    public void Reposition(BSize viewport)
    {
        if (ActiveDialog is { IsBrokenOut: false } dialog && _activeOwner is { } owner)
            dialog.SetPlacement(Placement(owner, _activeSize, viewport));
    }

    /// <summary>Closes an open dialog; its pick returns <see cref="MailFilePickStatus.Canceled"/>, and later picks are unavailable.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var pending = _pending;
        if (ActiveDialog is { } dialog)
        {
            try { dialog.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        _active = null; _activeOwner = null; _pending = null; _activeFolder = null;
        pending?.TrySetResult(UiDialogResult.Cancelled);
    }

    private const string CannotOpenHere = "The file dialog cannot be opened here.";
    private const string CouldNotOpen = "The file dialog could not be opened.";

    private string? Unavailable() =>
        _disposed ? CouldNotOpen : ActiveDialog is not null ? "Another file dialog is already open." : null;

    private UiWindow? ResolveOwner()
    {
        UiWindow? owner;
        try { owner = _owner(); }
        catch (ObjectDisposedException) { return null; }
        return owner is { IsDisposed: false, IsClosed: false, Session: { IsDisposed: false } } ? owner : null;
    }

    private static void Validate(MailFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("A file dialog needs a title.", nameof(request));
        if (request.Types is null || request.Types.Any(type => type is null)) throw new ArgumentException("File types cannot be null.", nameof(request));
    }

    private StandardFileDialog CreateFileDialog(MailFileRequest request, UiFileDialogMode mode, string acceptText)
    {
        var dialog = new StandardFileDialog(_listing)
        {
            CurrentDirectory = StartDirectory(request),
            PreferredSize = FileDialogSize,
            BreakOutMode = UiWindowBreakOutMode.Automatic,
        };
        IReadOnlyList<MailFileType> types = request.Types.Count == 0 ? [MailFileType.AllFiles] : request.Types;
        dialog.SetFileTypeFilters(types.Select(type => new UiFileDialogFilter(type.Name, type.Pattern, type.DefaultExtension)), 0);
        // Load-bearing order: changing Mode resets Title and the accept button's text, so both are set after it.
        // ShowOpenModal and ShowSaveModal set the same Mode again, which changes nothing.
        dialog.Mode = mode;
        dialog.Title = request.Title;
        dialog.OkButton.Text = acceptText;
        dialog.FileNameEdit.AccessibleName = "File name";
        dialog.PlacesList.AccessibleName = "Places";
        dialog.FilesList.AccessibleName = "Files";
        dialog.DirectoriesList.AccessibleName = "Folders";
        dialog.FileTypeComboBox.AccessibleName = "File type";
        dialog.SortComboBox.AccessibleName = "Sort files by";
        return dialog;
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> modally over <paramref name="owner"/> and completes, on the UI thread, with its
    /// result once it closed and focus went back to where it was; null when it could not be shown.
    /// </summary>
    private Task<UiDialogResult?> PresentAsync(UiDialog dialog, UiWindow owner, BSize size, UiElement focus,
        Func<UiWindow, BRect, Task<UiDialogResult>> show, CancellationToken token)
    {
        var session = owner.Session!;
        var dispatcher = session.Dispatcher;
        var returnFocus = session.FocusedElement;
        var pending = new TaskCompletionSource<UiDialogResult?>();
        CancellationTokenRegistration registration = default;
        void Complete(UiDialogResult result)
        {
            registration.Dispose();
            if (!_disposed) RestoreFocus(returnFocus);
            pending.TrySetResult(result);
        }
        dialog.ResultCompleted += (_, e) =>
        {
            // Closed at once, so the shell sees no file dialog from here on; the pick completes after the close
            // finished, through the dispatcher, so whoever awaits it never runs inside the dialog's close.
            if (ReferenceEquals(_active, dialog)) { _active = null; _activeOwner = null; _pending = null; _activeFolder = null; }
            if (_disposed || session.IsDisposed) { Complete(e.Result); return; }
            dispatcher.Post(() => Complete(e.Result));
        };

        _active = dialog; _activeOwner = owner; _activeSize = size; _pending = pending;
        try
        {
            _ = show(owner, Placement(owner, size, null));
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            if (ReferenceEquals(_active, dialog)) { _active = null; _activeOwner = null; _pending = null; _activeFolder = null; }
            return Task.FromResult<UiDialogResult?>(null);
        }
        if (pending.Task.IsCompleted) return pending.Task;

        var activeSession = dialog.Session ?? session;
        StandardThemeController.ApplyToSubtree(dialog, StandardControlPaint.GetTheme(ThemeSource(owner)));
        if (focus.CanFocus) activeSession.SetFocus(focus);
        registration = token.Register(() => dispatcher.Post(() =>
        {
            if (!dialog.IsDisposed && !dialog.IsResultCompleted) dialog.Cancel();
        }));
        return pending.Task;
    }

    private static void RestoreFocus(UiElement? element)
    {
        if (element is not { IsDisposed: false, CanFocus: true } || element.Session is not { IsDisposed: false } session) return;
        if (!ReferenceEquals(session.FocusedElement, element)) FocusNavigation.FocusAndReveal(session, element);
    }

    /// <summary>The main window a dialog's owner belongs to, whose theme the dialog takes.</summary>
    private static UiWindow ThemeSource(UiWindow owner)
    {
        var window = owner;
        while (window.Owner is { } next) window = next;
        return window;
    }

    /// <summary>
    /// Centered over <paramref name="owner"/>, as large as <paramref name="size"/> but never past the owner's edges.
    /// A form shown over the main window is measured by its placement, which a resize updates first.
    /// </summary>
    internal static BRect Placement(UiWindow owner, BSize size, BSize? viewport)
    {
        BSize area = owner is { Owner: not null, IsBrokenOut: false, Placement.IsEmpty: false }
            ? new BSize(owner.Placement.Width, owner.Placement.Height)
            : viewport ?? owner.Session?.Host.ViewportSize ?? new BSize(owner.Bounds.Width, owner.Bounds.Height);
        double width = Math.Min(size.Width, Math.Max(0, area.Width - 2 * Margin));
        double height = Math.Min(size.Height, Math.Max(0, area.Height - 2 * Margin));
        return new BRect(Math.Max(0, (area.Width - width) / 2), Math.Max(0, (area.Height - height) / 2), width, height);
    }

    private string StartDirectory(MailFileRequest request)
    {
        foreach (var candidate in new[] { request.InitialDirectory, LastDirectory(request.Purpose), DefaultDirectory(request.Purpose) })
            if (!string.IsNullOrWhiteSpace(candidate) && (_localListing ? IsUsableDirectory(candidate) : Path.IsPathFullyQualified(candidate)))
                return Path.GetFullPath(candidate);
        return Environment.CurrentDirectory;
    }

    private static string? DefaultDirectory(MailFilePurpose purpose)
    {
        string? Known(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder) is { Length: > 0 } path ? path : null; }
            catch (PlatformNotSupportedException) { return null; }
        }
        return purpose switch
        {
            MailFilePurpose.SaveAttachment or MailFilePurpose.ExportOriginal or MailFilePurpose.SaveMessage =>
                Known(Environment.SpecialFolder.UserProfile) is { } home && Directory.Exists(Path.Combine(home, "Downloads"))
                    ? Path.Combine(home, "Downloads") : Known(Environment.SpecialFolder.MyDocuments),
            _ => Known(Environment.SpecialFolder.MyDocuments),
        };
    }

    private void Remember(MailFilePurpose purpose, string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && (!_localListing || IsUsableDirectory(directory))) _lastDirectory[purpose] = directory;
    }

    /// <summary>
    /// True when <paramref name="path"/> is a folder files can be saved into. A folder link (a junction, a volume
    /// mount point or a folder symbolic link) is usable when the folder it leads to, followed to the end, exists;
    /// one that leads nowhere, in a loop, or to a file is not. A reparse point that is not a link, such as a
    /// cloud-synced (OneDrive Files On-Demand) or deduplicated folder, is the folder itself and is usable.
    /// </summary>
    internal static bool IsUsableDirectory(string path)
    {
        try
        {
            var folder = new DirectoryInfo(path);
            if (!folder.Exists) return false;
            if ((folder.Attributes & FileAttributes.ReparsePoint) == 0 || folder.LinkTarget is null) return true;
            return folder.ResolveLinkTarget(returnFinalTarget: true) is { } target && Directory.Exists(target.FullName);
        }
        catch (Exception error) when (IsFileSystemError(error)) { return false; }
    }

    /// <summary>
    /// True when anything is at <paramref name="path"/>: a file, a folder, or a file or folder link, including
    /// one that leads nowhere. A new file is never written through such a name.
    /// </summary>
    internal static bool IsTaken(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        try { return new FileInfo(path).LinkTarget is not null; }
        catch (Exception error) when (IsFileSystemError(error)) { return true; }
    }

    /// <summary>Why <paramref name="path"/> cannot be opened, or null when it is an existing regular file.</summary>
    internal static string? OpenProblem(string path)
    {
        string name = DisplayName(path);
        FileAttributes attributes;
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return $"There is no file named '{name}' in {Path.GetDirectoryName(path)}. Choose an existing file.";
            attributes = File.GetAttributes(path);
        }
        catch (Exception error) when (IsFileSystemError(error)) { return $"'{name}' cannot be read: {error.Message}"; }
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0
            ? $"'{name}' is not a regular file. Choose a file." : null;
    }

    /// <summary>Why a new file cannot be saved as <paramref name="path"/>, or null when it can.</summary>
    internal static string? SaveProblem(string path)
    {
        string name = DisplayName(path);
        try
        {
            if (Directory.Exists(path)) return $"'{name}' is a folder. Type a file name.";
            // A link at the name itself, even one that leads nowhere, is taken: nothing is ever written through it.
            if (IsTaken(path))
                return $"'{name}' already exists in {Path.GetDirectoryName(path)}. Choose another name; Broiler Mail never replaces files.";
        }
        catch (Exception error) when (IsFileSystemError(error)) { return $"'{name}' cannot be checked: {error.Message}"; }
        return Path.GetDirectoryName(path) is { } parent && IsUsableDirectory(parent) ? null : "Save into an existing folder.";
    }

    private static string DisplayName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    internal static bool IsFileSystemError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}

/// <summary>An existing file on this device.</summary>
public sealed class LocalMailFile : IMailReadableFile
{
    public LocalMailFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        LocalPath = Path.GetFullPath(path);
    }

    public string DisplayName => Path.GetFileName(LocalPath);
    public string LocalPath { get; }
    public long? Length
    {
        get
        {
            try { return new FileInfo(LocalPath).Length; }
            catch (Exception error) when (MailFileDialogs.IsFileSystemError(error)) { return null; }
        }
    }

    public Task<Stream> OpenReadAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
    }
}

/// <summary>A new file on this device, written once and never over an existing one.</summary>
public sealed class LocalMailWritableFile : IMailWritableFile
{
    public LocalMailWritableFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        LocalPath = Path.GetFullPath(path);
    }

    public string DisplayName => Path.GetFileName(LocalPath);
    public string LocalPath { get; }

    public Task<MailWrittenFile> WriteNewAsync(Func<Stream, CancellationToken, Task> write, CancellationToken token = default) =>
        LocalMailWrite.WriteNewAsync(Path.GetDirectoryName(LocalPath) ?? throw new IOException("The file has no folder."), [LocalPath], write, token);
}

/// <summary>An existing folder on this device.</summary>
public sealed class LocalMailDirectory : IMailPickedDirectory
{
    /// <summary>How many numbered names, "name (2).ext" through "name (99).ext", are tried before a unique suffix.</summary>
    public const int MaximumNumberedName = 99;

    public LocalMailDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        LocalPath = Path.GetFullPath(path);
    }

    public string DisplayName => Path.GetFileName(Path.TrimEndingDirectorySeparator(LocalPath)) is { Length: > 0 } name ? name : LocalPath;
    public string LocalPath { get; }

    public Task<MailWrittenFile> WriteNewFileAsync(string fileName, Func<Stream, CancellationToken, Task> write, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) || fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("A file name without a folder is required.", nameof(fileName));
        return LocalMailWrite.WriteNewAsync(LocalPath, Candidates(fileName), write, token);
    }

    private IEnumerable<string> Candidates(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName), extension = Path.GetExtension(fileName);
        yield return Path.Combine(LocalPath, fileName);
        for (int number = 2; number <= MaximumNumberedName; number++)
            yield return Path.Combine(LocalPath, $"{stem} ({number}){extension}");
        yield return Path.Combine(LocalPath, $"{stem} {Guid.NewGuid():N}{extension}");
    }
}

/// <summary>The one way picked locations are written: a flushed temporary sibling, then a move that replaces nothing.</summary>
internal static class LocalMailWrite
{
    public const string TemporaryPrefix = ".broiler-save-";
    public const string TemporaryExtension = ".part";

    public static async Task<MailWrittenFile> WriteNewAsync(string directory, IEnumerable<string> targets,
        Func<Stream, CancellationToken, Task> write, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(write);
        token.ThrowIfCancellationRequested();
        using var candidates = targets.GetEnumerator();
        string target = NextFree(candidates) ?? throw new IOException("The file already exists; nothing was replaced.");
        string part = Path.Combine(directory, TemporaryPrefix + Guid.NewGuid().ToString("N") + TemporaryExtension);
        try
        {
            long bytes;
            await using (var stream = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await write(stream, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                bytes = stream.Length;
            }
            while (true)
            {
                try
                {
                    File.Move(part, target, overwrite: false);
                    return new MailWrittenFile(Path.GetFileName(target), target, bytes);
                }
                catch (IOException) when (MailFileDialogs.IsTaken(target))
                {
                    // The name was taken while the file was written: use the next free one, or give up.
                    target = NextFree(candidates) ?? throw new IOException($"'{Path.GetFileName(target)}' appeared while it was being written; nothing was replaced.");
                }
            }
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? NextFree(IEnumerator<string> candidates)
    {
        while (candidates.MoveNext())
            if (!MailFileDialogs.IsTaken(candidates.Current)) return candidates.Current;
        return null;
    }
}
