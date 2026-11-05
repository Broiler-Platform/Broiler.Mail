using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class OfflineFolderTests
{
    [Fact]
    public async Task SelectionPersistenceAndConcurrentUpdatesKeepAccountAndFolderIdentity()
    {
        using var directory = new TestDirectory(); string path = directory.File("offline-folders.json"); var store = new JsonOfflineFolderStore(path); var one = AccountId.New(); var two = AccountId.New();
        Assert.Empty(await store.LoadAsync(one)); Assert.False(File.Exists(path));
        await Task.WhenAll(store.SaveAsync(one, ["INBOX", "Work/Projects", "../literal-mailbox"]), new JsonOfflineFolderStore(path).SaveAsync(two, ["INBOX"]));
        Assert.Equal(new[] { "INBOX", "Work/Projects", "../literal-mailbox" }, await new JsonOfflineFolderStore(path).LoadAsync(one));
        await store.SaveAsync(one, []); Assert.Empty(await store.LoadAsync(one)); Assert.Equal("INBOX", (await store.LoadAsync(two)).Single());
        Assert.False(Directory.Exists(directory.File("literal-mailbox")));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("control")]
    [InlineData("empty")]
    [InlineData("length")]
    [InlineData("count")]
    [InlineData("unicode")]
    public async Task InvalidSelectionsKeepPreviousFile(string mode)
    {
        using var directory = new TestDirectory(); string path = directory.File("offline-folders.json"); var store = new JsonOfflineFolderStore(path); var account = AccountId.New(); await store.SaveAsync(account, ["INBOX"]); byte[] before = File.ReadAllBytes(path);
        string[] invalid = mode switch { "duplicate" => ["INBOX", "INBOX"], "control" => ["bad\nfolder"], "empty" => [""], "length" => [new string('a', 1025)], "unicode" => ["\ud800"], _ => Enumerable.Range(1, 9).Select(i => "Folder" + i).ToArray() };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(account, invalid)); Assert.Equal(before, File.ReadAllBytes(path));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(account, ["Archive"], new(true))); Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrFutureSelectionsDisableEditingWithoutResettingMail(bool future)
    {
        using var f = await Fixture.Create(); string path = f.Directory.File("offline-folders.json");
        if (future) { var json = JsonNode.Parse(File.ReadAllText(path))!; json["schemaVersion"] = 9; File.WriteAllText(path, json.ToJsonString()); } else File.WriteAllText(path, "broken");
        byte[] before = File.ReadAllBytes(path); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        Assert.False(model.OfflineFoldersReady); Assert.False(model.CanToggleOfflineFolder); Assert.False(model.CanDownloadOfflineFolders); Assert.True(model.CanReceive); Assert.Equal(2, model.Messages.Count);
        await model.ToggleOfflineFolderAsync(); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Empty(f.Receiver.Calls);
        File.WriteAllText(path, "{\"schemaVersion\":1,\"data\":{\"accounts\":[]}}"); await model.ReloadOfflineFolderSelectionAsync(); Assert.True(model.OfflineFoldersReady); Assert.Empty(model.OfflineFolders);
    }

    [Fact]
    public async Task ExplicitMultiFolderBatchUsesOnlyCurrentSavedHeadersAndSkipsAlreadySavedBodies()
    {
        using var f = await Fixture.Create(); await f.Cache.SaveBodyAsync(new(f.Account.Id, "INBOX", 7), new(new(f.Account.Id, "INBOX", 7, 2), "Already saved"));
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        Assert.Empty(f.Receiver.Calls); Assert.True(model.CanDownloadOfflineFolders); await model.DownloadOfflineFoldersAsync();
        Assert.Equal(3, model.DownloadCompleted); Assert.Equal(3, model.DownloadTotal); Assert.Equal(2, f.Receiver.Calls.Count);
        Assert.Contains(f.Receiver.Calls, key => key.MailboxId == "INBOX" && key.Uid == 1); Assert.Contains(f.Receiver.Calls, key => key.MailboxId == "Archive" && key.Uid == 1);
        Assert.All(f.Receiver.Calls, key => Assert.Equal(f.Account.Id, key.AccountId)); Assert.Equal(0, f.Receiver.HeaderReceives); Assert.All(model.Messages, row => Assert.True(row.IsBodyCached));
        Assert.Contains("No headers were received", model.Status); Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Null(model.Body);
    }

    [Fact]
    public async Task FailedBatchRetainsConfirmedBodiesAndRetrySkipsThem()
    {
        using var f = await Fixture.Create(); bool fail = true;
        f.Receiver.Body = key => key.MailboxId == "INBOX" && key.Uid == 1 && fail ? Task.FromException<MailMessageBody>(new MailConnectionException("Synthetic stop")) : Task.FromResult(new MailMessageBody(key, "Saved"));
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        Assert.Equal(1, model.DownloadCompleted); Assert.True(model.ProblemIsSelectedFolderDownload); Assert.True(model.CanRetry); Assert.NotNull(await f.Cache.ReadBodyAsync(new(f.Account.Id, "INBOX", 7), 2));
        fail = false; await model.RetryAsync(InboxProblemScope.List); Assert.Null(model.ListProblem); Assert.Equal(3, model.DownloadCompleted);
        Assert.Single(f.Receiver.Calls, key => key.MailboxId == "INBOX" && key.Uid == 2); Assert.Equal(0, f.Receiver.HeaderReceives);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrAccountSwitchRejectsLateUncancellableBodies(bool switchAccount)
    {
        using var f = await Fixture.Create(); var entered = new TaskCompletionSource<MailMessageKey>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Receiver.Body = key => { entered.SetResult(key); return release.Task; };
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        var downloading = model.DownloadOfflineFoldersAsync(); var key = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (switchAccount) { model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; await model.OfflineFolderLoadTask; } else model.Cancel();
        release.SetResult(new(key, "Late body")); await downloading;
        Assert.Null(await f.Cache.ReadBodyAsync(new(key.AccountId, key.MailboxId, key.UidValidity), key.Uid)); Assert.False(model.IsBusy);
        if (switchAccount) { Assert.Empty(model.Messages); Assert.Empty(model.OfflineFolders); } else Assert.True(model.ProblemIsSelectedFolderDownload);
    }

    [Fact]
    public async Task EpochChangeDuringFetchPreventsSavingOldIdentityBody()
    {
        using var f = await Fixture.Create(); f.Receiver.Body = async key =>
        {
            await f.Cache.SaveSummariesAsync(new(key.AccountId, key.MailboxId, 8), [Row(new(key.AccountId, key.MailboxId, 8, key.Uid))], DateTimeOffset.UtcNow);
            return new(key, "Old epoch body");
        };
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        Assert.NotNull(model.ListProblem); Assert.Equal(0, model.DownloadCompleted); Assert.Null(await f.Cache.ReadBodyAsync(new(f.Account.Id, "INBOX", 7), 2));
        Assert.Equal((uint)8, (await f.Cache.GetLatestMailboxAsync(f.Account.Id, "INBOX"))!.Key.UidValidity);
    }

    [Fact]
    public async Task SelectionToggleSurvivesRestartAndUnselectKeepsSavedMail()
    {
        using var f = await Fixture.Create(); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        await model.ToggleOfflineFolderAsync(); Assert.False(model.IsSelectedFolderOffline); Assert.Equal(2, model.Messages.Count); Assert.Equal("Archive", (await f.Store.LoadAsync(f.Account.Id)).Single());
        await model.ToggleOfflineFolderAsync(); using var reopened = f.Model(); reopened.SetAccount(f.Account with { IsEnabled = false }); await reopened.CacheLoadTask; await reopened.OfflineFolderLoadTask;
        Assert.True(reopened.IsSelectedFolderOffline); Assert.False(reopened.CanDownloadOfflineFolders); Assert.True(reopened.CanToggleOfflineFolder); Assert.Empty(f.Receiver.Calls);
    }

    [Fact]
    public async Task FolderBatchIsCappedAt500SavedHeadersAndReportsMissingCoverage()
    {
        using var f = await Fixture.Create(); var mailbox = new MailboxKey(f.Account.Id, "Large", 7);
        var rows = Enumerable.Range(1, 501).Select(i => Row(new(f.Account.Id, "Large", 7, (uint)i))).ToArray();
        await f.Cache.SaveSummariesAsync(mailbox, rows.Take(500).ToArray(), DateTimeOffset.UtcNow); await f.Cache.SaveSummariesAsync(mailbox, rows.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        await f.Store.SaveAsync(f.Account.Id, ["Large", "Empty"]);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        Assert.Equal(500, model.DownloadCompleted); Assert.Equal(500, f.Receiver.Calls.Count); Assert.Null(await f.Cache.ReadBodyAsync(mailbox, 1));
        Assert.Contains("1 folders exceeded", model.Status); Assert.Contains("1 had no saved headers", model.Status);
    }

    [Fact]
    public async Task LatePreferenceLoadCannotReplaceAnotherAccountsSelection()
    {
        using var f = await Fixture.Create(); var other = TestDirectory.Profile(); var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new InboxViewModel(f.Receiver, new ImmediateUiDispatcher(), f.Cache, offlineFolders: new DelayedSelections(f.Account.Id, pending));
        model.SetAccount(f.Account); await model.CacheLoadTask; var old = model.OfflineFolderLoadTask;
        model.SetAccount(other); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        pending.SetResult(["Old account folder"]); await old; Assert.Equal("Other account folder", model.OfflineFolders.Single()); Assert.Empty(f.Receiver.Calls);
    }
    private sealed class DelayedSelections(AccountId delayed, TaskCompletionSource<IReadOnlyList<string>> pending) : IOfflineFolderStore
    {
        public Task<IReadOnlyList<string>> LoadAsync(AccountId account, CancellationToken token = default) => account == delayed ? pending.Task : Task.FromResult<IReadOnlyList<string>>(["Other account folder"]);
        public Task SaveAsync(AccountId account, IReadOnlyList<string> folders, CancellationToken token = default) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task ContinuationWalksOlderHeadersPerFolderAndRestartRechecksWithoutRefetching()
    {
        using var f = await Fixture.Create(); await SeedLarge(f, 1001); await f.Store.SaveAsync(f.Account.Id, ["Large", "Archive"]);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        await model.DownloadOfflineFoldersAsync(); Assert.Equal(501, model.DownloadCompleted); Assert.True(model.HasOfflineFolderContinuation);
        Assert.Equal(502u, f.Receiver.Calls.Where(key => key.MailboxId == "Large").Min(key => key.Uid));
        Assert.Null(await f.Cache.ReadBodyAsync(new(f.Account.Id, "Large", 7), 501));
        await model.DownloadOfflineFoldersAsync(); Assert.Equal(500, model.DownloadCompleted); Assert.True(model.HasOfflineFolderContinuation);
        await model.DownloadOfflineFoldersAsync(); Assert.Equal(1, model.DownloadCompleted); Assert.False(model.HasOfflineFolderContinuation); Assert.False(model.CanDownloadOfflineFolders); Assert.True(model.CanRestartOfflineFolderBatches);
        Assert.Equal(1002, f.Receiver.Calls.Count); Assert.Equal(1002, f.Receiver.Calls.Distinct().Count()); Assert.Single(f.Receiver.Calls, key => key.MailboxId == "Archive"); Assert.Equal(0, f.Receiver.HeaderReceives);
        Assert.True(await model.RestartOfflineFolderBatchesAsync()); await model.DownloadOfflineFoldersAsync(); Assert.Equal(1002, f.Receiver.Calls.Count); Assert.True(model.HasOfflineFolderContinuation);
        Assert.NotNull(await f.Cache.ReadBodyAsync(new(f.Account.Id, "Large", 7), 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledOlderBatchDoesNotAdvanceCursorAndRetrySkipsCommittedBodies(bool cancel)
    {
        using var f = await Fixture.Create(); await SeedLarge(f, 502); await f.Store.SaveAsync(f.Account.Id, ["Large"]);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        var entered = new TaskCompletionSource<MailMessageKey>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<MailMessageBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Receiver.Body = key =>
        {
            if (key.Uid != 1) return Task.FromResult(new MailMessageBody(key, "Older saved"));
            if (!cancel) return Task.FromException<MailMessageBody>(new MailConnectionException("Synthetic older batch failure"));
            entered.SetResult(key); return release.Task;
        };
        var downloading = model.DownloadOfflineFoldersAsync();
        if (cancel) { var key = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.Cancel(); release.SetResult(new(key, "Late")); }
        await downloading; Assert.True(model.HasOfflineFolderContinuation); Assert.NotNull(model.ListProblem); Assert.NotNull(await f.Cache.ReadBodyAsync(new(f.Account.Id, "Large", 7), 2)); Assert.Null(await f.Cache.ReadBodyAsync(new(f.Account.Id, "Large", 7), 1));
        f.Receiver.Body = key => Task.FromResult(new MailMessageBody(key, "Retry saved")); await model.RetryAsync(InboxProblemScope.List);
        Assert.Null(model.ListProblem); Assert.False(model.HasOfflineFolderContinuation); Assert.False(model.CanDownloadOfflineFolders); Assert.Equal(2, model.DownloadCompleted);
        Assert.Single(f.Receiver.Calls, key => key.Uid == 2); Assert.Equal(2, f.Receiver.Calls.Count(key => key.Uid == 1));
    }

    [Fact]
    public async Task ChangedEpochRefusesContinuationUntilExplicitRestart()
    {
        using var f = await Fixture.Create(); await SeedLarge(f, 501); await f.Store.SaveAsync(f.Account.Id, ["Large"]);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        await f.Cache.SaveSummariesAsync(new(f.Account.Id, "Large", 8), [Row(new(f.Account.Id, "Large", 8, 1))], DateTimeOffset.UtcNow); int calls = f.Receiver.Calls.Count;
        await model.DownloadOfflineFoldersAsync(); Assert.Equal(calls, f.Receiver.Calls.Count); Assert.True(model.HasOfflineFolderContinuation); Assert.Contains("Restart offline batches", model.ListProblem!.Text);
        Assert.True(await model.RestartOfflineFolderBatchesAsync()); Assert.Null(model.ListProblem); await model.DownloadOfflineFoldersAsync(); Assert.Null(model.ListProblem); Assert.Equal((uint)8, f.Receiver.Calls.Last().UidValidity);
    }

    [Fact]
    public async Task SelectionChangesResetProgressWhileReloadAndMaintenanceKeepDurableCompletion()
    {
        using var f = await Fixture.Create(); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        Assert.True(model.HasCompletedOfflineFolderBatch); Assert.False(model.CanDownloadOfflineFolders);
        await model.ReloadOfflineFolderSelectionAsync(); Assert.True(model.HasCompletedOfflineFolderBatch); Assert.False(model.CanDownloadOfflineFolders);
        await model.ToggleOfflineFolderAsync(); Assert.False(model.HasCompletedOfflineFolderBatch); await model.DownloadOfflineFoldersAsync();
        model.BeginStorageMaintenance(); Assert.True(model.HasCompletedOfflineFolderBatch); model.EndStorageMaintenance(false); Assert.False(model.CanDownloadOfflineFolders);
        model.SetAccount(TestDirectory.Profile()); await model.CacheLoadTask; await model.OfflineFolderLoadTask; Assert.False(model.HasCompletedOfflineFolderBatch); Assert.Empty(model.OfflineFolders);
        Assert.NotNull(await f.Cache.ReadBodyAsync(new(f.Account.Id, "INBOX", 7), 1));
    }

    [Fact]
    public async Task FolderNavigationBetweenBatchesKeepsContinuationWithoutRepeatingFinishedFolders()
    {
        using var f = await Fixture.Create(); await SeedLarge(f, 501); await f.Store.SaveAsync(f.Account.Id, ["Large", "Archive"]);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask; Assert.True(model.HasOfflineFolderContinuation);
        await model.DownloadOfflineFoldersAsync(); Assert.Equal(1, model.DownloadCompleted); Assert.Equal(1u, f.Receiver.Calls.Last().Uid); Assert.Equal("Large", f.Receiver.Calls.Last().MailboxId);
        Assert.Single(f.Receiver.Calls, key => key.MailboxId == "Archive"); Assert.Equal("Archive", model.SelectedMailboxId);
    }

    [Fact]
    public async Task ReopenedModelContinuesOlderBatchAndRetainsCompletionUntilDurableRestart()
    {
        using var f = await Fixture.Create(); await SeedLarge(f, 501); await f.Store.SaveAsync(f.Account.Id, ["Large", "Archive"]);
        using (var first = f.Model()) { first.SetAccount(f.Account); await first.CacheLoadTask; await first.OfflineFolderLoadTask; await first.DownloadOfflineFoldersAsync(); Assert.True(first.HasOfflineFolderContinuation); }
        using (var second = f.Model())
        {
            second.SetAccount(f.Account); await second.CacheLoadTask; await second.OfflineFolderLoadTask;
            Assert.True(second.HasOfflineFolderContinuation); int calls = f.Receiver.Calls.Count;
            await second.DownloadOfflineFoldersAsync(); Assert.Equal(1, second.DownloadCompleted); Assert.Equal(calls + 1, f.Receiver.Calls.Count);
        }
        using (var third = f.Model())
        {
            third.SetAccount(f.Account); await third.CacheLoadTask; await third.OfflineFolderLoadTask;
            Assert.True(third.HasCompletedOfflineFolderBatch); Assert.False(third.CanDownloadOfflineFolders);
            Assert.True(await third.RestartOfflineFolderBatchesAsync());
        }
        using var fourth = f.Model(); fourth.SetAccount(f.Account); await fourth.CacheLoadTask; await fourth.OfflineFolderLoadTask;
        Assert.False(fourth.HasCompletedOfflineFolderBatch); Assert.True(fourth.CanDownloadOfflineFolders); Assert.NotNull(await f.Cache.ReadBodyAsync(new(f.Account.Id, "Large", 7), 1));
    }

    [Fact]
    public async Task ProgressValidationAndSelectionCompareKeepPreviousBytes()
    {
        using var f = await Fixture.Create(); string path = f.Directory.File("offline-folders.json");
        var key = new MailMessageKey(f.Account.Id, "INBOX", 7, 1);
        await f.Store.SaveProgressAsync(f.Account.Id, ["INBOX", "Archive"], [key]); byte[] before = File.ReadAllBytes(path);
        foreach (var keys in new[] { new[] { key with { AccountId = AccountId.New() } }, new[] { key with { Uid = 0 } }, new[] { key with { UidValidity = 0 } }, new[] { key with { MailboxId = "Other" } }, new[] { key, key } })
        { await Assert.ThrowsAsync<ArgumentException>(() => f.Store.SaveProgressAsync(f.Account.Id, ["INBOX", "Archive"], keys)); Assert.Equal(before, File.ReadAllBytes(path)); }
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.SaveProgressAsync(f.Account.Id, ["Archive"], [])); Assert.Equal(before, File.ReadAllBytes(path));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.SaveProgressAsync(f.Account.Id, ["INBOX", "Archive"], [], new(true))); Assert.Equal(before, File.ReadAllBytes(path));
        await f.Store.SaveAsync(f.Account.Id, ["INBOX"]); Assert.Null(await f.Store.LoadProgressAsync(f.Account.Id, ["INBOX"]));
    }

    [Fact]
    public async Task CorruptProgressDisablesBatchControlsWithoutOverwritingSavedMail()
    {
        using var f = await Fixture.Create(); await f.Store.SaveProgressAsync(f.Account.Id, ["INBOX", "Archive"], [new(f.Account.Id, "INBOX", 7, 1)]);
        string path = f.Directory.File("offline-folders.json"); var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["data"]!["accounts"]![0]!["remaining"]![0]!["uid"] = 0; File.WriteAllText(path, json.ToJsonString()); byte[] before = File.ReadAllBytes(path);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        Assert.False(model.OfflineFoldersReady); Assert.False(model.CanDownloadOfflineFolders); Assert.True(model.CanReceive); Assert.Equal(2, model.Messages.Count); Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task FailedCheckpointWriteKeepsSavedBodiesAndRetryDoesNotRedownload()
    {
        using var f = await Fixture.Create(); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask;
        string path = f.Directory.File("offline-folders.json"); byte[] good = File.ReadAllBytes(path); File.WriteAllText(path, "broken");
        await model.DownloadOfflineFoldersAsync(); Assert.NotNull(model.ListProblem); Assert.False(model.HasCompletedOfflineFolderBatch); Assert.Equal(3, f.Receiver.Calls.Count);
        Assert.NotNull(await f.Cache.ReadBodyAsync(new(f.Account.Id, "Archive", 9), 1));
        File.WriteAllBytes(path, good); await model.RetryAsync(InboxProblemScope.List);
        Assert.Null(model.ListProblem); Assert.True(model.HasCompletedOfflineFolderBatch); Assert.Equal(3, f.Receiver.Calls.Count);
    }

    [Fact]
    public async Task RestartFailureRetainsDurableCompletionUntilReloadAndRetry()
    {
        using var f = await Fixture.Create(); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        string path = f.Directory.File("offline-folders.json"); byte[] good = File.ReadAllBytes(path); File.WriteAllText(path, "broken");
        Assert.False(await model.RestartOfflineFolderBatchesAsync()); Assert.True(model.HasCompletedOfflineFolderBatch); Assert.False(model.CanDownloadOfflineFolders); Assert.False(model.IsSavingOfflineFolders);
        File.WriteAllBytes(path, good); Assert.True(await model.RestartOfflineFolderBatchesAsync()); Assert.Null(await f.Store.LoadProgressAsync(f.Account.Id, ["INBOX", "Archive"]));
    }

    [Fact]
    public async Task PersistedContinuationRefusesNewEpochAfterReopenBeforeAnyFetch()
    {
        using var f = await Fixture.Create(); await f.Store.SaveAsync(f.Account.Id, ["INBOX"]);
        await f.Store.SaveProgressAsync(f.Account.Id, ["INBOX"], [new(f.Account.Id, "INBOX", 7, 2)]);
        await f.Cache.SaveSummariesAsync(new(f.Account.Id, "INBOX", 8), [Row(new(f.Account.Id, "INBOX", 8, 1))], DateTimeOffset.UtcNow);
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; await model.OfflineFolderLoadTask; await model.DownloadOfflineFoldersAsync();
        Assert.Empty(f.Receiver.Calls); Assert.True(model.HasOfflineFolderContinuation); Assert.NotNull(model.ListProblem);
        Assert.Equal(7u, Assert.Single((await f.Store.LoadProgressAsync(f.Account.Id, ["INBOX"]))!).UidValidity);
    }

    [Fact]
    public async Task ProgressUpdatesStayAccountScopedAndLegacyLoadDoesNotRewrite()
    {
        using var f = await Fixture.Create(); var other = AccountId.New(); await f.Store.SaveAsync(other, ["INBOX"]);
        byte[] legacy = File.ReadAllBytes(f.Directory.File("offline-folders.json")); Assert.Null(await f.Store.LoadProgressAsync(other, ["INBOX"])); Assert.Equal(legacy, File.ReadAllBytes(f.Directory.File("offline-folders.json")));
        await Task.WhenAll(f.Store.SaveProgressAsync(f.Account.Id, ["INBOX", "Archive"], []), new JsonOfflineFolderStore(f.Directory.File("offline-folders.json")).SaveProgressAsync(other, ["INBOX"], [new(other, "INBOX", 19, 50)]));
        Assert.Empty((await f.Store.LoadProgressAsync(f.Account.Id, ["INBOX", "Archive"]))!);
        Assert.Equal(19u, Assert.Single((await f.Store.LoadProgressAsync(other, ["INBOX"]))!).UidValidity);
    }

    private static async Task SeedLarge(Fixture f, int count)
    {
        var mailbox = new MailboxKey(f.Account.Id, "Large", 7);
        var rows = Enumerable.Range(1, count).Select(i => Row(new(f.Account.Id, "Large", 7, (uint)i))).ToArray();
        foreach (var chunk in rows.Chunk(500)) await f.Cache.SaveSummariesAsync(mailbox, chunk, DateTimeOffset.UtcNow);
    }

    private static MailMessageSummary Row(MailMessageKey key) => new() { Key = key, Subject = "Synthetic offline fixture", Sender = "sender@example.test" };
    private sealed class Fixture : IDisposable
    {
        public TestDirectory Directory = new(); public AccountProfile Account = TestDirectory.Profile(); public SqliteMailCache Cache = null!; public JsonOfflineFolderStore Store = null!; public Receiver Receiver = new();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Cache = new(f.Directory.File("mail.db")); f.Store = new(f.Directory.File("offline-folders.json"));
            await f.Cache.SaveSummariesAsync(new(f.Account.Id, "INBOX", 7), [Row(new(f.Account.Id, "INBOX", 7, 1)), Row(new(f.Account.Id, "INBOX", 7, 2))], DateTimeOffset.UtcNow);
            await f.Cache.SaveSummariesAsync(new(f.Account.Id, "Archive", 9), [Row(new(f.Account.Id, "Archive", 9, 1))], DateTimeOffset.UtcNow);
            await f.Store.SaveAsync(f.Account.Id, ["INBOX", "Archive"]); return f;
        }
        public InboxViewModel Model() => new(Receiver, new ImmediateUiDispatcher(), Cache, OfflineDownloadPolicy.HeadersOnly, Store);
        public void Dispose() => Directory.Dispose();
    }
    private sealed class Receiver : IMailFolderReceiver
    {
        public List<MailMessageKey> Calls = []; public int HeaderReceives;
        public Func<MailMessageKey, Task<MailMessageBody>> Body = key => Task.FromResult(new MailMessageBody(key, "Synthetic saved body"));
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) { Calls.Add(key); return Body(key); }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No connection test in offline fixture.");
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No discovery in offline fixture.");
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string mailbox, int count, MailInboxCursor? older = null, CancellationToken token = default) { HeaderReceives++; throw new InvalidOperationException("No header receive in offline fixture."); }
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => GetMailboxAsync(account, "INBOX", count, older, token);
    }
}
