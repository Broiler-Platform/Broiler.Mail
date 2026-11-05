using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class AccountNavigationTests
{
    [Fact]
    public async Task MissingAndConcurrentPreferencesAreBoundedAccountScopedAndNamesAreNeverPaths()
    {
        using var directory = new TestDirectory(); string path = directory.File("account-navigation.json"); var store = new JsonAccountNavigationStore(path); var a = AccountId.New(); var b = AccountId.New();
        Assert.Equal("INBOX", (await store.LoadAsync(a)).StartMailboxId); Assert.False(File.Exists(path));
        await Task.WhenAll(store.SaveAsync(new(a, "Work/Projects", ["../literal-folder", "INBOX"])), new JsonAccountNavigationStore(path).SaveAsync(new(b, "Inbox", ["Inbox", "INBOX"])));
        Assert.Equal("Work/Projects", (await store.LoadAsync(a)).StartMailboxId); Assert.Equal(new[] { "Inbox", "INBOX" }, (await store.LoadAsync(b)).PinnedMailboxIds);
        Assert.False(Directory.Exists(directory.File("literal-folder")));
        await store.SaveAsync(new(a, "INBOX", [])); Assert.Empty((await store.LoadAsync(a)).PinnedMailboxIds); Assert.Equal("Inbox", (await store.LoadAsync(b)).StartMailboxId);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("blank")]
    [InlineData("control")]
    [InlineData("long")]
    [InlineData("unicode")]
    [InlineData("duplicate")]
    [InlineData("count")]
    public async Task InvalidPreferencesAndCanceledWritesKeepPreviousBytes(string kind)
    {
        using var f = await Fixture.Create(); var row = new AccountNavigation(f.Account.Id, "Archive", ["INBOX"]);
        await f.Store.SaveAsync(row); string path = f.Directory.File("account-navigation.json"); byte[] before = File.ReadAllBytes(path);
        var invalid = kind switch
        {
            "account" => row with { AccountId = default }, "blank" => row with { StartMailboxId = " " }, "control" => row with { StartMailboxId = "x\n" },
            "long" => row with { StartMailboxId = new('x', 1025) }, "unicode" => row with { PinnedMailboxIds = ["\ud800"] },
            "duplicate" => row with { PinnedMailboxIds = ["INBOX", "INBOX"] }, _ => row with { PinnedMailboxIds = Enumerable.Range(1,9).Select(i => "Folder" + i).ToArray() },
        };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.SaveAsync(invalid)); Assert.Equal(before, File.ReadAllBytes(path));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.SaveAsync(row, new(true))); Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task StartupAndSwitchRestoreOnlyTheirSavedHeadersWithoutReceivingOrOpeningBodies()
    {
        using var f = await Fixture.Create(); var other = TestDirectory.Profile();
        await f.Cache.SaveSummariesAsync(new(other.Id, "Archive", 7), [Row(other.Id, "Archive", "Other account")], DateTimeOffset.UtcNow);
        await f.Store.SaveAsync(new(f.Account.Id, "Archive", ["Archive", "Unsynced"])); await f.Store.SaveAsync(new(other.Id, "INBOX", ["Archive"]));
        using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask; Assert.Equal("Archive", model.SelectedMailboxId); Assert.Equal("Saved archive", Assert.Single(model.Messages).Subject); Assert.Null(model.Body); Assert.True(model.NavigationReady);
        Assert.Contains(model.Folders, row => row.MailboxId == "Unsynced"); Assert.Contains(f.Account.EmailAddress, model.NavigationNotice);
        model.SetAccount(other with { IsEnabled = false }); await model.CacheLoadTask; Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Empty(model.Messages); Assert.Single(model.PinnedFolders); Assert.True(model.OpenPinnedFolder("Archive")); await model.CacheLoadTask;
        Assert.Equal("Other account", Assert.Single(model.Messages).Subject); Assert.False(model.CanReceive);
        model.SetAccount(f.Account); await model.CacheLoadTask; Assert.Equal("Archive", model.SelectedMailboxId); Assert.Equal(0, f.Receiver.Calls);
    }

    [Fact]
    public async Task ExplicitStartPinResetAndReloadKeepMailAndDoNotFetch()
    {
        using var f = await Fixture.Create(); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask;
        Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask; await model.SetStartFolderAsync(); await model.TogglePinnedFolderAsync();
        Assert.Equal("Archive", model.StartFolder); Assert.Equal("Archive", Assert.Single(model.PinnedFolders)); Assert.True(model.IsCurrentFolderPinned);
        await model.TogglePinnedFolderAsync(); Assert.Empty(model.PinnedFolders); Assert.Single(model.Messages);
        Assert.True(model.SelectFolder("INBOX")); await model.CacheLoadTask; await model.ReloadAccountNavigationAsync(); Assert.Equal("Archive", model.SelectedMailboxId);
        await model.ResetAccountNavigationAsync(); Assert.Equal("INBOX", model.StartFolder); Assert.Equal("Archive", model.SelectedMailboxId); Assert.Single(model.Messages);
        await model.ReloadAccountNavigationAsync(); Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Empty(model.PinnedFolders); Assert.Equal(0, f.Receiver.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrFuturePreferencesKeepInboxUsableAndNeverSilentlyReset(bool future)
    {
        using var f = await Fixture.Create(); await f.Store.SaveAsync(new(f.Account.Id, "Archive", ["Archive"])); string path = f.Directory.File("account-navigation.json");
        if (future) { var json = JsonNode.Parse(File.ReadAllText(path))!; json["schemaVersion"] = 8; File.WriteAllText(path, json.ToJsonString()); } else File.WriteAllText(path, "broken");
        byte[] before = File.ReadAllBytes(path); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask;
        Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Single(model.Messages); Assert.False(model.NavigationReady); Assert.True(model.CanReceive); Assert.True(model.CanSwitchFolder); Assert.False(model.CanResetNavigation);
        await model.SetStartFolderAsync(); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, f.Receiver.Calls);
        File.WriteAllText(path, "{\"schemaVersion\":1,\"data\":{\"accounts\":[]}}"); await model.ReloadAccountNavigationAsync(); Assert.True(model.NavigationReady);
    }

    [Fact]
    public async Task FailedPreferenceSaveKeepsOldShortcutsAndCurrentMail()
    {
        using var f = await Fixture.Create(); using var model = f.Model(); model.SetAccount(f.Account); await model.CacheLoadTask;
        Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask; string path = f.Directory.File("account-navigation.json"); File.WriteAllText(path, "broken");
        await model.TogglePinnedFolderAsync(); Assert.Empty(model.PinnedFolders); Assert.Single(model.Messages); Assert.False(model.IsSavingNavigation); Assert.Contains("not confirmed saved", model.NavigationNotice); Assert.Equal("broken", File.ReadAllText(path));
    }

    [Fact]
    public async Task LateNavigationReadCannotRepaintAnotherAccount()
    {
        using var f = await Fixture.Create(); var other = TestDirectory.Profile(); var pending = new TaskCompletionSource<AccountNavigation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new DelayedStore(f.Account.Id, pending);
        using var model = new InboxViewModel(f.Receiver, new ImmediateUiDispatcher(), f.Cache, navigation: store);
        model.SetAccount(f.Account); var first = model.CacheLoadTask; await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.SetAccount(other); await model.CacheLoadTask;
        pending.SetResult(new(f.Account.Id, "Archive", ["Old account"])); await first; Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Empty(model.PinnedFolders); Assert.Empty(model.Messages); Assert.Equal(0, f.Receiver.Calls);
    }
    [Fact]
    public async Task LatePreferenceWriteCannotReplaceAnotherAccountsNavigation()
    {
        using var f = await Fixture.Create(); var other = TestDirectory.Profile(); var store = new DelayedWriteStore();
        using var model = new InboxViewModel(f.Receiver, new ImmediateUiDispatcher(), f.Cache, navigation: store);
        model.SetAccount(f.Account); await model.CacheLoadTask; Assert.True(model.SelectFolder("Archive")); await model.CacheLoadTask;
        var saving = model.SetStartFolderAsync(); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.SetAccount(other); await model.CacheLoadTask; store.Release.SetResult(); await saving;
        Assert.Equal(f.Account.Id, store.Written!.AccountId); Assert.Equal("Archive", store.Written.StartMailboxId);
        Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Equal("INBOX", model.StartFolder); Assert.False(model.IsSavingNavigation); Assert.Empty(model.PinnedFolders);
    }
    private sealed class DelayedWriteStore : IAccountNavigationStore
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AccountNavigation? Written;
        public Task<AccountNavigation> LoadAsync(AccountId account, CancellationToken token = default) => Task.FromResult(new AccountNavigation(account, "INBOX", []));
        public async Task SaveAsync(AccountNavigation row, CancellationToken token = default) { Written = row; Entered.SetResult(); await Release.Task; }
    }

    [Fact]
    public async Task WrongAccountPreferenceResponseIsRejectedWithoutLeakingFolderNames()
    {
        using var f = await Fixture.Create(); var pending = new TaskCompletionSource<AccountNavigation>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.SetResult(new(AccountId.New(), "Private folder", ["Private shortcut"]));
        using var model = new InboxViewModel(f.Receiver, new ImmediateUiDispatcher(), f.Cache, navigation: new DelayedStore(f.Account.Id, pending));
        model.SetAccount(f.Account); await model.CacheLoadTask; Assert.Equal("INBOX", model.SelectedMailboxId); Assert.False(model.NavigationReady); Assert.Empty(model.PinnedFolders); Assert.Single(model.Messages);
        Assert.DoesNotContain(model.Folders, folder => folder.MailboxId == "Private folder" || folder.MailboxId == "Private shortcut");
    }

    [Fact]
    public async Task CanceledNavigationRestoreReleasesOrdinaryFolderNavigationAndDiscardsLateRead()
    {
        using var f = await Fixture.Create(); var pending = new TaskCompletionSource<AccountNavigation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new DelayedStore(f.Account.Id, pending);
        using var model = new InboxViewModel(f.Receiver, new ImmediateUiDispatcher(), f.Cache, navigation: store);
        model.SetAccount(f.Account); var loading = model.CacheLoadTask; await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.CanSwitchFolder); model.Cancel(); Assert.True(model.CanSwitchFolder); Assert.True(model.CanReloadNavigation);
        pending.SetResult(new(f.Account.Id, "Archive", ["Archive"])); await loading;
        Assert.Equal("INBOX", model.SelectedMailboxId); Assert.False(model.NavigationReady); Assert.Empty(model.PinnedFolders); Assert.Equal(0, f.Receiver.Calls);
    }

    private sealed class DelayedStore(AccountId delayed, TaskCompletionSource<AccountNavigation> pending) : IAccountNavigationStore
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AccountNavigation> LoadAsync(AccountId account, CancellationToken token = default)
        {
            if (account != delayed) return Task.FromResult(new AccountNavigation(account, "INBOX", []));
            Entered.TrySetResult(); return pending.Task;
        }
        public Task SaveAsync(AccountNavigation row, CancellationToken token = default) => throw new InvalidOperationException();
    }
    private static MailMessageSummary Row(AccountId account, string folder, string subject) => new() { Key = new(account, folder, 7, 1), Sender = "sender@example.test", Subject = subject };
    private sealed class Fixture : IDisposable
    {
        public TestDirectory Directory = new(); public AccountProfile Account = TestDirectory.Profile(); public SqliteMailCache Cache = null!; public JsonAccountNavigationStore Store = null!; public Receiver Receiver = new();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Cache = new(f.Directory.File("mail.db")); f.Store = new(f.Directory.File("account-navigation.json"));
            foreach (var (folder, subject) in new[] { ("INBOX", "Saved inbox"), ("Archive", "Saved archive") }) await f.Cache.SaveSummariesAsync(new(f.Account.Id, folder, 7), [Row(f.Account.Id, folder, subject)], DateTimeOffset.UtcNow);
            return f;
        }
        public InboxViewModel Model() => new(Receiver, new ImmediateUiDispatcher(), Cache, navigation: Store);
        public void Dispose() => Directory.Dispose();
    }
    private sealed class Receiver : IMailFolderReceiver
    {
        public int Calls;
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) { Calls++; throw new InvalidOperationException("No automatic body fetch."); }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) { Calls++; throw new InvalidOperationException(); }
        public Task<IReadOnlyList<MailFolder>> GetFoldersAsync(AccountProfile account, CancellationToken token = default) { Calls++; throw new InvalidOperationException(); }
        public Task<MailInboxPage> GetMailboxAsync(AccountProfile account, string folder, int count, MailInboxCursor? older = null, CancellationToken token = default) { Calls++; throw new InvalidOperationException("No automatic receive."); }
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => GetMailboxAsync(account, "INBOX", count, older, token);
    }
}
