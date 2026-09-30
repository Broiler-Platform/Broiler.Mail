using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed class JsonDraftStore(string path) : IDraftStore
{
    private readonly JsonConfigurationFile<DraftStoreState> _file = new(path, () => new(0, null), Validate, 4 * 1024 * 1024);
    public bool IsPersistent => true;
    public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default) => _file.ReadAsync(cancellationToken);
    public async Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
    {
        DraftStoreState? saved = null;
        await _file.UpdateAsync(current =>
        {
            if (current.Revision != expectedRevision) throw new DraftConflictException();
            return saved = new(checked(current.Revision + 1), draft);
        }, cancellationToken).ConfigureAwait(false);
        return saved!;
    }

    private static void Validate(DraftStoreState state)
    {
        if (state.Revision < 0 || state.Revision == long.MaxValue) throw new ArgumentException("Invalid draft revision.");
        if (state.Draft is not { } snapshot) return;
        var draft = snapshot.Draft;
        if (!Enum.IsDefined(snapshot.SentCopy) || snapshot.SentCopy != SentCopyState.NotRequested && snapshot.State != DraftSubmissionState.Accepted)
            throw new ArgumentException("Invalid saved Sent-copy state.");
        if (snapshot.SentCopyFolder is { } folder && (folder.Length > 512 || folder.Any(char.IsControl)))
            throw new ArgumentException("Invalid saved Sent folder.");
        if (draft is null || draft.Id == Guid.Empty || draft.AccountId.Value == Guid.Empty || !Enum.IsDefined(snapshot.State))
            throw new ArgumentException("Invalid saved draft identity or state.");
        // Storage limits exceed submission limits so incomplete/invalid edits can still be recovered.
        static void Text(string? value, int limit)
        {
            if (value is null || value.Length > limit) throw new ArgumentException("The draft exceeds the local storage limit.");
        }
        Text(draft.FromAddress, 320);
        Text(draft.Subject, 16_000);
        Text(draft.PlainText, 400_000);
        Text(snapshot.ToText, 32_000); Text(snapshot.CcText, 32_000); Text(snapshot.BccText, 32_000);
        if (draft.InReplyTo is { } parent) Text(parent, 998);
        foreach (var list in new[] { draft.To, draft.Cc, draft.Bcc, draft.References })
        {
            if (list is null || list.Count > 100) throw new ArgumentException("Invalid saved draft header list.");
            foreach (string item in list) Text(item, 998);
        }
    }
}
