using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class RecipientBookTests
{
    [Fact]
    public async Task ReopeningUpsertingRemovingAndConcurrentUpdatesPreserveAccountPartitions()
    {
        using var directory = new TestDirectory(); string path = directory.File("recipients.json"); var store = new JsonRecipientBook(path);
        var one = AccountId.New(); var two = AccountId.New();
        Assert.Empty(await store.LoadAsync(one)); Assert.False(File.Exists(path));
        await Task.WhenAll(store.SaveAsync(new(one, "one@example.test", "One")), new JsonRecipientBook(path).SaveAsync(new(two, "one@example.test", "Other account")));
        await store.SaveAsync(new(one, "ONE@example.test", "Updated"));
        Assert.Equal("Updated", (await new JsonRecipientBook(path).LoadAsync(one)).Single().Name);
        Assert.Equal("Other account", (await store.LoadAsync(two)).Single().Name);
        await store.RemoveAsync(one, "one@example.test"); Assert.Empty(await store.LoadAsync(one)); Assert.Single(await store.LoadAsync(two));
    }

    [Theory]
    [InlineData("a@example.test,b@example.test", "Name")]
    [InlineData("Name <a@example.test>", "Name")]
    [InlineData("a@example.test\r\nBcc: hidden@example.test", "Name")]
    [InlineData("a@example.test", "bad\nname")]
    [InlineData("not-a-mailbox", "Name")]
    [InlineData("", "Name")]
    public void InvalidOrHeaderInjectingEntriesAreRejected(string address, string name) => Assert.Throws<ArgumentException>(() => SavedRecipient.Create(AccountId.New(), address, name));

    [Theory]
    [InlineData("future")]
    [InlineData("duplicate")]
    [InlineData("corrupt")]
    public async Task UnreadableBooksAreNeverResetBySaveOrRemove(string kind)
    {
        using var directory = new TestDirectory(); string path = directory.File("recipients.json"); var store = new JsonRecipientBook(path); var account = AccountId.New();
        await store.SaveAsync(new(account, "one@example.test", "One")); var json = JsonNode.Parse(File.ReadAllText(path))!;
        if (kind == "future") json["schemaVersion"] = 2;
        if (kind == "duplicate") { var entries = json["data"]!["recipients"]!.AsArray(); entries.Add(entries[0]!.DeepClone()); }
        File.WriteAllText(path, kind == "corrupt" ? "broken" : json.ToJsonString()); byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(account));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new(account, "two@example.test", "Two")));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RemoveAsync(account, "one@example.test"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task LimitsCancellationAndInvalidAccountsPreserveTheExistingBook()
    {
        using var directory = new TestDirectory(); string path = directory.File("recipients.json"); var store = new JsonRecipientBook(path); var account = AccountId.New();
        await store.SaveAsync(new(account, "one@example.test", "One")); byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(account, "two@example.test", "Two"), new(true)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new(account, "one@example.test", new string('n', 121))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(default));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task SuggestionsAreBoundedCaseInsensitivePrefixMatchesAndNeverHarvestBcc()
    {
        using var directory = new TestDirectory(); var store = new JsonRecipientBook(directory.File("recipients.json")); var account = TestDirectory.Profile();
        for (int i = 0; i < 12; i++) await store.SaveAsync(new(account.Id, $"person{i:D2}@example.test", $"Person {i:D2}"));
        using var model = new ComposerViewModel(recipientBook: store); model.SetAccount(account); await model.RecipientBook!.LoadTask;
        model.StartNew(); model.Edit("to@example.test", "cc@example.test", "secret@example.test", "Subject", "Body"); await model.SaveAsync();
        Assert.Equal(10, model.RecipientBook.Suggest("PERSON").Count); Assert.Single(model.RecipientBook.Suggest("person11@")); Assert.Empty(model.RecipientBook.Suggest("erson"));
        Assert.Equal(12, (await store.LoadAsync(account.Id)).Count); Assert.DoesNotContain(await store.LoadAsync(account.Id), row => row.Address == "secret@example.test");
        Assert.Empty(model.RecipientBook.Suggest(new string('x', 321)));
    }

    [Theory]
    [InlineData("To")]
    [InlineData("Cc")]
    [InlineData("Bcc")]
    public async Task ExplicitInsertionAutosavesWithoutChangingSenderThreadOrOtherFields(string field)
    {
        using var directory = new TestDirectory(); var store = new JsonRecipientBook(directory.File("recipients.json")); var account = TestDirectory.Profile();
        var selected = new SavedRecipient(account.Id, "saved@example.test", "Saved"); await store.SaveAsync(selected);
        var drafts = new JsonDraftStore(directory.File("drafts.json")); using var model = new ComposerViewModel(drafts, recipientBook: store);
        model.SetAccount(account); await model.RecipientBook!.LoadTask;
        model.StartFromMessage(new(new(account.Id, "INBOX", 7, 1), "Original") { Composition = new() { From = ["sender@example.test"], MessageId = "parent@example.test" } }, CompositionKind.Reply);
        model.Edit("first@example.test", "copy@example.test", "private@example.test", "Subject", "Body"); var id = model.DraftId; var from = model.FromAddress;
        Assert.True(model.InsertRecipient(field, selected)); Assert.False(model.InsertRecipient(field, selected)); Assert.True(await model.SaveAsync());
        var saved = (await drafts.LoadAsync()).Draft!; Assert.Equal(id, saved.Draft.Id); Assert.Equal(from, saved.Draft.FromAddress); Assert.Equal("parent@example.test", saved.Draft.InReplyTo);
        Assert.Equal("Subject", saved.Draft.Subject); Assert.Equal("Body", saved.Draft.PlainText);
        Assert.Equal(field == "To" ? "first@example.test, saved@example.test" : "first@example.test", saved.ToText);
        Assert.Equal(field == "Cc" ? "copy@example.test, saved@example.test" : "copy@example.test", saved.CcText);
        Assert.Equal(field == "Bcc" ? "private@example.test, saved@example.test" : "private@example.test", saved.BccText);
    }

    [Fact]
    public async Task ExistingInvalidFieldAndForeignOrUnselectedAddressCannotAlterDraft()
    {
        using var directory = new TestDirectory(); var store = new JsonRecipientBook(directory.File("recipients.json")); var account = TestDirectory.Profile(); var row = new SavedRecipient(account.Id, "saved@example.test", "Saved");
        await store.SaveAsync(row); using var model = new ComposerViewModel(recipientBook: store); model.SetAccount(account); await model.RecipientBook!.LoadTask; model.StartNew();
        model.Edit("unfinished <", "", "private@example.test", "Subject", "Body");
        Assert.False(model.InsertRecipient("To", row)); Assert.Equal("unfinished <", model.To); Assert.Equal("To", model.InvalidField);
        Assert.False(model.InsertRecipient("Bcc", row with { AccountId = AccountId.New() })); Assert.False(model.InsertRecipient("Bcc", row with { Address = "unknown@example.test" }));
        model.SetProfileStorageBusy(true); Assert.False(model.InsertRecipient("Bcc", row)); Assert.Equal("private@example.test", model.Bcc);
    }

    [Fact]
    public async Task LateLoadCannotReplaceAnotherAccountsSuggestions()
    {
        var one = AccountId.New(); var two = AccountId.New(); var pending = new TaskCompletionSource<IReadOnlyList<SavedRecipient>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new RecipientBookViewModel(new DelayedBook(one, pending), new ImmediateUiDispatcher());
        model.SetAccount(one); var old = model.LoadTask; model.SetAccount(two); await model.LoadTask;
        pending.SetResult([new(one, "old@example.test", "Old")]); await old;
        Assert.Equal(two, model.Recipients.Single().AccountId); Assert.Equal("new@example.test", model.Recipients.Single().Address);
    }

    [Theory]
    [InlineData(DraftSubmissionState.Accepted)]
    [InlineData(DraftSubmissionState.Unknown)]
    [InlineData(DraftSubmissionState.Sending)]
    public async Task SubmittedOrUncertainDraftsCannotBeChangedByRecipientInsertion(DraftSubmissionState state)
    {
        using var directory = new TestDirectory(); var store = new JsonRecipientBook(directory.File("recipients.json")); var account = TestDirectory.Profile();
        var row = new SavedRecipient(account.Id, "saved@example.test", "Saved"); await store.SaveAsync(row);
        var snapshot = new DraftSnapshot { Draft = MailComposition.Create(account), ToText = "original@example.test", CcText = "", BccText = "", State = state };
        using var model = new ComposerViewModel(initial: new(1, snapshot), recipientBook: store);
        model.SetAccount(account); await model.RecipientBook!.LoadTask;
        Assert.False(model.InsertRecipient("To", row)); Assert.Equal("original@example.test", model.To);
    }

    [Fact]
    public async Task AccountLimitFailureKeepsAllPreviouslySavedAddresses()
    {
        using var directory = new TestDirectory(); string path = directory.File("recipients.json"); var store = new JsonRecipientBook(path); var account = AccountId.New();
        await store.SaveAsync(new(account, "first@example.test", "First"));
        var json = JsonNode.Parse(File.ReadAllText(path))!; var entries = json["data"]!["recipients"]!.AsArray(); var template = entries[0]!.DeepClone();
        for (int i = 1; i < 500; i++) { var entry = template.DeepClone(); entry["address"] = $"person{i}@example.test"; entries.Add(entry); }
        File.WriteAllText(path, json.ToJsonString()); byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new(account, "overflow@example.test", "Overflow")));
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(500, (await store.LoadAsync(account)).Count);
    }

    [Fact]
    public async Task DisposedRecipientBookIgnoresLateUncancellableLoad()
    {
        var account = AccountId.New(); var pending = new TaskCompletionSource<IReadOnlyList<SavedRecipient>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new ClosedDispatcher(); var model = new RecipientBookViewModel(new DelayedBook(account, pending), dispatcher);
        model.SetAccount(account); var loading = model.LoadTask; model.Dispose();
        pending.SetResult([new(account, "late@example.test", "Late")]); await loading;
        Assert.Equal(0, dispatcher.Posts); Assert.Empty(model.Recipients);
    }

    [Fact]
    public async Task BrokenRecipientBookDoesNotPreventManualCompositionOrOverwriteTheBook()
    {
        using var directory = new TestDirectory(); directory.Create(); string path = directory.File("recipients.json"); File.WriteAllText(path, "corrupt saved recipients");
        var drafts = new JsonDraftStore(directory.File("drafts.json")); using var model = new ComposerViewModel(drafts, recipientBook: new JsonRecipientBook(path));
        model.SetAccount(TestDirectory.Profile()); await model.RecipientBook!.LoadTask;
        Assert.True(model.RecipientBook.IsError); Assert.False(model.RecipientBook.IsReady); Assert.True(model.StartNew());
        model.Edit("manual@example.test", "", "private@example.test", "Subject", "Body"); Assert.True(await model.SaveAsync());
        Assert.Equal("manual@example.test", (await drafts.LoadAsync()).Draft!.ToText); Assert.Equal("corrupt saved recipients", File.ReadAllText(path));
    }

    private sealed class ClosedDispatcher : Broiler.UI.IUiDispatcher
    {
        public int Posts { get; private set; }
        public bool CheckAccess() => true;
        public void Post(Action callback) { Posts++; throw new ObjectDisposedException("closed test dispatcher"); }
    }

    private sealed class DelayedBook(AccountId delayed, TaskCompletionSource<IReadOnlyList<SavedRecipient>> pending) : IRecipientBook
    {
        public Task<IReadOnlyList<SavedRecipient>> LoadAsync(AccountId account, CancellationToken token = default) => account == delayed ? pending.Task : Task.FromResult<IReadOnlyList<SavedRecipient>>([new(account, "new@example.test", "New")]);
        public Task SaveAsync(SavedRecipient recipient, CancellationToken token = default) => throw new InvalidOperationException();
        public Task RemoveAsync(AccountId account, string address, CancellationToken token = default) => throw new InvalidOperationException();
    }
}
