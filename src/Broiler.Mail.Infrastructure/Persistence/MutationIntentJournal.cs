using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Infrastructure.Persistence;

public sealed record MutationIntent([property: JsonRequired] Guid Id, [property: JsonRequired] MailMessageKey Source, [property: JsonRequired] string? DestinationMailbox)
{
    public string Operation { get; init; } = "mailbox";
    public string? Flag { get; init; }
    public bool? IsSet { get; init; }
}
public sealed record MutationIntentState([property: JsonRequired] IReadOnlyList<MutationIntent> Entries);

/// <summary>Durable pre-network MOVE/delete intents. Never replayed as server commands.</summary>
public sealed class MutationIntentJournal
{
    private static readonly ConcurrentDictionary<(string Path, MailMessageKey Source), byte> Active = new();
    private static readonly ConcurrentDictionary<(string Path, Guid Id), byte> Pending = new();
    private readonly string _cachePath;
    private readonly JsonConfigurationFile<MutationIntentState> _file;
    public CacheRecoveryJournal Receipts { get; }
    public MutationIntentJournal(string cachePath)
    {
        _cachePath = Path.GetFullPath(cachePath);
        _file = new(_cachePath + ".intents.json", () => new([]), Validate, ConfigurationJsonContext.Storage.MutationIntents);
        Receipts = new(_cachePath + ".recovery.json");
    }
    public int PendingCount => Pending.Keys.Count(key => key.Path == _cachePath);
    internal void ForgetPending(Guid id) => Pending.TryRemove((_cachePath, id), out _);
    public int ActiveCount => Active.Keys.Count(key => key.Path == _cachePath);
    internal IDisposable Enter(MailMessageKey source)
    {
        if (!Active.TryAdd((_cachePath, source), 0)) throw new InvalidOperationException("A server mutation for this message is still in progress.");
        return new Lease(() => Active.TryRemove((_cachePath, source), out _));
    }
    internal async Task<bool> BlocksAccountRemovalAsync(Broiler.Mail.Core.Accounts.AccountId account, CancellationToken token)
    {
        var intents = await ReadAsync(token).ConfigureAwait(false);
        var receipts = await Receipts.ReadAsync(token).ConfigureAwait(false);
        ValidateReceiptLinks(intents, receipts);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (intents.Any(row => row.Source.AccountId == account) || receipts.Any(row => row.Source.AccountId == account) ||
            Active.Keys.Any(row => string.Equals(row.Path, _cachePath, comparison) && row.Source.AccountId == account)) return true;
        // A transient marker with no readable durable identity cannot safely be attributed to another account.
        var known = intents.Select(row => row.Id).Concat(receipts.Select(row => row.Id)).ToHashSet();
        return Pending.Keys.Any(row => string.Equals(row.Path, _cachePath, comparison) && !known.Contains(row.Id));
    }
    public async Task<Guid> BeginAsync(MailMessageKey source, string? destination, CancellationToken token = default, string? flag = null, bool? isSet = null)
    {
        if ((await Receipts.ReadAsync(token).ConfigureAwait(false)).Any(entry => entry.Source == source))
            throw new InvalidOperationException("Repair this message's confirmed local change before another mutation.");
        Guid id = Guid.NewGuid();
        await _file.UpdateAsync(state =>
        {
            if (state.Entries.Any(entry => entry.Source == source)) throw new InvalidOperationException("Review the unresolved action for this message before another mutation.");
            return new(state.Entries.Append(new(id, source, destination) { Operation = flag is null ? "mailbox" : "flag", Flag = flag, IsSet = isSet }).ToArray());
        }, token).ConfigureAwait(false);
        Pending.TryAdd((_cachePath, id), 0); return id;
    }
    public async Task<IReadOnlyList<MutationIntent>> ReadAsync(CancellationToken token = default) => (await _file.ReadAsync(token).ConfigureAwait(false)).Entries;
    internal static void ValidateReceiptLinks(IReadOnlyList<MutationIntent> intents, IReadOnlyList<CacheRecoveryEntry> receipts)
    {
        foreach (var intent in intents)
            if (receipts.SingleOrDefault(receipt => receipt.Id == intent.Id) is { } receipt &&
                (receipt.Source != intent.Source || receipt.Destination?.MailboxId != intent.DestinationMailbox || receipt.Operation != intent.Operation || receipt.Flag != intent.Flag || receipt.IsSet != intent.IsSet))
                throw new InvalidDataException("A mutation intent and its confirmed receipt have different identities.");
    }
    public async Task CompleteIfPresentAsync(Guid id, MailMessageKey source, string? destination, CancellationToken token = default, string? flag = null, bool? isSet = null)
    {
        var entries = await ReadAsync(token).ConfigureAwait(false);
        if (entries.SingleOrDefault(entry => entry.Id == id) is { } entry)
        {
            if (entry.Source != source || entry.DestinationMailbox != destination || entry.Flag != flag || entry.IsSet != isSet) throw new InvalidDataException("Conflicting mutation completion identity.");
            await CompleteAsync(id, token).ConfigureAwait(false);
        }
    }
    public async Task CompleteAsync(Guid id, CancellationToken token = default, bool keepPending = false)
    {
        await _file.UpdateAsync(state => new(state.Entries.Where(entry => entry.Id != id).ToArray()), token).ConfigureAwait(false);
        if (!keepPending) ForgetPending(id);
    }
    public async Task DismissAsync(Guid id, CancellationToken token = default)
    {
        await _file.UpdateAsync(state =>
    {
        var entry = state.Entries.SingleOrDefault(entry => entry.Id == id) ?? throw new InvalidOperationException("The unresolved action changed; refresh the list.");
        if (Active.ContainsKey((_cachePath, entry.Source))) throw new InvalidOperationException("This server operation is still finishing. Wait before reviewing its outcome.");
        return new(state.Entries.Where(entry => entry.Id != id).ToArray());
    }, token).ConfigureAwait(false);
        ForgetPending(id);
    }
    private static void Validate(MutationIntentState state)
    {
        if (state.Entries is null || state.Entries.Count > 128 || state.Entries.Any(entry => entry is null || entry.Id == Guid.Empty) || state.Entries.Select(entry => entry.Id).Distinct().Count() != state.Entries.Count || state.Entries.Select(entry => entry.Source).Distinct().Count() != state.Entries.Count)
            throw new InvalidDataException("Unsupported or full mutation-intent journal.");
        foreach (var entry in state.Entries)
        {
            if (entry.Operation is not ("mailbox" or "flag") ||
                (entry.Operation == "flag" ? entry.DestinationMailbox is not null || entry.Flag is not ("seen" or "flagged") || entry.IsSet is null : entry.Flag is not null || entry.IsSet is not null))
                throw new InvalidDataException("Invalid mutation-intent operation payload.");
            _ = new MailboxKey(entry.Source.AccountId, entry.Source.MailboxId, entry.Source.UidValidity);
            if (entry.Source.Uid == 0) throw new InvalidDataException("Invalid mutation UID.");
            _ = Broiler.Mail.Infrastructure.Protocols.Imap.ImapMailboxName.Validate(entry.Source.MailboxId);
            if (entry.DestinationMailbox is { } target)
            {
                target = Broiler.Mail.Infrastructure.Protocols.Imap.ImapMailboxName.Validate(target);
                if (target == entry.Source.MailboxId) throw new InvalidDataException("A move requires a different mailbox.");
            }
        }
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}
