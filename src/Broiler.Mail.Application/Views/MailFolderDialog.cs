// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   27
// Annotated:        0/27
// Exempt:           19
// Human-reviewed:   0/27
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       27
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Globalization;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.FileDialog;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// The content of the folder dialog: a path field, known places, and the subfolders of the current folder.
/// Selecting a subfolder only selects it; Enter or a double click opens it. Accept returns the selected
/// subfolder, or the current folder when none is selected, and the line above the buttons always names which.
/// A path typed into the field and not yet opened is what Accept uses: it is opened first, and the dialog closes
/// with it once it could be listed, or stays open and says why it could not. Accept during a listing waits for it.
/// Enter never accepts: only the accept button and Ctrl+Enter do.
/// </summary>
internal sealed class MailFolderDialog : UiElement
{
    private readonly StandardDialog _dialog;
    private readonly IUiFileDialogDirectoryProvider _listing;
    private readonly StandardButton _go = new() { Text = "Go", AccessibleName = "Go to typed folder" };
    private readonly StandardButton _up = new() { Text = "Up one level" };
    private readonly StandardLabel _placesHeader = new() { Text = "Places", UseMnemonic = false };
    private readonly StandardLabel _foldersHeader = new() { Text = "Folders", UseMnemonic = false };
    private readonly StandardLabel _message = new() { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false };
    private readonly StandardButton _accept;
    private readonly StandardButton _cancel = new() { Text = "Cancel", IsCancel = true };
    private readonly Dictionary<string, string> _placePaths = [];
    private CancellationTokenSource? _load;
    private (int Generation, string Path, Task<IReadOnlyList<UiFileDialogEntry>> Listing)? _deferred;
    private int _generation;
    /// <summary>The listing whose folder Accept closes the dialog with once it arrives; 0 when none.</summary>
    private int _acceptGeneration;
    private bool _navigating;

    internal MailFolderDialog(StandardDialog dialog, IUiFileDialogDirectoryProvider listing, string directory, string acceptText)
    {
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _listing = listing ?? throw new ArgumentNullException(nameof(listing));
        // Not the default button: an Enter that bubbles up must never choose a folder.
        _accept = new StandardButton { Text = acceptText };
        Focusable = false;
        // Child order is Tab order.
        foreach (var child in new UiElement[] { PathEdit, _go, _up, _placesHeader, Places, _foldersHeader, Folders, _message, _accept, _cancel })
            AddChild(child);

        PopulatePlaces();
        _go.Clicked += (_, _) => Navigate(PathEdit.Text);
        PathEdit.Submitted += (_, _) => Navigate(PathEdit.Text);
        PathEdit.TextChanged += (_, _) => { if (!_navigating) ShowChoice(); };
        _up.Clicked += (_, _) => GoUp();
        Places.SelectionChanged += (_, e) =>
        {
            if (!_navigating && e.NewItemId is { } id && _placePaths.TryGetValue(id, out var place)) Navigate(place);
        };
        Places.ItemActivated += (_, e) =>
        {
            if (_placePaths.TryGetValue(e.Item.Id, out var place) && !SamePath(place, CurrentDirectory)) Navigate(place);
        };
        Folders.SelectionChanged += (_, _) => { if (!_navigating) ShowChoice(); };
        Folders.ItemActivated += (_, e) => Navigate(e.Item.Id);
        _accept.Clicked += (_, _) => Accept();
        _cancel.Clicked += (_, _) => _dialog.Cancel();
        Navigate(directory);
    }

    internal StandardEdit PathEdit { get; } = new() { MaxLength = 32767, AccessibleName = "Folder path" };
    internal StandardListView Places { get; } = new() { ItemHeight = 28, AccessibleName = "Places" };
    internal StandardListView Folders { get; } = new() { ItemHeight = 28, AccessibleName = "Folders" };
    internal StandardButton AcceptButton => _accept;
    internal StandardButton CancelButton => _cancel;
    internal StandardButton UpButton => _up;

    /// <summary>The folder listed; empty until one could be read.</summary>
    internal string CurrentDirectory { get; private set; } = "";

    /// <summary>What Accept returns: the selected subfolder, else the current folder.</summary>
    internal string? Choice => Folders.SelectedItemId ?? (CurrentDirectory.Length > 0 ? CurrentDirectory : null);

    internal string Message => _message.Text;
    internal bool IsLoading => _load is not null;

    /// <summary>True when the path field holds something other than the folder listed, which Accept would open first.</summary>
    internal bool HasTypedPath
    {
        get
        {
            string text = PathEdit.Text;
            if (string.IsNullOrWhiteSpace(text)) return CurrentDirectory.Length > 0;
            try { return !SamePath(Path.GetFullPath(text.Trim()), CurrentDirectory); }
            catch (Exception error) when (MailFileDialogs.IsFileSystemError(error)) { return true; }
        }
    }

    /// <summary>
    /// Chooses <see cref="Choice"/> and closes the dialog with it. A typed path that was not opened yet is opened
    /// first and chosen once it is listed; a listing still loading is waited for. False when nothing closed now.
    /// </summary>
    internal bool Accept()
    {
        if (HasTypedPath)
        {
            _acceptGeneration = _generation + 1;
            Navigate(PathEdit.Text);
            return _dialog.IsResultCompleted;
        }
        if (IsLoading)
        {
            _acceptGeneration = _generation;
            return false;
        }
        _acceptGeneration = 0;
        return Choice is { } choice && _dialog.Accept(choice);
    }

    internal bool GoUp()
    {
        if (CurrentDirectory.Length == 0 || Directory.GetParent(CurrentDirectory) is not { } parent) return false;
        Navigate(parent.FullName);
        return true;
    }

    /// <summary>Keys the folder dialog takes before the focused control would: Ctrl+Enter accepts and Alt+Up goes up anywhere.</summary>
    internal bool PreviewInput(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        var modifiers = Modifiers(input);
        if (modifiers == KeyboardModifierState.Control && IsKey(input, 0x0D, "Enter")) { Accept(); return true; }
        if (modifiers == KeyboardModifierState.Alt && IsKey(input, 0x26, "Up")) { GoUp(); return true; }
        return false;
    }

    /// <summary>Lists <paramref name="path"/>. A folder that cannot be read keeps the current one and says why.</summary>
    internal void Navigate(string path)
    {
        string full;
        try { full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Type a folder path.") : path.Trim()); }
        catch (Exception error) when (MailFileDialogs.IsFileSystemError(error))
        {
            Fail(path, error);
            return;
        }
        int generation = ++_generation;
        _load?.Cancel();
        var cancellation = _load = new CancellationTokenSource();
        Task<IReadOnlyList<UiFileDialogEntry>> listing;
        try { listing = _listing.GetEntriesAsync(full, cancellation.Token) ?? throw new IOException("Nothing was listed."); }
        catch (Exception error) when (MailFileDialogs.IsFileSystemError(error)) { listing = Task.FromException<IReadOnlyList<UiFileDialogEntry>>(error); }
        if (listing.IsCompleted)
        {
            Apply(generation, full, listing);
            return;
        }
        _message.Text = $"Loading {Describe(full)}…";
        _ = listing.ContinueWith(completed =>
        {
            _ = completed.Exception;
            if (Session?.Dispatcher is { } dispatcher) dispatcher.Post(() => Apply(generation, full, completed));
            else _deferred = (generation, full, completed);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (_deferred is { } deferred) { _deferred = null; Apply(deferred.Generation, deferred.Path, deferred.Listing); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _load?.Cancel(); _load = null; }
        base.Dispose(disposing);
    }

    private void Apply(int generation, string full, Task<IReadOnlyList<UiFileDialogEntry>> listing)
    {
        if (IsDisposed || generation != _generation) return;
        _load = null;
        if (!listing.IsCompletedSuccessfully)
        {
            Fail(full, listing.Exception?.InnerException ?? new OperationCanceledException("The folder listing was cancelled."));
            return;
        }
        var folders = (listing.Result ?? [])
            .Where(entry => entry is { IsDirectory: true } && SamePath(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(entry.FullPath)), full))
            .DistinctBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _navigating = true;
        try
        {
            Folders.SetItems(folders.Select(entry => new UiListItem(entry.FullPath, entry.Name)));
            Folders.SelectedItemId = null;
            CurrentDirectory = full;
            PathEdit.Text = full;
            Folders.AccessibleName = $"Folders in {Describe(full)}";
            _foldersHeader.Text = $"Folders in {Describe(full)} ({folders.Length.ToString(CultureInfo.InvariantCulture)})";
            _up.IsEnabled = Directory.GetParent(full) is not null;
            SyncSelectedPlace();
        }
        finally { _navigating = false; }
        ShowChoice();
        if (generation == _acceptGeneration)
        {
            _acceptGeneration = 0;
            if (!_dialog.IsResultCompleted && Choice is { } choice) _dialog.Accept(choice);
        }
    }

    private void Fail(string path, Exception error)
    {
        _load = null;
        // A folder that could not be opened is never chosen; the dialog stays open and says why.
        _acceptGeneration = 0;
        _message.Text = string.IsNullOrWhiteSpace(path) ? error.Message : $"Cannot open {Describe(path)}: {error.Message}";
        // The folder that is still listed stays the one a choice would use.
        _navigating = true;
        try { if (CurrentDirectory.Length > 0) PathEdit.Text = CurrentDirectory; }
        finally { _navigating = false; }
        _accept.IsEnabled = Choice is not null;
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    private void ShowChoice()
    {
        bool typed = HasTypedPath;
        _accept.IsEnabled = typed || Choice is not null;
        _message.Text = typed
            ? string.IsNullOrWhiteSpace(PathEdit.Text) ? "Type a folder path." : $"Choose folder: {PathEdit.Text.Trim()}. It is opened first."
            : Choice is { } choice
            ? Folders.Items.Count == 0 ? $"Choose folder: {choice}. It has no subfolders." : $"Choose folder: {choice}"
            : "Choose a folder.";
        Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render | UiInvalidationKind.Semantic);
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (base.OnInput(input)) return true;
        if (input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        if (PreviewInput(input)) return true;
        // Enter acts where it was pressed (Go in the path field, open in the folder list, the focused button) and
        // stops here, so the dialog never takes it as an implicit Accept.
        if (IsKey(input, 0x0D, "Enter")) return true;
        if (Modifiers(input) == KeyboardModifierState.None && IsKey(input, 0x08, "Backspace") &&
            Session?.FocusedElement is { } focused && (focused == Places || focused == Folders))
            return GoUp() || true;
        return false;
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new BSize(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
    }

    protected override void ArrangeCore(BRect rect)
    {
        double line = BTextMeasurer.GetLineHeight(StandardControlPaint.GetTheme(this).FontBody);
        double row = Math.Max(30, Math.Ceiling(line + 10));
        double header = Math.Max(20, Math.Ceiling(line + 2));
        double gap = 8;
        double width = Math.Max(0, rect.Width);
        double goWidth = Math.Max(48, Math.Ceiling(line * 2.5));
        double upWidth = Math.Max(104, Math.Ceiling(line * 6.5));
        PathEdit.Arrange(new BRect(rect.Left, rect.Top, Math.Max(0, width - goWidth - upWidth - 2 * gap), row));
        _go.Arrange(new BRect(Math.Max(rect.Left, rect.Right - upWidth - gap - goWidth), rect.Top, goWidth, row));
        _up.Arrange(new BRect(Math.Max(rect.Left, rect.Right - upWidth), rect.Top, upWidth, row));

        double buttonWidth = Math.Max(92, Math.Ceiling(line * 5));
        double acceptWidth = Math.Max(buttonWidth, Math.Ceiling(_accept.Text.Length * line * 0.55 + 24));
        double buttonsTop = Math.Max(rect.Top + row + gap, rect.Bottom - row);
        double messageHeight = Math.Max(header, Math.Ceiling(line * 2 + 4));
        double messageTop = Math.Max(rect.Top + row + gap, buttonsTop - gap - messageHeight);
        double listTop = rect.Top + row + gap;
        double listHeight = Math.Max(0, messageTop - gap - listTop - header);
        double placesWidth = Math.Min(190, Math.Max(120, width * 0.28));
        double foldersLeft = rect.Left + placesWidth + gap;
        double foldersWidth = Math.Max(0, width - placesWidth - gap);

        _placesHeader.Arrange(new BRect(rect.Left, listTop, placesWidth, header));
        Places.Arrange(new BRect(rect.Left, listTop + header, placesWidth, listHeight));
        _foldersHeader.Arrange(new BRect(foldersLeft, listTop, foldersWidth, header));
        Folders.Arrange(new BRect(foldersLeft, listTop + header, foldersWidth, listHeight));
        _message.Arrange(new BRect(rect.Left, messageTop, width, messageHeight));
        _cancel.Arrange(new BRect(Math.Max(rect.Left, rect.Right - buttonWidth), buttonsTop, buttonWidth, row));
        _accept.Arrange(new BRect(Math.Max(rect.Left, rect.Right - buttonWidth - gap - acceptWidth), buttonsTop, acceptWidth, row));
    }

    private void PopulatePlaces()
    {
        var items = new List<UiListItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string label, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); if (!Directory.Exists(full)) return; }
            catch (Exception error) when (MailFileDialogs.IsFileSystemError(error)) { return; }
            if (!seen.Add(Path.TrimEndingDirectorySeparator(full))) return;
            string id = "place:" + items.Count.ToString(CultureInfo.InvariantCulture);
            _placePaths[id] = full;
            items.Add(new UiListItem(id, label));
        }
        string? Known(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch (PlatformNotSupportedException) { return null; }
        }
        string? home = Known(Environment.SpecialFolder.UserProfile);
        Add("Home", home);
        Add("Desktop", Known(Environment.SpecialFolder.DesktopDirectory));
        Add("Documents", Known(Environment.SpecialFolder.MyDocuments));
        Add("Downloads", string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, "Downloads"));
        string[] drives;
        try { drives = Directory.GetLogicalDrives(); }
        catch (Exception error) when (MailFileDialogs.IsFileSystemError(error)) { drives = []; }
        foreach (var drive in drives)
        {
            string root = Path.GetPathRoot(drive) ?? drive;
            Add(root == Path.DirectorySeparatorChar.ToString() ? "File system" : "Drive " + Path.TrimEndingDirectorySeparator(root), drive);
        }
        Places.SetItems(items);
    }

    private void SyncSelectedPlace()
    {
        string current = Path.TrimEndingDirectorySeparator(CurrentDirectory);
        Places.SelectedItemId = _placePaths
            .Where(pair => SamePath(pair.Value, current) ||
                current.StartsWith(Path.TrimEndingDirectorySeparator(pair.Value) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value.Length)
            .Select(pair => pair.Key)
            .FirstOrDefault();
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static string Describe(string path)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(path ?? "");
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : path ?? "";
    }

    /// <summary>Shift, Control and Alt held, whichever side of the keyboard they are on.</summary>
    private static KeyboardModifierState Modifiers(UiInputEvent input)
    {
        var held = input.KeyModifiers;
        var modifiers = KeyboardModifierState.None;
        if ((held & (KeyboardModifierState.Shift | KeyboardModifierState.LeftShift | KeyboardModifierState.RightShift)) != 0) modifiers |= KeyboardModifierState.Shift;
        if ((held & (KeyboardModifierState.Control | KeyboardModifierState.LeftControl | KeyboardModifierState.RightControl)) != 0) modifiers |= KeyboardModifierState.Control;
        if ((held & (KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt | KeyboardModifierState.RightAlt)) != 0) modifiers |= KeyboardModifierState.Alt;
        return modifiers;
    }

    private static bool IsKey(UiInputEvent input, int code, string name) =>
        input.NativeKeyCode == code || string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(input.KeyName, "VirtualKey:" + code.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
