using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class DraftLibraryTests
{
    [Fact]
    public async Task FirstParkBacksUpExactLegacyFileAndActiveSavesPreserveLibrary()
    {
        using var directory = new TestDirectory(); string path = directory.File("drafts.json"); var store = new JsonDraftStore(path); var account = TestDirectory.Profile();
        var first = Snapshot(account); var saved = await store.SaveAsync(0, first); byte[] legacy = File.ReadAllBytes(path);
        Assert.Null(saved.SavedDrafts); var parked = await store.ParkAsync(saved.Revision);
        Assert.Null(parked.Draft); Assert.Equal(first.Draft.Id, parked.SavedDrafts!.Single().Draft.Id);
        Assert.Equal(legacy, File.ReadAllBytes(Directory.GetFiles(directory.Root, "drafts.json.before-library-*.backup").Single()));
        var second = Snapshot(account); saved = await new JsonDraftStore(path).SaveAsync(parked.Revision, second);
        Assert.Equal(first.Draft.Id, saved.SavedDrafts!.Single().Draft.Id);
        var swapped = await store.ActivateAsync(saved.Revision, first.Draft.Id);
        Assert.Equal(first.Draft.Id, swapped.Draft!.Draft.Id); Assert.Equal(second.Draft.Id, swapped.SavedDrafts!.Single().Draft.Id);
        var cleared = await store.SaveAsync(swapped.Revision, null); Assert.Null(cleared.Draft); Assert.Equal(second.Draft.Id, cleared.SavedDrafts!.Single().Draft.Id);
        Assert.Single(Directory.GetFiles(directory.Root, "drafts.json.before-library-*.backup"));
    }

    [Fact]
    public async Task StaleAndCancelledChangesNeverOverwriteAnotherDraft()
    {
        using var directory = new TestDirectory(); string path = directory.File("drafts.json"); var store = new JsonDraftStore(path);
        var initial = await store.SaveAsync(0, Snapshot(TestDirectory.Profile())); var parked = await store.ParkAsync(initial.Revision); byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<DraftConflictException>(() => store.ActivateAsync(initial.Revision, initial.Draft!.Draft.Id));
        await Assert.ThrowsAsync<DraftConflictException>(() => store.RemoveSavedAsync(initial.Revision, initial.Draft!.Draft.Id));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ActivateAsync(parked.Revision, initial.Draft!.Draft.Id, new(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ActivateAsync(parked.Revision, Guid.NewGuid()));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("state")]
    [InlineData("null")]
    public async Task InvalidLibraryIsRefusedWithoutReset(string kind)
    {
        using var directory = new TestDirectory(); string path = directory.File("drafts.json"); var store = new JsonDraftStore(path);
        var saved = await store.SaveAsync(0, Snapshot(TestDirectory.Profile())); await store.ParkAsync(saved.Revision);
        var json = JsonNode.Parse(File.ReadAllText(path))!; var rows = json["data"]!["savedDrafts"]!.AsArray();
        if (kind == "duplicate") rows.Add(rows[0]!.DeepClone());
        if (kind == "state") rows[0]!["state"] = "Unrecognized";
        if (kind == "null") rows.Add(null);
        File.WriteAllText(path, json.ToJsonString()); byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync()); await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(saved.Revision + 1, null));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task LibraryLimitKeepsActiveTextAndAllowsAtomicSwapAtCapacity()
    {
        using var directory = new TestDirectory(); string path = directory.File("drafts.json"); var store = new JsonDraftStore(path); var account = TestDirectory.Profile(); DraftStoreState state = new(0, null);
        for (int i = 0; i < 20; i++) { state = await store.SaveAsync(state.Revision, Snapshot(account)); state = await store.ParkAsync(state.Revision); }
        state = await store.SaveAsync(state.Revision, Snapshot(account)); var active = state.Draft!.Draft.Id; byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ParkAsync(state.Revision)); Assert.Equal(before, File.ReadAllBytes(path));
        state = await store.ActivateAsync(state.Revision, state.SavedDrafts![0].Draft.Id);
        Assert.Equal(20, state.SavedDrafts!.Count); Assert.Contains(state.SavedDrafts, row => row.Draft.Id == active);
    }

    [Fact]
    public async Task ComposerParksAndSwapsRawEditsAcrossAccountsWithoutChangingIdentity()
    {
        using var directory = new TestDirectory(); var store = new JsonDraftStore(directory.File("drafts.json")); var first = TestDirectory.Profile(); var second = TestDirectory.Profile();
        using var model = new ComposerViewModel(store); model.SetAccount(first); model.StartNew(); var firstId = model.DraftId;
        model.Edit("unfinished <", "copy@example.test", "private@example.test", "First subject", "First body"); Assert.True(await model.ParkDraftAsync());
        Assert.False(model.HasDraft); Assert.True(model.CanStart); Assert.Single(model.SavedDrafts);
        model.SetAccount(second); Assert.Empty(model.SavedDrafts); model.StartNew(); var secondId = model.DraftId;
        model.Edit("second@example.test", "", "", "Second subject", "Second body"); model.SetAccount(first);
        Assert.True(await model.ResumeDraftAsync(firstId!.Value)); Assert.Equal(firstId, model.DraftId); Assert.Equal(first.EmailAddress, model.FromAddress);
        Assert.Equal("unfinished <", model.To); Assert.Equal("private@example.test", model.Bcc); Assert.Equal("First body", model.PlainText);
        model.SetAccount(second); Assert.Equal(secondId, model.SavedDrafts.Single().Draft.Id);
        Assert.True(await model.ResumeDraftAsync(secondId!.Value)); Assert.Equal(secondId, model.DraftId); Assert.Equal("Second body", model.PlainText);
        Assert.True(await model.SaveAsync()); var reopened = await new JsonDraftStore(directory.File("drafts.json")).LoadAsync(); Assert.Equal(firstId, reopened.SavedDrafts!.Single().Draft.Id);
    }

    [Theory]
    [InlineData(DraftSubmissionState.Accepted, SentCopyState.Saved)]
    [InlineData(DraftSubmissionState.Unknown, SentCopyState.NotRequested)]
    [InlineData(DraftSubmissionState.Sending, SentCopyState.NotRequested)]
    [InlineData(DraftSubmissionState.Accepted, SentCopyState.Pending)]
    public async Task ResumingSubmissionRecordsPreservesOutcomesAndNeverEnablesResend(DraftSubmissionState submission, SentCopyState copy)
    {
        using var directory = new TestDirectory(); var store = new JsonDraftStore(directory.File("drafts.json")); var account = TestDirectory.Profile();
        var saved = await store.SaveAsync(0, Snapshot(account) with { State = submission, SentCopy = copy, SentCopyFolder = copy == SentCopyState.NotRequested ? null : "Sent" });
        saved = await store.ParkAsync(saved.Revision); using var model = new ComposerViewModel(store, saved); model.SetAccount(account);
        Assert.True(await model.ResumeDraftAsync(saved.SavedDrafts!.Single().Draft.Id));
        Assert.Equal(submission == DraftSubmissionState.Sending ? DraftSubmissionState.Unknown : submission, model.SubmissionState);
        Assert.Equal(copy == SentCopyState.Pending ? SentCopyState.Unknown : copy, model.SentCopy); Assert.False(model.CanEdit); Assert.False(model.CanSend); Assert.True(await model.SaveAsync());
        Assert.Equal(model.SubmissionState, (await store.LoadAsync()).Draft!.State);
    }

    [Fact]
    public async Task RemovalRequiresSeparateConfirmationAndCannotCrossSelectedAccounts()
    {
        using var directory = new TestDirectory(); var store = new JsonDraftStore(directory.File("drafts.json")); var account = TestDirectory.Profile();
        using var model = new ComposerViewModel(store); model.SetAccount(account); model.StartNew(); var id = model.DraftId!.Value; await model.ParkDraftAsync();
        Assert.False(await model.ConfirmSavedDraftRemovalAsync()); Assert.True(model.RequestSavedDraftRemoval(id)); model.KeepSavedDraft(); Assert.False(await model.ConfirmSavedDraftRemovalAsync());
        model.RequestSavedDraftRemoval(id); model.SetAccount(TestDirectory.Profile()); Assert.Null(model.SavedDraftRemoval); Assert.False(await model.ConfirmSavedDraftRemovalAsync()); Assert.False(await model.ResumeDraftAsync(id));
        model.SetAccount(account); model.StartNew(); var active = model.DraftId; model.Edit("active@example.test", "", "", "Active", "Keep this");
        Assert.True(model.RequestSavedDraftRemoval(id)); Assert.True(await model.ConfirmSavedDraftRemovalAsync()); Assert.Empty(model.SavedDrafts); Assert.Equal(active, model.DraftId); Assert.Equal("Keep this", model.PlainText);
    }

    [Fact]
    public async Task FailedActiveFlushKeepsDraftAndDoesNotParkOrSwap()
    {
        using var directory = new TestDirectory(); var store = new JsonDraftStore(directory.File("drafts.json")); var account = TestDirectory.Profile();
        using var model = new ComposerViewModel(store); model.SetAccount(account); model.StartNew(); model.Edit("a@example.test", "", "", "First", "Keep this"); Assert.True(await model.SaveAsync());
        var other = await store.LoadAsync(); await store.SaveAsync(other.Revision, Snapshot(account));
        model.Edit("a@example.test", "", "", "Unsaved change", "Still open"); Assert.False(await model.ParkDraftAsync()); Assert.True(model.HasDraft); Assert.Equal("Still open", model.PlainText); Assert.Empty((await store.LoadAsync()).SavedDrafts ?? []);
    }

    private static DraftSnapshot Snapshot(AccountProfile account) => new() { Draft = MailComposition.Create(account) with { Subject = "Fixture", PlainText = "Body" }, ToText = "raw <", CcText = "", BccText = "private@example.test", State = DraftSubmissionState.Editing };
}
