using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed partial class JsonDraftStore
{
    private readonly string _libraryPath = Path.GetFullPath(path);
    public Task<DraftStoreState> ParkAsync(long expectedRevision, CancellationToken token = default) => ChangeLibraryAsync(expectedRevision, current =>
    {
        if (current.Draft is null) throw new InvalidOperationException("No active draft to save and close.");
        if ((current.SavedDrafts?.Count ?? 0) >= 20) throw new InvalidOperationException("The library holds 20 drafts. Resume or remove one first.");
        return current with { Draft = null, SavedDrafts = (current.SavedDrafts ?? []).Append(current.Draft).ToArray() };
    }, token);
    public Task<DraftStoreState> ActivateAsync(long expectedRevision, Guid id, CancellationToken token = default) => ChangeLibraryAsync(expectedRevision, current =>
    {
        var selected = (current.SavedDrafts ?? []).SingleOrDefault(row => row.Draft.Id == id) ?? throw new InvalidOperationException("The saved draft no longer exists.");
        var rows = (current.SavedDrafts ?? []).Where(row => row.Draft.Id != id).ToList();
        if (current.Draft is not null) rows.Add(current.Draft);
        return current with { Draft = selected, SavedDrafts = rows.ToArray() };
    }, token);
    public Task<DraftStoreState> RemoveSavedAsync(long expectedRevision, Guid id, CancellationToken token = default) => ChangeLibraryAsync(expectedRevision, current =>
    {
        if (!(current.SavedDrafts ?? []).Any(row => row.Draft.Id == id)) throw new InvalidOperationException("The saved draft no longer exists.");
        return current with { SavedDrafts = (current.SavedDrafts ?? []).Where(row => row.Draft.Id != id).ToArray() };
    }, token);
    private async Task<DraftStoreState> ChangeLibraryAsync(long revision, Func<DraftStoreState, DraftStoreState> change, CancellationToken token)
    {
        DraftStoreState? saved = null;
        await _file.UpdateAsync(current =>
        {
            if (current.Revision != revision) throw new DraftConflictException();
            var next = change(current) with { Revision = checked(current.Revision + 1) };
            Validate(next);
            // Preserve exact legacy bytes before first enabling the additive library format.
            if (current.SavedDrafts is null && next.SavedDrafts is not null && File.Exists(_libraryPath))
            {
                byte[] bytes = File.ReadAllBytes(_libraryPath);
                string backup = _libraryPath + ".before-library-" + Guid.NewGuid().ToString("N") + ".backup";
                using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(flushToDisk: true); }
                if (!bytes.AsSpan().SequenceEqual(File.ReadAllBytes(backup))) throw new IOException("Draft library recovery backup failed verification.");
            }
            return saved = next;
        }, token).ConfigureAwait(false);
        return saved!;
    }
}
