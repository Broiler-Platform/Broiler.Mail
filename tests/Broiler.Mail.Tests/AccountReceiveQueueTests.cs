using System.Collections.Concurrent;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class AccountReceiveQueueTests
{
    [Fact]
    public async Task ExplicitEnabledQueueKeepsOverlappingMessageKeysAndSelectionIsolatedAndSkipsDisabledAccounts()
    {
        using var f = await Fixture.Create(3); var disabled = f.Profiles[2] with { IsEnabled = false }; await f.Store.SaveAndSelectAsync(disabled); await f.Store.SelectAsync(f.Profiles[0].Id); f.Queue.UpdateAccounts((await f.Store.LoadStateAsync()).Accounts);
        Assert.Empty(f.Receiver.Started); Assert.False(f.Queue.HasWork); Assert.Equal(2, f.Queue.QueueAllEnabled()); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(f.Profiles[0].Id, (await f.Store.LoadStateAsync()).SelectedAccountId); Assert.Equal(0, f.Receiver.Calls(disabled.Id)); Assert.False(f.Queue.QueueAccount(disabled.Id));
        foreach (var profile in f.Profiles.Take(2)) { var head = await f.Cache.GetLatestMailboxAsync(profile.Id, "INBOX"); Assert.NotNull(head); var row = Assert.Single(await f.Cache.ReadSummariesAsync(head.Key, 10)); Assert.Equal(profile.EmailAddress, row.Sender); Assert.Equal(1u, row.Key.Uid); Assert.Equal(AccountReceiveState.Completed, f.Queue.Jobs.Single(job => job.AccountId == profile.Id).State); }
        Assert.InRange(f.Receiver.MaximumActive, 1, 2); Assert.All(f.Queue.Jobs.Where(job => job.AccountId != disabled.Id), job => Assert.Equal(1, job.SavedHeaders));
    }
    [Fact]
    public async Task OneHeldAccountCannotStopOtherAccountsFromMakingFifoProgressAndDuplicateRequestsCoalesce()
    {
        using var f = await Fixture.Create(4); var first = f.Profiles[0].Id; var held = NewSource(); var entered = NewSource(); var othersDone = NewSource();
        f.Receiver.Get = async (profile, token) => { if (profile.Id == first) { entered.TrySetResult(); await held.Task.WaitAsync(token); } return Page(profile); };
        f.Queue.Changed += (_, _) => { if (f.Queue.Jobs.Where(job => job.AccountId != first).All(job => job.State == AccountReceiveState.Completed)) othersDone.TrySetResult(); };
        Assert.Equal(4, f.Queue.QueueAllEnabled()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(f.Queue.QueueAccount(first)); await othersDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(f.Queue.HasWork); Assert.Equal(AccountReceiveState.Receiving, f.Queue.Jobs.Single(job => job.AccountId == first).State);
        Assert.Equal(f.Profiles.Skip(1).Select(profile => profile.Id), f.Receiver.Started.Where(id => id != first)); Assert.Equal(1, f.Receiver.Calls(first)); Assert.InRange(f.Receiver.MaximumActive, 1, 2);
        held.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task QueuedAndActiveCancellationArePerAccountAndDoNotCancelAnotherAccount()
    {
        using var f = await Fixture.Create(3); var entered = new ConcurrentDictionary<AccountId, TaskCompletionSource>(); foreach (var p in f.Profiles) entered[p.Id] = NewSource(); var release = NewSource();
        f.Receiver.Get = async (profile, token) => { entered[profile.Id].TrySetResult(); await release.Task.WaitAsync(token); return Page(profile); };
        f.Queue.QueueAllEnabled(); await Task.WhenAll(entered[f.Profiles[0].Id].Task, entered[f.Profiles[1].Id].Task).WaitAsync(TimeSpan.FromSeconds(5));
        f.Queue.Cancel(f.Profiles[2].Id); f.Queue.Cancel(f.Profiles[0].Id); release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, f.Receiver.Calls(f.Profiles[2].Id)); Assert.Equal(AccountReceiveState.Canceled, f.Queue.Jobs[0].State); Assert.Equal(AccountReceiveState.Completed, f.Queue.Jobs[1].State); Assert.Equal(AccountReceiveState.Canceled, f.Queue.Jobs[2].State);
        Assert.Null(await f.Cache.GetLatestMailboxAsync(f.Profiles[0].Id, "INBOX")); Assert.NotNull(await f.Cache.GetLatestMailboxAsync(f.Profiles[1].Id, "INBOX"));
    }
    [Theory]
    [InlineData("disable")]
    [InlineData("remove")]
    [InlineData("change")]
    public async Task SavedConfigurationIsRecheckedBeforeAQueuedAccountCanConnect(string mode)
    {
        using var f = await Fixture.Create(3); var release = NewSource(); var entered = NewSource(); int blocked = 0; var target = f.Profiles[2];
        f.Receiver.Get = async (profile, token) => { if (profile.Id != target.Id) { if (Interlocked.Increment(ref blocked) == 2) entered.TrySetResult(); await release.Task.WaitAsync(token); } return Page(profile); };
        f.Queue.QueueAllEnabled(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode == "remove") await f.Store.RemoveAsync(target.Id); else await f.Store.SaveAndSelectAsync(target with { IsEnabled = mode != "disable", IncomingServer = target.IncomingServer with { Host = mode == "change" ? "changed.example.test" : target.IncomingServer.Host } });
        // No roster notification: dispatch must independently re-read the durable store.
        release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, f.Receiver.Calls(target.Id)); Assert.Null(await f.Cache.GetLatestMailboxAsync(target.Id, "INBOX")); Assert.Equal(AccountReceiveState.ConfigurationChanged, f.Queue.Jobs.Single(job => job.AccountId == target.Id).State);
    }
    [Fact]
    public async Task ConfigurationChangeCancelsAnIgnoredInFlightReceiveBeforeItCanWrite()
    {
        using var f = await Fixture.Create(1); var entered = NewSource(); var release = NewSource(); CancellationToken observed = default;
        f.Receiver.Get = async (profile, token) => { observed = token; entered.TrySetResult(); await release.Task; return Page(profile); };
        f.Queue.QueueAllEnabled(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var updated = f.Profiles[0] with { IncomingServer = f.Profiles[0].IncomingServer with { Host = "new.example.test" } };
        await f.Store.SaveAndSelectAsync(updated); f.Queue.UpdateAccounts([updated]); Assert.True(observed.IsCancellationRequested); Assert.True(f.Queue.HasWork); Assert.Equal(AccountReceiveState.Canceling, f.Queue.Jobs.Single().State);
        release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Null(await f.Cache.GetLatestMailboxAsync(updated.Id, "INBOX")); Assert.Equal(AccountReceiveState.ConfigurationChanged, f.Queue.Jobs.Single().State);
        f.Receiver.Get = (profile, _) => Task.FromResult(Page(profile)); Assert.True(f.Queue.QueueAccount(updated.Id)); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(AccountReceiveState.Completed, f.Queue.Jobs.Single().State);
    }
    [Fact]
    public async Task AccountWritesWaitForValidatedCachePublicationButNeverForNetworkIo()
    {
        using var f = await Fixture.Create(1); var cacheEntered = NewSource(); var releaseCache = NewSource(); var receiverEntered = NewSource(); var releaseReceiver = NewSource(); var target = f.Profiles[0];
        f.Receiver.Get = async (profile, token) => { receiverEntered.TrySetResult(); await releaseReceiver.Task.WaitAsync(token); return Page(profile); };
        var heldCache = new CacheProxy(f.Cache) { BeforeSave = async (_, token) => { cacheEntered.TrySetResult(); await releaseCache.Task.WaitAsync(token); } };
        using var queue = new AccountReceiveQueueViewModel(f.Store, f.Receiver, heldCache, new ImmediateUiDispatcher()); queue.UpdateAccounts([target]); queue.QueueAllEnabled(); await receiverEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Selection can change while the network is waiting; it is not an account configuration change.
        await f.Store.SelectAsync(target.Id).WaitAsync(TimeSpan.FromSeconds(5)); releaseReceiver.SetResult(); await cacheEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var change = f.Store.SaveAndSelectAsync(target with { DisplayName = "Changed after publication" }); Assert.False(change.IsCompleted); releaseCache.SetResult();
        await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); await change.WaitAsync(TimeSpan.FromSeconds(5)); Assert.NotNull(await f.Cache.GetLatestMailboxAsync(target.Id, "INBOX")); Assert.Equal("Changed after publication", (await f.Store.LoadStateAsync()).Accounts.Single().DisplayName);
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("oversize")]
    [InlineData("authentication")]
    public async Task ARejectedProviderOrInvalidPageDoesNotWriteOrPreventAnotherAccountsSave(string mode)
    {
        using var f = await Fixture.Create(2); var bad = f.Profiles[0].Id;
        f.Receiver.Get = (profile, _) =>
        {
            if (profile.Id != bad) return Task.FromResult(Page(profile));
            if (mode == "authentication") throw new MailConnectionException("Fixture sign-in rejected.");
            var page = Page(profile); return Task.FromResult(mode switch
            {
                "foreign" => Page(profile with { Id = AccountId.New() }), "missing" => page with { Mailbox = null },
                "duplicate" => page with { Messages = [page.Messages[0], page.Messages[0]] },
                _ => page with { Messages = Enumerable.Range(1, 51).Select(uid => page.Messages[0] with { Key = page.Messages[0].Key with { Uid = (uint)uid } }).ToArray() },
            });
        };
        f.Queue.QueueAllEnabled(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(AccountReceiveState.Failed, f.Queue.Jobs[0].State); Assert.Equal(AccountReceiveState.Completed, f.Queue.Jobs[1].State); Assert.Null(await f.Cache.GetLatestMailboxAsync(bad, "INBOX")); Assert.NotNull(await f.Cache.GetLatestMailboxAsync(f.Profiles[1].Id, "INBOX")); Assert.Equal(1, f.Receiver.Calls(bad));
    }
    [Fact]
    public async Task CacheFailureIsPerAccountAndRetryRequiresAnotherExplicitRequest()
    {
        using var f = await Fixture.Create(2); bool fail = true; var bad = f.Profiles[0].Id;
        var cache = new CacheProxy(f.Cache) { BeforeSave = (box, _) => box.AccountId == bad && fail ? throw new IOException("Fixture disk-full") : Task.CompletedTask };
        using var queue = new AccountReceiveQueueViewModel(f.Store, f.Receiver, cache, new ImmediateUiDispatcher()); queue.UpdateAccounts(f.Profiles); queue.QueueAllEnabled(); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AccountReceiveState.Failed, queue.Jobs[0].State); Assert.Equal(AccountReceiveState.Completed, queue.Jobs[1].State); Assert.Equal(1, f.Receiver.Calls(bad));
        fail = false; Assert.True(queue.QueueAccount(bad)); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(2, f.Receiver.Calls(bad)); Assert.Equal(AccountReceiveState.Completed, queue.Jobs[0].State);
    }
    [Fact]
    public async Task DisposeCancelsAllWorkAndQueuedCallbacksCannotPaintLateResultsOrStartPendingAccounts()
    {
        var dispatcher = new TestQueueDispatcher(); using var f = await Fixture.Create(3, dispatcher); int enteredCount = 0; var entered = NewSource(); var release = NewSource();
        f.Receiver.Get = async (profile, _) => { if (Interlocked.Increment(ref enteredCount) == 2) entered.TrySetResult(); await release.Task; return Page(profile); };
        int painted = 0; f.Queue.Changed += (_, _) => painted++; f.Queue.QueueAllEnabled(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); f.Queue.Dispose(); release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5)); dispatcher.Drain();
        Assert.Equal(0, painted); Assert.Equal(0, f.Receiver.Calls(f.Profiles[2].Id)); foreach (var profile in f.Profiles) Assert.Null(await f.Cache.GetLatestMailboxAsync(profile.Id, "INBOX"));
    }
    [Fact]
    public async Task RosterAndCoalescingBoundsHoldAndSenderOverridesMatchAfterJsonReload()
    {
        using var f = await Fixture.Create(32); var release = NewSource(); f.Receiver.Get = async (profile, token) => { await release.Task.WaitAsync(token); return Page(profile); };
        var updated = f.Profiles[0] with { SenderIdentities = [new() { Address = f.Profiles[0].EmailAddress, DisplayName = "Pinned sender" }] }; await f.Store.SaveAndSelectAsync(updated); f.Queue.UpdateAccounts((await f.Store.LoadStateAsync()).Accounts);
        Assert.Equal(32, f.Queue.QueueAllEnabled()); Assert.Equal(0, f.Queue.QueueAllEnabled()); Assert.Equal(32, f.Queue.Jobs.Count); Assert.Throws<ArgumentException>(() => f.Queue.UpdateAccounts(f.Profiles.Append(TestDirectory.Profile()).ToArray()));
        release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(15)); Assert.All(f.Queue.Jobs, job => Assert.Equal(AccountReceiveState.Completed, job.State)); Assert.InRange(f.Receiver.MaximumActive, 1, 2);
    }
    [Theory]
    [InlineData(TransportSecurity.Tls)]
    [InlineData(TransportSecurity.StartTls)]
    public async Task ProductionTlsAdaptersUseSeparateImapCredentialBindingsAndOneRejectedAccountDoesNotBlockTheOther(TransportSecurity security)
    {
        using var dir = new TestDirectory(); await using var rejected = new LocalImapServer(security, rejectAuthentication: true); await using var accepted = new LocalImapServer(security); accepted.Messages.Add(new(1, "fixture"));
        var first = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = rejected.Port, UserName = "test", Security = security } };
        var second = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = accepted.Port, UserName = "test", Security = security } };
        var raw = new JsonAccountCollectionStore(dir.Root); await raw.SaveAndSelectAsync(first); await raw.SaveAndSelectAsync(second); await raw.SelectAsync(first.Id);
        var credentials = new RecordingCredentials(); credentials.Values[CredentialKey.For(first, MailProtocol.Imap)] = LocalImapServer.Password; credentials.Values[CredentialKey.For(second, MailProtocol.Imap)] = LocalImapServer.Password;
        var receiver = new ImapMailReceiver(credentials, (_, certificate, _, _) => certificate?.GetCertHashString() is {} hash && (hash == accepted.Certificate.GetCertHashString() || hash == rejected.Certificate.GetCertHashString()), TimeSpan.FromSeconds(10));
        var cache = new SqliteMailCache(dir.File("mail.db")); using var queue = new AccountReceiveQueueViewModel(new(raw), receiver, cache, new ImmediateUiDispatcher()); queue.UpdateAccounts([first, second]); queue.QueueAllEnabled(); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Equal(AccountReceiveState.Failed, queue.Jobs[0].State); Assert.True(queue.Jobs[1].State == AccountReceiveState.Completed, queue.Jobs[1].Detail); Assert.Null(await cache.GetLatestMailboxAsync(first.Id, "INBOX")); Assert.NotNull(await cache.GetLatestMailboxAsync(second.Id, "INBOX"));
        Assert.Equal(2, credentials.Reads.Count); Assert.Contains(CredentialKey.For(first, MailProtocol.Imap), credentials.Reads); Assert.Contains(CredentialKey.For(second, MailProtocol.Imap), credentials.Reads); Assert.All(credentials.Reads, key => Assert.Equal(MailProtocol.Imap, key.Protocol));
        Assert.All(accepted.MailCommands, command => Assert.True(command.StartsWith("EXAMINE ") || command.StartsWith("FETCH "), command)); Assert.Empty(rejected.MailCommands);
    }
    private sealed class RecordingCredentials : ICredentialStore
    {
        public readonly Dictionary<CredentialKey, string> Values = []; public readonly ConcurrentQueue<CredentialKey> Reads = new();
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) { Reads.Enqueue(key); return Task.FromResult(Values.TryGetValue(key, out var value) ? value : null); }
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) => throw new InvalidOperationException("Queue must not write credentials.");
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("Queue must not delete credentials.");
    }
    [Theory]
    [InlineData("line")]
    [InlineData("count")]
    public async Task OrdinaryHeaderPagesUseBoundedMetadataResponsesBeforeAQueueResultCanBePublished(string mode)
    {
        using var dir = new TestDirectory(); await using var server = new LocalImapServer(TransportSecurity.Tls); server.Messages.Add(new(1, "fixture"));
        server.HeaderResponseOverride = mode == "line" ? "* 1 FETCH (UID 1 ENVELOPE (\"" + new string('x', 128 * 1024) : string.Concat(Enumerable.Repeat("* OK unsolicited\r\n", 36));
        var profile = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } }; var raw = new JsonAccountCollectionStore(dir.Root); await raw.SaveAndSelectAsync(profile);
        var credentials = new RecordingCredentials(); credentials.Values[CredentialKey.For(profile, MailProtocol.Imap)] = LocalImapServer.Password;
        var receiver = new ImapMailReceiver(credentials, (_, certificate, _, _) => certificate?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(8)); var cache = new SqliteMailCache(dir.File("mail.db"));
        using var queue = new AccountReceiveQueueViewModel(new(raw), receiver, cache, new ImmediateUiDispatcher()); queue.UpdateAccounts([profile]); queue.QueueAllEnabled(); await queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AccountReceiveState.Failed, queue.Jobs.Single().State); Assert.Null(await cache.GetLatestMailboxAsync(profile.Id, "INBOX")); Assert.Single(credentials.Reads);
    }
    internal static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Fact]
    public async Task RepeatedQueuedCancellationKeepsOnePendingSlotAndNeverReplaysCanceledRequests()
    {
        using var f = await Fixture.Create(3); var entered = NewSource(); var release = NewSource(); int held = 0; var target = f.Profiles[2].Id;
        f.Receiver.Get = async (profile, token) => { if (profile.Id != target) { if (Interlocked.Increment(ref held) == 2) entered.TrySetResult(); await release.Task.WaitAsync(token); } return Page(profile); };
        f.Queue.QueueAllEnabled(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int index = 0; index < 256; index++) { f.Queue.Cancel(target); Assert.Equal(0, f.Queue.PendingCount); Assert.True(f.Queue.QueueAccount(target)); Assert.Equal(1, f.Queue.PendingCount); }
        release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, f.Receiver.Calls(target)); Assert.Equal(0, f.Queue.PendingCount);
    }
    [Fact]
    public async Task ReplacingTheRosterPrunesRemovedQueuedAccountsAndDoesNotLeakTheirCapacityOrLateResults()
    {
        using var f = await Fixture.Create(4); var entered = NewSource(); var release = NewSource(); int held = 0; var oldIds = f.Profiles.Select(profile => profile.Id).ToHashSet();
        f.Receiver.Get = async (profile, _) => { if (oldIds.Contains(profile.Id)) { if (Interlocked.Increment(ref held) == 2) entered.TrySetResult(); await release.Task; } return Page(profile); };
        f.Queue.QueueAllEnabled(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); foreach (var profile in f.Profiles) await f.Store.RemoveAsync(profile.Id);
        var replacement = Enumerable.Range(1, 32).Select(index => TestDirectory.Profile() with { DisplayName = $"Replacement {index}", EmailAddress = $"replacement{index}@example.test" }).ToArray(); foreach (var profile in replacement) await f.Store.SaveAndSelectAsync(profile); f.Queue.UpdateAccounts(replacement);
        Assert.Equal(0, f.Queue.PendingCount); Assert.Equal(32, f.Queue.QueueAllEnabled()); Assert.Equal(32, f.Queue.PendingCount); release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.All(f.Queue.Jobs, job => Assert.Equal(AccountReceiveState.Completed, job.State)); Assert.Equal(0, f.Receiver.Calls(f.Profiles[2].Id)); Assert.Equal(0, f.Receiver.Calls(f.Profiles[3].Id)); foreach (var profile in f.Profiles) Assert.Null(await f.Cache.GetLatestMailboxAsync(profile.Id, "INBOX"));
    }
    [Fact]
    public async Task RepeatedRequestsCoalesceUiNotificationsUntilTheDispatcherCanRefreshCurrentState()
    {
        var dispatcher = new TestQueueDispatcher(); using var f = await Fixture.Create(3, dispatcher); dispatcher.Drain(); var entered = NewSource(); var release = NewSource(); int held = 0, painted = 0; var target = f.Profiles[2].Id;
        f.Receiver.Get = async (profile, token) => { if (profile.Id != target) { if (Interlocked.Increment(ref held) == 2) entered.TrySetResult(); await release.Task.WaitAsync(token); } return Page(profile); };
        f.Queue.Changed += (_, _) => painted++; f.Queue.QueueAllEnabled(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int index = 0; index < 256; index++) { f.Queue.Cancel(target); f.Queue.QueueAccount(target); }
        Assert.Equal(0, painted); dispatcher.Drain(); Assert.Equal(1, painted); Assert.Equal(1, f.Queue.PendingCount);
        release.SetResult(); await f.Queue.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10)); dispatcher.Drain(); Assert.All(f.Queue.Jobs, job => Assert.Equal(AccountReceiveState.Completed, job.State));
    }
    internal static MailInboxPage Page(AccountProfile profile) => new([new() { Key = new(profile.Id, "INBOX", 7, 1), Sender = profile.EmailAddress, Subject = "Received page" }], null) { Mailbox = new(profile.Id, "INBOX", 7) };
    internal sealed class Receiver : IMailReceiver
    {
        public readonly ConcurrentQueue<AccountId> Started = new(); private int _active; public int MaximumActive; private readonly object _sync = new();
        public Func<AccountProfile, CancellationToken, Task<MailInboxPage>>? Get;
        public int Calls(AccountId id) => Started.Count(value => value == id);
        public async Task<MailInboxPage> GetInboxAsync(AccountProfile profile, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal(50, count); Assert.Null(older); Started.Enqueue(profile.Id); int active = Interlocked.Increment(ref _active); lock (_sync) MaximumActive = Math.Max(MaximumActive, active);
            try { return Get is null ? Page(profile) : await Get(profile, cancellationToken); } finally { Interlocked.Decrement(ref _active); }
        }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No connection test.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No body download in account queue.");
    }
    internal sealed class CacheProxy(IMailCache inner) : IMailCache
    {
        public Func<MailboxKey, CancellationToken, Task>? BeforeSave;
        public async Task SaveSummariesAsync(MailboxKey mailbox, IReadOnlyList<MailMessageSummary> messages, DateTimeOffset receivedAt, CancellationToken token = default) { if (BeforeSave is not null) await BeforeSave(mailbox, token); await inner.SaveSummariesAsync(mailbox, messages, receivedAt, token); }
        public Task<CachedMailbox?> GetLatestMailboxAsync(AccountId account, string mailboxId, CancellationToken token = default) => inner.GetLatestMailboxAsync(account, mailboxId, token);
        public Task<IReadOnlyList<MailMessageSummary>> ReadSummariesAsync(MailboxKey mailbox, int maximumCount, uint? afterUid = null, CancellationToken token = default) => inner.ReadSummariesAsync(mailbox, maximumCount, afterUid, token);
        public Task<MailMessageBody?> ReadBodyAsync(MailboxKey mailbox, uint uid, CancellationToken token = default) => inner.ReadBodyAsync(mailbox, uid, token);
        public Task SaveBodyAsync(MailboxKey mailbox, MailMessageBody body, CancellationToken token = default) => inner.SaveBodyAsync(mailbox, body, token);
    }
    internal sealed class Fixture : IDisposable
    {
        public readonly TestDirectory Directory = new(); public AccountProfile[] Profiles = []; public CoordinatedAccountCollectionStore Store = null!; public SqliteMailCache Cache = null!; public readonly Receiver Receiver = new(); public AccountReceiveQueueViewModel Queue = null!;
        public static async Task<Fixture> Create(int count, IUiDispatcher? dispatcher = null)
        {
            var f = new Fixture(); var raw = new JsonAccountCollectionStore(f.Directory.Root); f.Store = new(raw); f.Cache = new(f.Directory.File("mail.db"));
            f.Profiles = Enumerable.Range(1, count).Select(index => TestDirectory.Profile() with { DisplayName = $"Account {index}", EmailAddress = $"account{index}@example.test" }).ToArray(); foreach (var profile in f.Profiles) await f.Store.SaveAndSelectAsync(profile); await f.Store.SelectAsync(f.Profiles[0].Id);
            f.Queue = new(f.Store, f.Receiver, f.Cache, dispatcher ?? new ImmediateUiDispatcher()); f.Queue.UpdateAccounts(f.Profiles); return f;
        }
        public void Dispose() { Queue.Dispose(); Directory.Dispose(); }
    }
}
