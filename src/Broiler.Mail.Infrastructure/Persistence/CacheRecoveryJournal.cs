using Broiler.Mail.Core.Messages;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Infrastructure.Persistence;

/// <summary>Durable receipts accepted only after server confirmation. Null destination means permanent deletion.</summary>
public sealed record CacheRecoveryEntry([property: JsonRequired] Guid Id, [property: JsonRequired] MailMessageKey Source, [property: JsonRequired] MailMessageKey? Destination)
{
    public string Operation { get; init; } = "mailbox";
    public string? Flag { get; init; }
    public bool? IsSet { get; init; }
}
public sealed record CacheRecoveryState([property: JsonRequired] IReadOnlyList<CacheRecoveryEntry> Entries);

public sealed class CacheRecoveryJournal
{
    private readonly JsonConfigurationFile<CacheRecoveryState> _file;
    public CacheRecoveryJournal(string path) => _file = new(path, () => new([]), Validate, ConfigurationJsonContext.Storage.CacheRecovery);
    public async Task<IReadOnlyList<CacheRecoveryEntry>> ReadAsync(CancellationToken token = default) => (await _file.ReadAsync(token).ConfigureAwait(false)).Entries;
    public async Task<Guid> RecordAsync(MailMessageKey source, MailMessageKey? destination, CancellationToken token = default)
    {
        Guid id = Guid.NewGuid();
        await _file.UpdateAsync(state =>
        {
            var existing = state.Entries.FirstOrDefault(entry => entry.Operation == "mailbox" && entry.Source == source && entry.Destination == destination);
            if (existing is not null) { id = existing.Id; return state; }
            return new(state.Entries.Append(new(id, source, destination)).ToArray());
        }, token).ConfigureAwait(false);
        return id;
    }
    public Task ConfirmIntentAsync(Guid id, MailMessageKey source, MailMessageKey? destination, CancellationToken token = default) => _file.UpdateAsync(state =>
    {
        var existing = state.Entries.SingleOrDefault(entry => entry.Id == id);
        if (existing is not null && (existing.Operation != "mailbox" || existing.Source != source || existing.Destination != destination)) throw new InvalidDataException("Conflicting confirmed receipt.");
        return existing is not null ? state : new(state.Entries.Append(new(id, source, destination)).ToArray());
    }, token);
    public async Task<Guid> RecordFlagAsync(MailMessageKey source, string flag, bool isSet, Guid? intentId = null, CancellationToken token = default)
    {
        Guid id = intentId ?? Guid.NewGuid();
        await _file.UpdateAsync(state =>
        {
            var existing = state.Entries.FirstOrDefault(entry => entry.Operation == "flag" && entry.Source == source && entry.Flag == flag && entry.IsSet == isSet);
            if (existing is not null)
            {
                if (intentId is { } required && existing.Id != required) throw new InvalidDataException("Conflicting flag receipt identity.");
                id = existing.Id; return state;
            }
            return new(state.Entries.Append(new(id, source, null) { Operation = "flag", Flag = flag, IsSet = isSet }).ToArray());
        }, token).ConfigureAwait(false); return id;
    }
    public Task CompleteAsync(Guid id, CancellationToken token = default) => _file.UpdateAsync(state => new(state.Entries.Where(entry => entry.Id != id).ToArray()), token);
    private static void Validate(CacheRecoveryState state)
    {
        if (state.Entries is null || state.Entries.Count > 128 || state.Entries.Any(entry => entry is null || entry.Id == Guid.Empty) || state.Entries.Select(entry => entry.Id).Distinct().Count() != state.Entries.Count)
            throw new InvalidDataException("Unsupported or full cache recovery journal. Repair pending changes before continuing.");
        foreach (var entry in state.Entries)
        {
            Key(entry.Source);
            if (entry.Operation is not ("mailbox" or "flag") ||
                (entry.Operation == "flag" ? entry.Destination is not null || entry.Flag is not ("seen" or "flagged") || entry.IsSet is null : entry.Flag is not null || entry.IsSet is not null))
                throw new InvalidDataException("Invalid cache recovery operation payload.");
            if (entry.Destination is { } target)
            {
                Key(target);
                if (entry.Source.AccountId != target.AccountId || entry.Source.MailboxId == target.MailboxId)
                    throw new InvalidDataException("Invalid recovery move scope.");
            }
        }
    }
    private static void Key(MailMessageKey key)
    {
        _ = new MailboxKey(key.AccountId, key.MailboxId, key.UidValidity);
        for (int i = 0; i < key.MailboxId.Length; i++)
            if (char.IsSurrogate(key.MailboxId[i]) && (!char.IsHighSurrogate(key.MailboxId[i]) || ++i >= key.MailboxId.Length || !char.IsLowSurrogate(key.MailboxId[i])))
                throw new InvalidDataException("Invalid Unicode recovery mailbox identity.");
        if (key.Uid == 0 || key.MailboxId.Length > 1024 || key.MailboxId.Any(char.IsControl)) throw new InvalidDataException("Invalid recovery message identity.");
    }
}
