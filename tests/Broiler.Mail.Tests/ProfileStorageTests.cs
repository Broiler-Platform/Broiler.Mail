using System.Text.Json.Nodes;
using Broiler.Mail.Application;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class ProfileStorageTests
{
    [Fact]
    public async Task LocalImportedOriginalsSurviveVerifiedBackupRestoreAndCacheCleanup()
    {
        using var fixture = await Fixture.CreateAsync();
        var imports = new JsonLocalMessageOriginals(fixture.Profile.DirectoryPath);
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("Subject: Local only\r\n\r\nIrreplaceable original");
        var receipt = await imports.ImportAsync(fixture.Account.Id, new("original.eml", bytes));
        var cleared = await fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("imports-backup"));
        Assert.Equal(bytes, (await imports.ReadImportAsync(receipt)).Bytes);
        string restored = await fixture.Storage.RestoreBackupAsync(cleared.BackupDirectory, fixture.Directory.File("imports-restored"));
        var restoredStore = new JsonLocalMessageOriginals(restored);
        Assert.Equal(receipt, Assert.Single(await restoredStore.ListImportsAsync(fixture.Account.Id)));
        Assert.Equal(bytes, (await restoredStore.ReadImportAsync(receipt)).Bytes);
        Assert.Equal(0, (await fixture.Storage.GetInfoAsync()).HeaderCount);
    }

    [Fact]
    public async Task AccountNavigationRestoresAndCacheClearPreservesExplicitPreferences()
    {
        using var f = await Fixture.CreateAsync(); var store = new JsonAccountNavigationStore(Path.Combine(f.Profile.DirectoryPath, "account-navigation.json"));
        var row = new Broiler.Mail.Core.Services.AccountNavigation(f.Account.Id, "Archive", ["INBOX", "Archive"]); await store.SaveAsync(row);
        string backup = await f.Storage.CreateBackupAsync(f.Directory.File("navigation-backup"));
        string restored = await f.Storage.RestoreBackupAsync(backup, f.Directory.File("navigation-restored"));
        var saved = await new JsonAccountNavigationStore(Path.Combine(restored, "account-navigation.json")).LoadAsync(f.Account.Id);
        Assert.Equal(row.StartMailboxId, saved.StartMailboxId); Assert.Equal(row.PinnedMailboxIds, saved.PinnedMailboxIds);
        byte[] before = File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath, "account-navigation.json"));
        await f.Storage.ClearDownloadedMailAsync(f.Directory.File("navigation-clear")); Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath, "account-navigation.json")));
    }

    [Fact]
    public async Task CorruptNavigationPreventsSnapshotPublicationAndCacheDeletion()
    {
        using var f = await Fixture.CreateAsync(); string path = Path.Combine(f.Profile.DirectoryPath, "account-navigation.json"); File.WriteAllText(path, "broken");
        string destination = f.Directory.File("bad-navigation-backup"); await Assert.ThrowsAsync<InvalidDataException>(() => f.Storage.ClearDownloadedMailAsync(destination));
        Assert.False(System.IO.Directory.Exists(destination)); Assert.NotNull(await f.Cache.ReadBodyAsync(f.Mailbox, 1)); Assert.Equal("broken", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineCheckpointsRestoreWithSnapshotAndResetBeforeCacheClear(bool complete)
    {
        using var f = await Fixture.CreateAsync(); var store = new JsonOfflineFolderStore(Path.Combine(f.Profile.DirectoryPath, "offline-folders.json"));
        await store.SaveAsync(f.Account.Id, ["INBOX"]);
        MailMessageKey[] remaining = complete ? [] : [new(f.Account.Id, "INBOX", 7, 1)];
        await store.SaveProgressAsync(f.Account.Id, ["INBOX"], remaining);
        var cleared = await f.Storage.ClearDownloadedMailAsync(f.Directory.File("checkpoint-clear"));
        Assert.Equal("INBOX", Assert.Single(await store.LoadAsync(f.Account.Id))); Assert.Null(await store.LoadProgressAsync(f.Account.Id, ["INBOX"]));
        string restored = await f.Storage.RestoreBackupAsync(cleared.BackupDirectory, f.Directory.File("checkpoint-restore"));
        var restoredStore = new JsonOfflineFolderStore(Path.Combine(restored, "offline-folders.json"));
        Assert.Equal(remaining, await restoredStore.LoadProgressAsync(f.Account.Id, ["INBOX"]));
    }

    [Fact]
    public async Task OptionalAccountCollectionSurvivesVerifiedBackupAndRestore()
    {
        using var fixture = await Fixture.CreateAsync();
        var collection = new JsonAccountCollectionStore(fixture.Profile.DirectoryPath); var second = TestDirectory.Profile();
        await new JsonSettingsStore(fixture.Profile.SettingsPath).SaveAsync(new());
        await collection.SaveAsync(second); await collection.SelectAsync(second.Id);
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("collection-backup"));
        string restored = await fixture.Storage.RestoreBackupAsync(backup, fixture.Directory.File("collection-restored"));
        var state = await new JsonAccountCollectionStore(restored).LoadStateAsync();
        Assert.Equal(2, state.Accounts.Length); Assert.Equal(second.Id, state.SelectedAccountId);
        Assert.Equal(fixture.Account, Assert.Single(await new JsonAccountStore(Path.Combine(restored, "accounts.json")).LoadAsync()));
    }

    [Fact]
    public async Task VerifiedLiveBackupRestoresAccountDraftAndOfflineBodyIntoNewProfile()
    {
        using var fixture = await Fixture.CreateAsync();
        string backup = fixture.Directory.File("snapshot"); string restored = fixture.Directory.File("restored");
        await fixture.Storage.CreateBackupAsync(backup);
        Assert.True(File.Exists(Path.Combine(backup, "backup.json")));
        Assert.False(File.Exists(Path.Combine(backup, "credentials.fixture")));
        await fixture.Storage.RestoreBackupAsync(backup, restored);
        using var profile = MailProfile.Open(restored);
        Assert.Equal(fixture.Profile.Manifest.ProfileId, profile.Manifest.ProfileId);
        Assert.Equal(fixture.Account, Assert.Single(await new JsonAccountStore(profile.AccountsPath).LoadAsync()));
        Assert.Equal("Unfinished draft", (await new JsonDraftStore(profile.DraftsPath).LoadAsync()).Draft!.Draft.PlainText);
        Assert.Equal("Saved body", (await new SqliteMailCache(profile.CachePath).ReadBodyAsync(fixture.Mailbox, 1))!.PlainText);
        var info = await fixture.Storage.GetInfoAsync(); Assert.Equal(1, info.HeaderCount); Assert.Equal(1, info.DownloadedBodyCount);
    }

    [Fact]
    public async Task ClearCreatesRestorableBackupAndPreservesDraftConfigurationAndSecrets()
    {
        using var fixture = await Fixture.CreateAsync();
        byte[] draft = File.ReadAllBytes(fixture.Profile.DraftsPath), account = File.ReadAllBytes(fixture.Profile.AccountsPath);
        var cleared = await fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("before-clear"));
        Assert.True(cleared.Compacted);
        Assert.Equal(0, (await fixture.Storage.GetInfoAsync()).HeaderCount);
        Assert.Null(await fixture.Cache.ReadBodyAsync(fixture.Mailbox, 1));
        Assert.Equal(draft, File.ReadAllBytes(fixture.Profile.DraftsPath)); Assert.Equal(account, File.ReadAllBytes(fixture.Profile.AccountsPath));
        Assert.Equal("synthetic local secret", File.ReadAllText(Path.Combine(fixture.Profile.DirectoryPath, "credentials.fixture")));
        string restored = fixture.Directory.File("recovered");
        await fixture.Storage.RestoreBackupAsync(cleared.BackupDirectory, restored);
        Assert.Equal("Saved body", (await new SqliteMailCache(Path.Combine(restored, "mail-cache.db")).ReadBodyAsync(fixture.Mailbox, 1))!.PlainText);
    }

    [Fact]
    public async Task ExistingDestinationAndFailedBackupNeverClearCurrentMail()
    {
        using var fixture = await Fixture.CreateAsync(); string existing = fixture.Directory.File("existing"); Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");
        await Assert.ThrowsAsync<IOException>(() => fixture.Storage.ClearDownloadedMailAsync(existing));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        Assert.Equal("Saved body", (await fixture.Cache.ReadBodyAsync(fixture.Mailbox, 1))!.PlainText);
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("snapshot"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Storage.RestoreBackupAsync(backup, existing));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("traversal")]
    [InlineData("future")]
    public async Task InvalidBackupDoesNotPublishDestinationOrLeavePartialFiles(string corruption)
    {
        using var fixture = await Fixture.CreateAsync(); string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("snapshot"));
        string manifestPath = Path.Combine(backup, "backup.json");
        if (corruption == "checksum") File.AppendAllText(Path.Combine(backup, "accounts.json"), "tamper");
        else
        {
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            if (corruption == "traversal") manifest["Files"]![0]!["Name"] = "../escape.json";
            else manifest["SchemaVersion"] = 999;
            File.WriteAllText(manifestPath, manifest.ToJsonString());
        }
        string destination = fixture.Directory.File("restore-refused");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Storage.RestoreBackupAsync(backup, destination));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Directory.Root, ".mail-pending-*"));
        Assert.Equal("Saved body", (await fixture.Cache.ReadBodyAsync(fixture.Mailbox, 1))!.PlainText);
    }

    [Fact]
    public async Task CancellationAndCorruptDraftPreventClearBeforeDeletion()
    {
        using var fixture = await Fixture.CreateAsync(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("cancelled"), cancellation.Token));
        Assert.False(Directory.Exists(fixture.Directory.File("cancelled")));
        File.WriteAllText(fixture.Profile.DraftsPath, "broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("refused")));
        Assert.Equal("Saved body", (await fixture.Cache.ReadBodyAsync(fixture.Mailbox, 1))!.PlainText);
        Assert.Equal("broken", File.ReadAllText(fixture.Profile.DraftsPath));
    }

    [Fact]
    public async Task CleanupRequiresConfirmationAndCancelsLateReceiveBeforeItCanRepopulateCache()
    {
        using var fixture = await Fixture.CreateAsync();
        var late = new TaskCompletionSource<MailInboxPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestMailReceiver { Inbox = (_, _) => { started.SetResult(); return late.Task; } };
        using var application = new MailApplication(new JsonAccountStore(fixture.Profile.AccountsPath), new JsonSettingsStore(fixture.Profile.SettingsPath),
            receiver, new UnusedSender(), new TestCredentialStore(), cache: fixture.Cache, storage: fixture.Storage);
        await application.InitializeAsync(); var model = application.CreateViewModel(new ImmediateUiDispatcher());
        using var inbox = model.Inbox; using var storage = model.Settings.Storage!;
        await inbox.CacheLoadTask; storage.BackupDestination = fixture.Directory.File("before-clear");
        await storage.ClearAsync(); Assert.Single(await fixture.Cache.ReadSummariesAsync(fixture.Mailbox, 50));
        var receiving = inbox.ReceiveAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        storage.RequestClear(); await storage.ClearAsync(); await inbox.CacheLoadTask;
        Assert.False(storage.IsError, storage.Status); Assert.Empty(inbox.Messages);
        late.SetResult(new([SqliteMailCacheTests.Summary(fixture.Mailbox, 99)], null) { Mailbox = fixture.Mailbox });
        await receiving;
        Assert.Empty(await fixture.Cache.ReadSummariesAsync(fixture.Mailbox, 50)); Assert.Empty(inbox.Messages);
    }

    [Fact]
    public async Task BackupFlushesLatestDraftAndTemporarilyPreventsEditingOrDiscardingIt()
    {
        using var fixture = await Fixture.CreateAsync();
        using var application = new MailApplication(new JsonAccountStore(fixture.Profile.AccountsPath), new JsonSettingsStore(fixture.Profile.SettingsPath),
            new TestMailReceiver(), new UnusedSender(), new TestCredentialStore(), new JsonDraftStore(fixture.Profile.DraftsPath),
            cache: fixture.Cache, storage: fixture.Storage);
        await application.InitializeAsync(); var model = application.CreateViewModel(new ImmediateUiDispatcher());
        using var inbox = model.Inbox; using var composer = model.Composer; using var storage = model.Settings.Storage!;
        await inbox.CacheLoadTask;
        composer.Edit(composer.To, composer.Cc, composer.Bcc, composer.Subject, "Latest draft edit included in backup");
        var flush = storage.SaveDraftBeforeSnapshot!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.SaveDraftBeforeSnapshot = async () => { entered.SetResult(); await release.Task; return await flush(); };
        storage.BackupDestination = fixture.Directory.File("latest-draft-backup");
        var backingUp = storage.BackupAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.False(composer.CanEdit); Assert.False(composer.CanDiscard); Assert.True(storage.IsSnapshotBusy); }
        finally { release.TrySetResult(); }
        await backingUp;
        Assert.False(storage.IsError, storage.Status); Assert.True(composer.CanEdit);
        string restored = await fixture.Storage.RestoreBackupAsync(storage.RestoreSource, fixture.Directory.File("latest-draft-restored"));
        Assert.Equal("Latest draft edit included in backup", (await new JsonDraftStore(Path.Combine(restored, "drafts.json")).LoadAsync()).Draft!.Draft.PlainText);
    }

    [Fact]
    public async Task DerivedSearchIndexIsExcludedFromBackupAndRemovedByClear()
    {
        using var fixture = await Fixture.CreateAsync(); await fixture.Storage.RebuildSearchIndexAsync();
        Assert.True((await fixture.Storage.GetSearchIndexInfoAsync()).Ready);
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("index-backup"));
        Assert.Empty(Directory.GetFiles(backup, "*.search.db", SearchOption.AllDirectories));
        string restored = await fixture.Storage.RestoreBackupAsync(backup, fixture.Directory.File("index-restored"));
        var cache = new SqliteMailCache(Path.Combine(restored, "mail-cache.db"));
        Assert.False((await cache.GetSearchIndexInfoAsync()).Ready); await cache.RebuildSearchIndexAsync(); Assert.True((await cache.GetSearchIndexInfoAsync()).Ready);
        await fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("index-clear"));
        Assert.False(File.Exists(fixture.Profile.CachePath + ".search.db")); Assert.Equal(0, (await fixture.Storage.GetInfoAsync()).HeaderCount);
    }

    [Fact]
    public async Task SavedRecipientsSurviveVerifiedBackupRestoreAndCacheClear()
    {
        using var fixture = await Fixture.CreateAsync();
        var book = new JsonRecipientBook(Path.Combine(fixture.Profile.DirectoryPath, "recipients.json"));
        await book.SaveAsync(new(fixture.Account.Id, "saved@example.test", "Saved recipient"));
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("recipient-backup"));
        Assert.True(File.Exists(Path.Combine(backup, "recipients.json")));
        string restored = await fixture.Storage.RestoreBackupAsync(backup, fixture.Directory.File("recipient-restored"));
        Assert.Equal("Saved recipient", (await new JsonRecipientBook(Path.Combine(restored, "recipients.json")).LoadAsync(fixture.Account.Id)).Single().Name);
        await fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("recipient-clear"));
        Assert.Single(await book.LoadAsync(fixture.Account.Id)); Assert.Equal(0, (await fixture.Storage.GetInfoAsync()).HeaderCount);
    }

    [Fact]
    public async Task MultipleDraftsSurviveBackupRestoreAndCacheClear()
    {
        using var fixture = await Fixture.CreateAsync(); var drafts = new JsonDraftStore(fixture.Profile.DraftsPath);
        var state = await drafts.LoadAsync(); var id = state.Draft!.Draft.Id; state = await drafts.ParkAsync(state.Revision);
        var second = new Broiler.Mail.Core.Messages.DraftSnapshot { Draft = MailComposition.Create(fixture.Account) with { PlainText = "Second body" }, ToText = "second@example.test", CcText = "", BccText = "", State = DraftSubmissionState.Editing };
        await drafts.SaveAsync(state.Revision, second);
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("library-backup"));
        string restored = await fixture.Storage.RestoreBackupAsync(backup, fixture.Directory.File("library-restored"));
        var recovered = await new JsonDraftStore(Path.Combine(restored, "drafts.json")).LoadAsync(); Assert.Equal(second.Draft.Id, recovered.Draft!.Draft.Id); Assert.Equal(id, recovered.SavedDrafts!.Single().Draft.Id);
        await fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("library-clear")); Assert.Equal(id, (await drafts.LoadAsync()).SavedDrafts!.Single().Draft.Id);
    }

    [Fact]
    public async Task SenderIdentityOverridesSurviveVerifiedProfileBackupRestore()
    {
        using var fixture = await Fixture.CreateAsync(); var accounts = new JsonAccountCollectionStore(fixture.Profile.DirectoryPath);
        await accounts.SaveAsync(fixture.Account with { SenderAliases = "alias@example.test", SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Alias Name", Signature = "Alias signature", ReplySignature = "" }] });
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("identity-backup"));
        string restored = await fixture.Storage.RestoreBackupAsync(backup, fixture.Directory.File("identity-restored"));
        var profile = (await new JsonAccountCollectionStore(restored).LoadAsync()).Single(); Assert.Equal("Alias Name", profile.SenderDisplayName("alias@example.test")); Assert.Equal("", profile.SenderSignature("alias@example.test", true));
    }

    [Fact]
    public async Task OfflineFolderSelectionsSurviveVerifiedBackupRestoreAndCacheClear()
    {
        using var fixture = await Fixture.CreateAsync(); var selections = new JsonOfflineFolderStore(Path.Combine(fixture.Profile.DirectoryPath, "offline-folders.json"));
        await selections.SaveAsync(fixture.Account.Id, ["INBOX", "Archive"]);
        string backup = await fixture.Storage.CreateBackupAsync(fixture.Directory.File("offline-folders-backup"));
        string restored = await fixture.Storage.RestoreBackupAsync(backup, fixture.Directory.File("offline-folders-restored"));
        Assert.Equal(new[] { "INBOX", "Archive" }, await new JsonOfflineFolderStore(Path.Combine(restored, "offline-folders.json")).LoadAsync(fixture.Account.Id));
        await fixture.Storage.ClearDownloadedMailAsync(fixture.Directory.File("offline-folders-clear")); Assert.Equal(2, (await selections.LoadAsync(fixture.Account.Id)).Count);
    }

    private sealed class UnusedSender : IMailSender
    { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No send is part of local storage maintenance."); }

    private sealed class Fixture : IDisposable
    {
        public TestDirectory Directory { get; } = new();
        public required MailProfile Profile { get; init; }
        public required SqliteMailCache Cache { get; init; }
        public required ProfileStorageService Storage { get; init; }
        public required AccountProfile Account { get; init; }
        public MailboxKey Mailbox => new(Account.Id, "INBOX", 7);
        private Fixture(TestDirectory directory) { Directory = directory; }
        public static async Task<Fixture> CreateAsync()
        {
            var directory = new TestDirectory(); var profile = MailProfile.Open(directory.File("profile"));
            var cache = new SqliteMailCache(profile.CachePath); var account = TestDirectory.Profile();
            await new JsonAccountStore(profile.AccountsPath).SaveAsync(account);
            await new JsonDraftStore(profile.DraftsPath).SaveAsync(0, new() { Draft = MailComposition.Create(account) with { PlainText = "Unfinished draft" },
                ToText = "raw recipient", CcText = "", BccText = "", State = DraftSubmissionState.Editing });
            var mailbox = new MailboxKey(account.Id, "INBOX", 7);
            await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
            await cache.SaveBodyAsync(mailbox, new(SqliteMailCacheTests.Key(mailbox, 1), "Saved body"));
            File.WriteAllText(Path.Combine(profile.DirectoryPath, "credentials.fixture"), "synthetic local secret");
            return new(directory) { Profile = profile, Cache = cache, Storage = new(profile, cache), Account = account };
        }
        public void Dispose() { Profile.Dispose(); Directory.Dispose(); }
    }
}
