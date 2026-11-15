// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   6
// Annotated:        4/6
// Exempt:           2
// Human-reviewed:   0/6
// IP risk:          Low
// Security risk:    High
// Criteria:         4/4
// Resource impact:  4/10 max
// Unverified:       6
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Infrastructure.Persistence;

// Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=60FB52
// Hairs-Falsified-If: a save based on a stale revision overwrites a newer stored draft
// Hairs-Human:        PENDING
public sealed partial class JsonDraftStore(string path) : IDraftLibraryStore
{
    internal const int MaximumFileBytes = 32 * 1024 * 1024;
    private readonly JsonConfigurationFile<DraftStoreState> _file = new(path, () => new(0, null), Validate, ConfigurationJsonContext.Storage.Drafts, MaximumFileBytes);
    public bool IsPersistent => true;
    // Hairs-AI:           Origin=AI; IP=None; Security=High; Resources=4; Fingerprint=BC3C29
    // Hairs-Falsified-If: a corrupt or oversized drafts file loads as an empty store (revision 0, no draft) instead of throwing
    // Hairs-Human:        PENDING
    public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default) => _file.ReadAsync(cancellationToken);
    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=760835
    // Hairs-Falsified-If: a save whose expectedRevision differs from the stored revision writes the draft instead of throwing DraftConflictException
    // Hairs-Human:        PENDING
    public async Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
    {
        DraftStoreState? saved = null;
        await _file.UpdateAsync(current =>
        {
            if (current.Revision != expectedRevision) throw new DraftConflictException();
            return saved = current with { Revision = checked(current.Revision + 1), Draft = draft };
        }, cancellationToken).ConfigureAwait(false);
        return saved!;
    }

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=702580
    // Hairs-Falsified-If: a saved draft whose Sent-copy state is Pending while its submission state is not Accepted loads without an exception
    // Hairs-Human:        PENDING
    private static void Validate(DraftStoreState state)
    {
        if (state.Revision < 0 || state.Revision == long.MaxValue) throw new ArgumentException("Invalid draft revision.");
        var rows = (state.SavedDrafts ?? []).ToList();
        if (rows.Count > 20) throw new ArgumentException("Keep at most 20 saved drafts in the library.");
        if (state.Draft is not null) rows.Add(state.Draft);
        var ids = new HashSet<Guid>();
        int attachmentBytes = 0;
        foreach (var snapshot in rows)
        {
            if (snapshot?.Draft is null || !ids.Add(snapshot.Draft.Id)) throw new ArgumentException("Invalid or duplicate draft in the library.");
            ValidateSnapshot(snapshot);
            attachmentBytes += snapshot.Draft.Attachments?.Sum(part => part.Content.Length) ?? 0;
            if (attachmentBytes > DraftAttachment.MaximumProfileBytes) throw new ArgumentException("The draft library attachment quota is full.");
        }
    }

    private static void ValidateSnapshot(DraftSnapshot snapshot)
    {
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
        if (draft.FromDisplayName is { } displayName) Broiler.Mail.Core.Validation.ConfigurationValidator.ValidateSenderDisplayName(displayName);
        Text(draft.Subject, 16_000);
        Text(draft.PlainText, 400_000);
        DraftAttachment.Validate(draft.Attachments);
        Text(snapshot.ToText, 32_000); Text(snapshot.CcText, 32_000); Text(snapshot.BccText, 32_000);
        if (draft.InReplyTo is { } parent) Text(parent, 998);
        foreach (var list in new[] { draft.To, draft.Cc, draft.Bcc, draft.References })
        {
            if (list is null || list.Count > 100) throw new ArgumentException("Invalid saved draft header list.");
            foreach (string item in list) Text(item, 998);
        }
    }
}
