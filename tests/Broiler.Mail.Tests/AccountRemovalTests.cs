using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class AccountRemovalTests
{
    [Fact]
    public async Task ParkedDraftOwnerCannotBeRemovedOrHaveCredentialsDeleted()
    {
        using var f = await Fixture.Create(); var drafts = new JsonDraftStore(f.Profile.DraftsPath);
        var state = await drafts.SaveAsync(0, new() { Draft = MailComposition.Create(f.Second), ToText = "", CcText = "", BccText = "", State = DraftSubmissionState.Editing });
        await drafts.ParkAsync(state.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }));
        Assert.Equal(2, (await f.Store.LoadAsync()).Count); Assert.Empty(f.Credentials.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryBackedRemovalKeepsMailAndOnlyForgetsTheChosenAccountSlots(bool forget)
    {
        using var f = await Fixture.Create(); string? backup = null;
        await f.Service.RemoveAsync(f.Second, forget, path => backup = path);
        Assert.Equal(f.First.Id, Assert.Single(await f.Store.LoadAsync()).Id);
        Assert.Equal("Body B", (await f.Cache.ReadBodyAsync(new(f.Second.Id, "INBOX", 7), 1))!.PlainText);
        Assert.Equal("A secret", await f.Credentials.ReadAsync(CredentialKey.For(f.First, MailProtocol.Imap)));
        Assert.Equal(forget ? null : "B IMAP", await f.Credentials.ReadAsync(CredentialKey.For(f.Second, MailProtocol.Imap)));
        Assert.Equal(forget ? null : "B SMTP", await f.Credentials.ReadAsync(f.SecondSmtpKey));
        Assert.NotNull(backup);
        string restored = await f.Storage.RestoreBackupAsync(backup, f.Directory.File("restored"));
        var restoredState = await new JsonAccountCollectionStore(restored).LoadStateAsync();
        Assert.Equal(2, restoredState.Accounts.Length); Assert.Equal(f.Second.Id, restoredState.SelectedAccountId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedOrUnreadableDraftPreventsRemovalBeforeAnySecretChanges(bool corrupt)
    {
        using var f = await Fixture.Create();
        if (corrupt) await File.WriteAllTextAsync(f.Profile.DraftsPath, "{invalid");
        else await new JsonDraftStore(f.Profile.DraftsPath).SaveAsync(0, new() { Draft = MailComposition.Create(f.Second), ToText = "", CcText = "", BccText = "" });
        if (corrupt) await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }));
        Assert.Empty(f.Credentials.Deleted); Assert.Equal(2, (await f.Store.LoadAsync()).Count);
    }

    [Fact]
    public async Task PartialPasswordFailureLeavesAccountListedAndRetryIsIdempotent()
    {
        using var f = await Fixture.Create(); f.Credentials.FailSmtpDelete = true; string? backup = null;
        var failure = await Assert.ThrowsAsync<IOException>(() => f.Service.RemoveAsync(f.Second, true, path => backup = path));
        Assert.Contains("account remains listed", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(backup); Assert.Equal(2, (await f.Store.LoadAsync()).Count);
        Assert.Null(await f.Credentials.ReadAsync(CredentialKey.For(f.Second, MailProtocol.Imap)));
        Assert.Equal("B SMTP", await f.Credentials.ReadAsync(f.SecondSmtpKey));
        f.Credentials.FailSmtpDelete = false; await f.Service.RemoveAsync(f.Second, true, _ => { });
        Assert.Single(await f.Store.LoadAsync()); Assert.Null(await f.Credentials.ReadAsync(f.SecondSmtpKey));
    }

    [Fact]
    public async Task CancellationAfterForgettingOneSlotDoesNotRemoveConfiguration()
    {
        using var f = await Fixture.Create(); using var cancellation = new CancellationTokenSource();
        f.Credentials.AfterDelete = key => { if (key.Protocol == MailProtocol.Imap) cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }, cancellation.Token));
        Assert.Equal(2, (await f.Store.LoadAsync()).Count); Assert.Null(await f.Credentials.ReadAsync(CredentialKey.For(f.Second, MailProtocol.Imap)));
        Assert.Equal("B SMTP", await f.Credentials.ReadAsync(f.SecondSmtpKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedConfirmationIsRejectedBeforeAnySecretCleanup(bool forget)
    {
        using var f = await Fixture.Create(); var changed = f.Second with { DisplayName = "Changed after backup" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RemoveAsync(f.Second, forget, _ => f.Store.SaveAsync(changed).GetAwaiter().GetResult()));
        Assert.Contains(changed, await f.Store.LoadAsync()); Assert.Empty(f.Credentials.Deleted);
    }

    [Fact]
    public async Task FailedSnapshotPreventsConfigurationAndCredentialChanges()
    {
        using var f = await Fixture.Create(); await File.WriteAllTextAsync(f.Profile.SettingsPath, "{invalid");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }));
        Assert.Equal(2, (await f.Store.LoadAsync()).Count); Assert.Empty(f.Credentials.Deleted);
    }

    [Fact]
    public async Task SenderOverrideCollectionsRemainComparableAfterPersistenceAndCanBeRemoved()
    {
        using var f = await Fixture.Create();
        f.Second = f.Second with { SenderIdentities = [new() { Address = f.Second.EmailAddress, DisplayName = "Pinned sender", Signature = "Saved signature" }] };
        await f.Store.SaveAndSelectAsync(f.Second);
        var reloaded = (await new JsonAccountCollectionStore(f.Directory.Root).LoadStateAsync()).Accounts.Single(row => row.Id == f.Second.Id);
        Assert.NotSame(f.Second.SenderIdentities, reloaded.SenderIdentities); Assert.True(AccountProfileConfiguration.Matches(f.Second, reloaded));
        await f.Service.RemoveAsync(reloaded, false, _ => { }); Assert.Single(await f.Store.LoadAsync()); Assert.Empty(f.Credentials.Deleted);
    }

    [Fact]
    public async Task ChangedSenderOverrideAfterBackupRefusesRemovalBeforeDeletingPasswords()
    {
        using var f = await Fixture.Create();
        f.Second = f.Second with { SenderIdentities = [new() { Address = f.Second.EmailAddress, Signature = "Original" }] }; await f.Store.SaveAndSelectAsync(f.Second);
        var changed = f.Second with { SenderIdentities = [new() { Address = f.Second.EmailAddress, Signature = "Changed" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RemoveAsync(f.Second, true, _ => f.Store.SaveAsync(changed).GetAwaiter().GetResult()));
        Assert.Empty(f.Credentials.Deleted); Assert.Equal("Changed", (await f.Store.LoadStateAsync()).Accounts.Single(row => row.Id == f.Second.Id).SenderIdentities![0].Signature);
    }

    [Theory]
    [InlineData("uncertain")]
    [InlineData("confirmed")]
    [InlineData("active")]
    [InlineData("active-case")]
    [InlineData("unattributed")]
    public async Task UnresolvedChangesBlockRemovalBeforeBackupOrPasswordDeletion(string mode)
    {
        using var f = await Fixture.Create(); var key = new MailMessageKey(f.Second.Id, "INBOX", 7, 1);
        var journal = new MutationIntentJournal(f.Profile.CachePath); IDisposable? active = null; Guid? intent = null;
        try
        {
            if (mode == "confirmed") await journal.Receipts.RecordAsync(key, null);
            else if (mode is "active" or "active-case") active = new MutationIntentJournal(mode == "active-case" && OperatingSystem.IsWindows() ? f.Profile.CachePath.ToUpperInvariant() : f.Profile.CachePath).Enter(key);
            else { intent = await journal.BeginAsync(key, null); if (mode == "unattributed") await journal.CompleteAsync(intent.Value, keepPending: true); }
            bool backedUp = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RemoveAsync(f.Second, true, _ => backedUp = true));
            Assert.False(backedUp); Assert.Equal(2, (await f.Store.LoadAsync()).Count); Assert.Empty(f.Credentials.Deleted);
            Assert.Equal("B IMAP", await f.Credentials.ReadAsync(CredentialKey.For(f.Second, MailProtocol.Imap)));
        }
        finally { active?.Dispose(); if (intent is {} id) await journal.CompleteAsync(id); }
    }

    [Fact]
    public async Task AnotherAccountsAttributedIntentDoesNotBlockAnUnrelatedRemoval()
    {
        using var f = await Fixture.Create(); var journal = new MutationIntentJournal(f.Profile.CachePath);
        var intent = await journal.BeginAsync(new(f.First.Id, "INBOX", 7, 1), null);
        try
        {
            await f.Service.RemoveAsync(f.Second, false, _ => { });
            Assert.Equal(f.First.Id, Assert.Single(await f.Store.LoadAsync()).Id); Assert.Empty(f.Credentials.Deleted);
            Assert.Equal(intent, Assert.Single(await journal.ReadAsync()).Id);
        }
        finally { await journal.CompleteAsync(intent); }
    }

    [Fact]
    public async Task PendingWorkAppearingAfterBackupIsRecheckedBeforeForgettingSecrets()
    {
        using var f = await Fixture.Create(); var journal = new MutationIntentJournal(f.Profile.CachePath); Guid? intent = null; string? backup = null;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RemoveAsync(f.Second, true, path =>
            { backup = path; intent = journal.BeginAsync(new(f.Second.Id, "INBOX", 7, 1), null).GetAwaiter().GetResult(); }));
            Assert.NotNull(backup); Assert.True(Directory.Exists(backup)); Assert.Equal(2, (await f.Store.LoadAsync()).Count); Assert.Empty(f.Credentials.Deleted);
        }
        finally { if (intent is {} id) await journal.CompleteAsync(id); }
    }

    [Fact]
    public async Task PendingChangeAfterSecretCleanupKeepsTheAccountListedForRecovery()
    {
        using var f = await Fixture.Create(); var journal = new MutationIntentJournal(f.Profile.CachePath); Guid? intent = null;
        f.Credentials.AfterDelete = key => { if (key.Protocol == MailProtocol.Smtp) intent = journal.BeginAsync(new(f.Second.Id, "INBOX", 7, 1), null).GetAwaiter().GetResult(); };
        try
        {
            var failure = await Assert.ThrowsAsync<IOException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }));
            Assert.Contains("Passwords were forgotten", failure.Message); Assert.Equal(2, (await f.Store.LoadAsync()).Count);
            Assert.Equal(2, f.Credentials.Deleted.Count); Assert.Equal(intent, Assert.Single(await journal.ReadAsync()).Id);
        }
        finally { if (intent is {} id) await journal.CompleteAsync(id); }
    }

    [Fact]
    public async Task CorruptMutationStateFailsClosedBeforeCredentialDeletion()
    {
        using var f = await Fixture.Create(); await File.WriteAllTextAsync(f.Profile.CachePath + ".intents.json", "invalid");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.RemoveAsync(f.Second, true, _ => { }));
        Assert.Equal(2, (await f.Store.LoadAsync()).Count); Assert.Empty(f.Credentials.Deleted);
    }

    private sealed class Fixture : IDisposable
    {
        public TestDirectory Directory = new(); public MailProfile Profile = null!; public SqliteMailCache Cache = null!;
        public JsonAccountCollectionStore Store = null!; public ProfileStorageService Storage = null!; public AccountRemovalService Service = null!;
        public AccountProfile First = TestDirectory.Profile(), Second = TestDirectory.Profile();
        public Credentials Credentials = new(); public CredentialKey SecondSmtpKey = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Profile = MailProfile.Open(f.Directory.Root); f.Cache = new(f.Profile.CachePath);
            f.Store = new(f.Directory.Root); await f.Store.SaveAsync(f.First); await f.Store.SaveAndSelectAsync(f.Second);
            f.Storage = new(f.Profile, f.Cache); f.Service = new(f.Store, f.Storage, f.Credentials);
            f.SecondSmtpKey = CredentialKey.For(f.Second with { OutgoingServer = f.Second.IncomingServer }, MailProtocol.Smtp);
            await f.Credentials.WriteAsync(CredentialKey.For(f.First, MailProtocol.Imap), "A secret");
            await f.Credentials.WriteAsync(CredentialKey.For(f.Second, MailProtocol.Imap), "B IMAP"); await f.Credentials.WriteAsync(f.SecondSmtpKey, "B SMTP");
            var mailbox = new MailboxKey(f.Second.Id, "INBOX", 7);
            await f.Cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
            await f.Cache.SaveBodyAsync(mailbox, new(new(f.Second.Id, "INBOX", 7, 1), "Body B")); return f;
        }
        public void Dispose() { Profile.Dispose(); Directory.Dispose(); }
    }
    private sealed class Credentials : ICredentialStore
    {
        private readonly Dictionary<(AccountId, MailProtocol), (string Binding, string Secret)> _values = [];
        public List<CredentialKey> Deleted = []; public bool FailSmtpDelete; public Action<CredentialKey>? AfterDelete;
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => Task.FromResult(_values.TryGetValue((key.AccountId, key.Protocol), out var value) && value.Binding == key.Binding ? value.Secret : null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) { _values[(key.AccountId, key.Protocol)] = (key.Binding, secret); return Task.CompletedTask; }
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); if (FailSmtpDelete && key.Protocol == MailProtocol.Smtp) throw new IOException("Synthetic vault failure");
            _values.Remove((key.AccountId, key.Protocol)); Deleted.Add(key); AfterDelete?.Invoke(key); return Task.CompletedTask;
        }
    }
}
