// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.UI.FileDialog;

namespace Broiler.Mail.Tests;

/// <summary>
/// Lists folders that exist only in memory. A folder can be listed at once, fail, or be held until the test
/// completes its listing, so asynchronous and stale results can be driven step by step.
/// </summary>
internal sealed class InMemoryFileDialogDirectoryProvider : IUiFileDialogDirectoryProvider
{
    private readonly Dictionary<string, Func<Task<IReadOnlyList<UiFileDialogEntry>>>> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every folder asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>A full path under a root that does not exist on disk.</summary>
    public static string PathOf(params string[] parts) =>
        Path.GetFullPath(Path.Combine([Path.GetTempPath(), "broiler-virtual-folders", .. parts]));

    /// <summary>Makes <paramref name="path"/> list the given subfolders (and the given files) at once.</summary>
    public InMemoryFileDialogDirectoryProvider Folder(string path, IEnumerable<string> subfolders, IEnumerable<string>? files = null)
    {
        var entries = subfolders.Select(name => new UiFileDialogEntry(name, Path.Combine(path, name), true, 0, DateTime.UnixEpoch))
            .Concat((files ?? []).Select(name => new UiFileDialogEntry(name, Path.Combine(path, name), false, 1, DateTime.UnixEpoch)))
            .ToArray();
        _folders[Key(path)] = () => Task.FromResult<IReadOnlyList<UiFileDialogEntry>>(entries);
        return this;
    }

    /// <summary>Makes listing <paramref name="path"/> fail with <paramref name="error"/>.</summary>
    public InMemoryFileDialogDirectoryProvider Fail(string path, Exception error)
    {
        _folders[Key(path)] = () => Task.FromException<IReadOnlyList<UiFileDialogEntry>>(error);
        return this;
    }

    /// <summary>Makes the next listing of <paramref name="path"/> wait for the returned source.</summary>
    public TaskCompletionSource<IReadOnlyList<UiFileDialogEntry>> Hold(string path)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<UiFileDialogEntry>>();
        _folders[Key(path)] = () => pending.Task;
        return pending;
    }

    /// <summary>Entries for subfolders of <paramref name="path"/>, to complete a held listing with.</summary>
    public static IReadOnlyList<UiFileDialogEntry> Subfolders(string path, params string[] names) =>
        names.Select(name => new UiFileDialogEntry(name, Path.Combine(path, name), true, 0, DateTime.UnixEpoch)).ToArray();

    public Task<IReadOnlyList<UiFileDialogEntry>> GetEntriesAsync(string directory, CancellationToken cancellationToken)
    {
        Requests.Add(directory);
        return _folders.TryGetValue(Key(directory), out var list)
            ? list()
            : Task.FromException<IReadOnlyList<UiFileDialogEntry>>(new DirectoryNotFoundException($"Could not find '{directory}'."));
    }

    private static string Key(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
