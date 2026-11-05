using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Broiler.Mail.Tests;

public sealed class AccountMailCleanupTests
{
    [Fact]
    public async Task InspectionCountsOnlyOneAccountIncludingOldEpochsWithoutChangingFiles()
    {
        using var f = await Fixture.Create(); byte[] before = File.ReadAllBytes(f.Profile.CachePath);
        var info = await f.Storage.GetAccountDownloadedMailInfoAsync(f.One.Id);
        Assert.Equal(f.One.Id, info.AccountId); Assert.Equal(3, info.HeaderCount); Assert.Equal(3, info.DownloadedBodyCount); Assert.Equal(2, info.MailboxCount);
        Assert.Equal(before, File.ReadAllBytes(f.Profile.CachePath)); var absent = await f.Storage.GetAccountDownloadedMailInfoAsync(AccountId.New()); Assert.Equal(0, absent.HeaderCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryBackedClearKeepsOtherAccountsConfigurationDraftsAndPreferences(bool indexed)
    {
        using var f = await Fixture.Create(); if (indexed) await f.Cache.RebuildSearchIndexAsync();
        var preserved = new[] { "account-collection.json", "drafts.json", "settings.json", "account-navigation.json", "recipients.json", "credential.fixture" }.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath, name)));
        string? announced = null; var result = await f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, f.Directory.File("before-account-clear"), path => announced = path);
        Assert.Equal(result.BackupDirectory, announced); Assert.Equal(3, result.RemovedHeaderCount); Assert.Equal(3, result.RemovedBodyCount);
        Assert.Null(await f.Cache.GetLatestMailboxAsync(f.One.Id, "INBOX")); Assert.Null(await f.Cache.ReadBodyAsync(new(f.One.Id, "INBOX", 7), 1)); Assert.Null(await f.Cache.ReadBodyAsync(new(f.One.Id, "INBOX", 8), 1));
        Assert.Equal("Other account", (await f.Cache.ReadBodyAsync(new(f.Two.Id, "INBOX", 7), 1))!.PlainText); Assert.Equal(1, (await f.Storage.GetAccountDownloadedMailInfoAsync(f.Two.Id)).HeaderCount);
        foreach (var pair in preserved) Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(f.Profile.DirectoryPath, pair.Key)));
        Assert.Equal("INBOX", Assert.Single(await f.Folders.LoadAsync(f.One.Id))); Assert.Null(await f.Folders.LoadProgressAsync(f.One.Id, ["INBOX"]));
        Assert.Empty((await f.Folders.LoadProgressAsync(f.Two.Id, ["INBOX"]))!);
        if (indexed)
        {
            var index = await f.Cache.GetSearchIndexInfoAsync(); Assert.True(index.Ready); Assert.Equal(1, index.Documents);
            var query = new MailHeaderSearchQuery(f.Two.Id, "fixture") { Field = MailHeaderSearchField.IndexedSubject };
            Assert.Single((await f.Cache.SearchHeadersAsync(query)).Messages); Assert.Empty((await f.Cache.SearchHeadersAsync(query with { AccountId = f.One.Id })).Messages);
        }
        string restored = await f.Storage.RestoreBackupAsync(result.BackupDirectory, f.Directory.File("restored")); var restoredCache = new SqliteMailCache(Path.Combine(restored, "mail-cache.db"));
        Assert.Equal("Target account", (await restoredCache.ReadBodyAsync(new(f.One.Id, "INBOX", 8), 1))!.PlainText);
        Assert.Empty((await new JsonOfflineFolderStore(Path.Combine(restored, "offline-folders.json")).LoadProgressAsync(f.One.Id, ["INBOX"]))!);
    }
    [Fact]
    public async Task CanceledAfterVerifiedBackupPreservesAllMailAndProgress()
    {
        using var f = await Fixture.Create(); using var cancellation = new CancellationTokenSource(); string? backup = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, f.Directory.File("canceled"), path => { backup = path; cancellation.Cancel(); }, cancellation.Token));
        Assert.True(System.IO.Directory.Exists(backup)); Assert.Equal(3, (await f.Storage.GetAccountDownloadedMailInfoAsync(f.One.Id)).HeaderCount); Assert.Empty((await f.Folders.LoadProgressAsync(f.One.Id, ["INBOX"]))!);
    }
    [Fact]
    public async Task ExistingDestinationNeverDeletesMailOrChangesCheckpoint()
    {
        using var f = await Fixture.Create(); string destination = f.Directory.File("existing"); System.IO.Directory.CreateDirectory(destination);
        await Assert.ThrowsAsync<IOException>(() => f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, destination));
        Assert.Equal(3, (await f.Storage.GetAccountDownloadedMailInfoAsync(f.One.Id)).HeaderCount); Assert.Empty((await f.Folders.LoadProgressAsync(f.One.Id, ["INBOX"]))!);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingOrCorruptMutationStateBlocksClearBeforeBackup(bool corrupt)
    {
        using var f = await Fixture.Create(); string destination = f.Directory.File("blocked");
        if (corrupt) File.WriteAllText(f.Profile.CachePath + ".intents.json", "broken");
        else await new MutationIntentJournal(f.Profile.CachePath).BeginAsync(new(f.Two.Id, "INBOX", 7, 1), "Archive");
        if (corrupt) await Assert.ThrowsAsync<InvalidDataException>(() => f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, destination));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, destination));
        Assert.False(System.IO.Directory.Exists(destination)); Assert.Equal(3, (await f.Storage.GetAccountDownloadedMailInfoAsync(f.One.Id)).HeaderCount);
    }
    [Fact]
    public async Task DeleteFailureRollsBackSourceAndIndexWhileKeepingRecoveryBackup()
    {
        using var f = await Fixture.Create(); await f.Cache.RebuildSearchIndexAsync();
        using (var connection = new SqliteConnection($"Data Source={f.Profile.CachePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TRIGGER fixture_abort_clear BEFORE DELETE ON messages WHEN old.account='{f.One.Id}' BEGIN SELECT RAISE(ABORT,'synthetic deletion failure'); END"; command.ExecuteNonQuery();
        }
        string? backup = null; await Assert.ThrowsAsync<IOException>(() => f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, f.Directory.File("failed-delete"), path => backup = path));
        Assert.True(System.IO.Directory.Exists(backup)); Assert.Equal(3, (await f.Storage.GetAccountDownloadedMailInfoAsync(f.One.Id)).HeaderCount); Assert.Equal(4, (await f.Cache.GetSearchIndexInfoAsync()).Documents);
        Assert.Equal("Other account", (await f.Cache.ReadBodyAsync(new(f.Two.Id, "INBOX", 7), 1))!.PlainText);
        // Checkpoint reset can conservatively win before a failed deletion; a retry must repeat, never skip work.
        Assert.Null(await f.Folders.LoadProgressAsync(f.One.Id, ["INBOX"])); Assert.Empty((await f.Folders.LoadProgressAsync(f.Two.Id, ["INBOX"]))!);
    }
    [Fact]
    public async Task InvalidAccountAndPreCanceledOperationsHaveNoSideEffects()
    {
        using var f = await Fixture.Create(); await Assert.ThrowsAsync<ArgumentException>(() => f.Storage.GetAccountDownloadedMailInfoAsync(default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Storage.ClearAccountDownloadedMailAsync(default, f.Directory.File("invalid")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Storage.ClearAccountDownloadedMailAsync(f.One.Id, f.Directory.File("canceled"), token: new(true)));
        Assert.False(System.IO.Directory.Exists(f.Directory.File("canceled"))); Assert.Equal(3, (await f.Storage.GetAccountDownloadedMailInfoAsync(f.One.Id)).HeaderCount);
    }
    private sealed class Fixture : IDisposable
    {
        public TestDirectory Directory = new(); public MailProfile Profile = null!; public SqliteMailCache Cache = null!; public ProfileStorageService Storage = null!; public JsonOfflineFolderStore Folders = null!;
        public AccountProfile One = TestDirectory.Profile(), Two = TestDirectory.Profile();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Profile = MailProfile.Open(f.Directory.File("profile")); f.Cache = new(f.Profile.CachePath); f.Storage = new(f.Profile, f.Cache);
            var accounts = new JsonAccountCollectionStore(f.Profile.DirectoryPath); await accounts.SaveAsync(f.One); await accounts.SaveAsync(f.Two);
            await new JsonSettingsStore(f.Profile.SettingsPath).SaveAsync(new());
            await new JsonDraftStore(f.Profile.DraftsPath).SaveAsync(0, new() { Draft = MailComposition.Create(f.One) with { PlainText = "Keep unfinished draft" }, ToText = "raw recipient", CcText = "", BccText = "", State = DraftSubmissionState.Editing });
            await new JsonAccountNavigationStore(Path.Combine(f.Profile.DirectoryPath, "account-navigation.json")).SaveAsync(new(f.One.Id, "Archive", ["INBOX"]));
            await new JsonRecipientBook(Path.Combine(f.Profile.DirectoryPath, "recipients.json")).SaveAsync(SavedRecipient.Create(f.One.Id, "friend@example.test", "Saved person"));
            File.WriteAllText(Path.Combine(f.Profile.DirectoryPath, "credential.fixture"), "synthetic protected-store stand-in");
            f.Folders = new(Path.Combine(f.Profile.DirectoryPath, "offline-folders.json"));
            foreach (var owner in new[] { f.One, f.Two }) { await f.Folders.SaveAsync(owner.Id, ["INBOX"]); await f.Folders.SaveProgressAsync(owner.Id, ["INBOX"], []); }
            foreach (var (owner, folder, epoch, body) in new[] { (f.One, "INBOX", 7u, "Target account"), (f.One, "INBOX", 8u, "Target account"), (f.One, "Archive", 9u, "Target account"), (f.Two, "INBOX", 7u, "Other account") })
            {
                var mailbox = new MailboxKey(owner.Id, folder, epoch); var key = new MailMessageKey(owner.Id, folder, epoch, 1);
                await f.Cache.SaveSummariesAsync(mailbox, [new() { Key = key, Subject = "Mail fixture", Sender = "sender@example.test" }], DateTimeOffset.UtcNow); await f.Cache.SaveBodyAsync(mailbox, new(key, body));
            }
            return f;
        }
        public void Dispose() { Profile.Dispose(); Directory.Dispose(); }
    }
}
