using Broiler.Mail.Application;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class DraftRecoveryTests
{
    [Fact]
    public async Task AutosaveRecoversInvalidRawEditsSenderAndThreadAcrossRestart()
    {
        using var directory = new TestDirectory();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        Assert.Equal(new DraftStoreState(0, null), await store.LoadAsync());
        Assert.False(Directory.Exists(directory.Root));
        var account = Account();
        using var model = new ComposerViewModel(store);
        model.SetAccount(account);
        model.StartFromMessage(new(new(account.Id, "INBOX", 7, 3), "Quote")
        {
            Composition = new() { From = ["sender@example.test"], MessageId = "parent@example.test", References = ["root@example.test"] },
        }, CompositionKind.Reply);
        Guid? id = model.DraftId;
        model.Edit("unfinished <", "", "private@example.test", "Unfinished\nsubject", "Grüße\nraw body\0");
        Assert.True(await model.SaveAsync());
        var saved = await new JsonDraftStore(directory.File("drafts.json")).LoadAsync();
        using var recovered = new ComposerViewModel(store, saved);
        recovered.SetAccount(account with { EmailAddress = "changed@example.test" });
        Assert.Equal(id, recovered.DraftId);
        Assert.Equal(account.EmailAddress, recovered.FromAddress);
        Assert.Equal(model.To, recovered.To); Assert.Equal(model.Bcc, recovered.Bcc);
        Assert.Equal(model.Subject, recovered.Subject); Assert.Equal(model.PlainText, recovered.PlainText);
        Assert.Equal("parent@example.test", saved.Draft!.Draft.InReplyTo);
        Assert.Equal(new[] { "root@example.test", "parent@example.test" }, saved.Draft.Draft.References);
        Assert.Throws<InvalidOperationException>(() => recovered.BuildDraft());
        Assert.Contains("saved locally", recovered.StorageStatus);
    }

    [Fact]
    public async Task SavedDiscardSurvivesRestartAndStaleWriterCannotResurrectIt()
    {
        using var directory = new TestDirectory();
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using var model = Create(store);
        Assert.True(await model.SaveAsync());
        var previous = await store.LoadAsync();
        Assert.True(await model.DiscardAsync());
        var deleted = await store.LoadAsync();
        Assert.Null(deleted.Draft);
        Assert.True(deleted.Revision > previous.Revision);
        await Assert.ThrowsAsync<DraftConflictException>(() => store.SaveAsync(previous.Revision, previous.Draft));
        Assert.Null((await store.LoadAsync()).Draft);
    }

    [Fact]
    public async Task CompetingInstancesNeverSilentlyReplaceEachOthersDraft()
    {
        using var directory = new TestDirectory();
        string path = directory.File("drafts.json");
        using var first = Create(new JsonDraftStore(path));
        Assert.True(await first.SaveAsync());
        var state = await new JsonDraftStore(path).LoadAsync();
        using var second = new ComposerViewModel(new JsonDraftStore(path), state);
        second.SetAccount(Account());
        first.Edit("one@example.test", "", "", "first writer", "first text");
        Assert.True(await first.SaveAsync());
        second.Edit("two@example.test", "", "", "second writer", "second text");
        Assert.False(await second.SaveAsync());
        Assert.Contains("Another app instance", second.StorageStatus);
        Assert.Equal("second text", second.PlainText);
        Assert.Equal("first writer", (await new JsonDraftStore(path).LoadAsync()).Draft!.Draft.Subject);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("{\"schemaVersion\":9,\"data\":{\"revision\":0,\"draft\":null}}")]
    [InlineData("{\"schemaVersion\":1,\"data\":{}}")]
    public async Task CorruptDraftDoesNotBlockAccountLoadingOrGetOverwritten(string contents)
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("drafts.json");
        await File.WriteAllTextAsync(path, contents);
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        await accounts.SaveAsync(Account());
        var credentials = new TestCredentialStore();
        var app = new MailApplication(accounts, new JsonSettingsStore(directory.File("settings.json")),
            new TestMailReceiver(), new SmtpMailSender(credentials), credentials, new JsonDraftStore(path));
        await app.InitializeAsync();
        Assert.NotNull(app.LoadedAccount);
        Assert.NotNull(app.DraftLoadError);
        using var model = new ComposerViewModel(app.Drafts, app.LoadedDraft, loadError: app.DraftLoadError);
        model.SetAccount(app.LoadedAccount);
        Assert.False(model.StartNew());
        Assert.False(await model.SaveAsync());
        Assert.True(await model.PrepareCloseAsync()); // A broken file must not trap a read-only app open.
        Assert.Equal(contents, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveAndDiscardFailuresRetainTextAndKeepClosePendingUntilRetrySucceeds()
    {
        var store = new ControlledStore();
        using var model = Create(store);
        Assert.True(await model.SaveAsync());
        store.Fail = true;
        model.Edit("bad address", "", "private@example.test", "Changed", "latest unsaved text");
        Assert.False(await model.SaveAsync());
        Assert.False(await model.PrepareCloseAsync());
        Assert.True(model.CanEdit);
        Assert.False(await model.DiscardAsync());
        Assert.True(model.HasDraft);
        Assert.Equal("latest unsaved text", model.PlainText);
        store.Fail = false;
        Assert.True(await model.PrepareCloseAsync());
        Assert.False(model.CanEdit);
        Assert.Equal("latest unsaved text", (await store.LoadAsync()).Draft!.Draft.PlainText);
    }

    [Fact]
    public async Task CorruptionAfterOpeningDoesNotFaultAutosaveOrOverwriteTheFile()
    {
        using var directory = new TestDirectory();
        string path = directory.File("drafts.json");
        using var model = Create(new JsonDraftStore(path));
        Assert.True(await model.SaveAsync());
        await File.WriteAllTextAsync(path, "broken after startup");
        model.Edit("a@example.test", "", "", "new", "still in memory");
        Assert.False(await model.SaveAsync());
        Assert.Contains("not saved", model.StorageStatus);
        Assert.Equal("still in memory", model.PlainText);
        Assert.Equal("broken after startup", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AutosaveSerializesWritesAndCoalescesRapidEdits()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ControlledStore { BeforeSave = async call => { if (call == 1) { entered.SetResult(); await release.Task; } } };
        using var model = Create(store);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int index = 0; index < 100; index++) model.Edit("recipient@example.test", "", "", "draft", $"edit {index}");
        release.SetResult();
        Assert.True(await model.SaveAsync());
        Assert.Equal(2, store.Calls);
        Assert.Equal("edit 99", (await store.LoadAsync()).Draft!.Draft.PlainText);
    }

    [Theory]
    [InlineData(SubmissionStatus.Accepted, DraftSubmissionState.Accepted)]
    [InlineData(SubmissionStatus.Rejected, DraftSubmissionState.Failed)]
    [InlineData(SubmissionStatus.Unknown, DraftSubmissionState.Unknown)]
    public async Task SubmissionPersistsIntentBeforeSendingAndRetainsEveryOutcome(SubmissionStatus result, DraftSubmissionState expected)
    {
        var store = new ControlledStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Sender(async (_, _) =>
        {
            Assert.Equal(DraftSubmissionState.Sending, (await store.LoadAsync()).Draft!.State);
            entered.SetResult();
            return await complete.Task;
        });
        using var model = Create(store, sender);
        var sending = model.SendAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DraftSubmissionState.Sending, model.SubmissionState);
        Assert.False(model.CanEdit); Assert.False(await model.PrepareCloseAsync());
        Assert.False(await model.DiscardAsync());
        complete.SetResult(new(result));
        await sending;
        Assert.Equal(expected, model.SubmissionState);
        Assert.Equal(expected, (await store.LoadAsync()).Draft!.State);
        Assert.Equal("body", model.PlainText);
        Assert.True(model.HasDraft);
        if (result != SubmissionStatus.Rejected)
        {
            Assert.False(model.CanEdit);
            await model.SendAsync();
            Assert.Equal(1, sender.Calls);
        }
        else Assert.True(model.CanSend);
    }

    [Fact]
    public async Task TransportExceptionAndInterruptedSendRecoverAsUnknownWithoutResubmitting()
    {
        var store = new ControlledStore();
        var sender = new Sender((_, _) => throw new IOException("untrusted server detail"));
        using var model = Create(store, sender);
        await model.SendAsync();
        Assert.Equal(DraftSubmissionState.Unknown, model.SubmissionState);
        Assert.DoesNotContain("untrusted", model.Status);
        var saved = await store.LoadAsync();
        await store.SaveAsync(saved.Revision, saved.Draft! with { State = DraftSubmissionState.Sending });
        using var recovered = new ComposerViewModel(store, await store.LoadAsync(), sender: sender);
        recovered.SetAccount(Account());
        Assert.Equal(DraftSubmissionState.Unknown, recovered.SubmissionState);
        Assert.True(await recovered.SaveAsync());
        await recovered.SendAsync();
        Assert.Equal(1, sender.Calls);
        Assert.Equal(DraftSubmissionState.Unknown, (await store.LoadAsync()).Draft!.State);
    }

    [Fact]
    public async Task FailedIntentWritePreventsTransportCall()
    {
        var store = new ControlledStore { FailState = DraftSubmissionState.Sending };
        var sender = new Sender((_, _) => Task.FromResult(new SendResult(SubmissionStatus.Accepted)));
        using var model = Create(store, sender);
        await model.SendAsync();
        Assert.Equal(0, sender.Calls);
        Assert.Equal(DraftSubmissionState.Failed, model.SubmissionState);
        Assert.Equal("body", model.PlainText);
        store.FailState = null;
        await model.SendAsync();
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task AcceptedButUnsavedOutcomeCannotEnableDuplicateSendAndRestartsUnknown()
    {
        var store = new ControlledStore { FailState = DraftSubmissionState.Accepted };
        var sender = new Sender((_, _) => Task.FromResult(new SendResult(SubmissionStatus.Accepted)));
        using var model = Create(store, sender);
        await model.SendAsync();
        Assert.Equal(DraftSubmissionState.Accepted, model.SubmissionState);
        Assert.Contains("could not be saved", model.Status);
        Assert.Equal(DraftSubmissionState.Sending, (await store.LoadAsync()).Draft!.State);
        Assert.False(await model.PrepareCloseAsync());
        await model.SendAsync();
        Assert.Equal(1, sender.Calls);
        using var recovered = new ComposerViewModel(store, await store.LoadAsync(), sender: sender);
        recovered.SetAccount(Account());
        Assert.Equal(DraftSubmissionState.Unknown, recovered.SubmissionState);
        Assert.False(recovered.CanSend);
        Assert.True(await recovered.SaveAsync());
    }

    [Fact]
    public void StorageNotificationsUseDispatcherAndFlushDoesNotWaitForItsDrain()
    {
        var dispatcher = new StandardQueuedUiDispatcher();
        var owner = Environment.CurrentManagedThreadId;
        using var model = new ComposerViewModel(new ControlledStore(), dispatcher: dispatcher);
        model.SetAccount(Account()); model.StartNew();
        var observed = new List<int>();
        model.Changed += (_, _) => observed.Add(Environment.CurrentManagedThreadId);
        // Deliberately keep the owning thread idle: persistence must finish without a UI drain.
#pragma warning disable xUnit1031
        Assert.True(model.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
#pragma warning restore xUnit1031
        Assert.Empty(observed);
        dispatcher.Drain();
        Assert.NotEmpty(observed);
        Assert.All(observed, thread => Assert.Equal(owner, thread));
    }

    private static ComposerViewModel Create(IDraftStore store, IMailSender? sender = null)
    {
        var model = new ComposerViewModel(store, sender: sender);
        model.SetAccount(Account()); model.StartNew();
        model.Edit("recipient@example.test", "", "", "subject", "body");
        return model;
    }
    private static AccountProfile Account() => TestDirectory.Profile() with
    {
        Id = new(new Guid("bb260631-e8d1-4521-a145-60fe2f3aab90")),
        OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test", Security = TransportSecurity.StartTls },
    };
    private sealed class Sender(Func<AccountProfile, MailDraft, Task<SendResult>> send) : IMailSender
    {
        public int Calls { get; private set; }
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken cancellationToken = default)
        { Calls++; return send(account, draft); }
    }
    private sealed class ControlledStore : IDraftStore
    {
        private readonly MemoryDraftStore _inner = new();
        public bool IsPersistent => true;
        public bool Fail { get; set; }
        public DraftSubmissionState? FailState { get; set; }
        public Func<int, Task>? BeforeSave { get; init; }
        public int Calls { get; private set; }
        public Task<DraftStoreState> LoadAsync(CancellationToken cancellationToken = default) => _inner.LoadAsync(cancellationToken);
        public async Task<DraftStoreState> SaveAsync(long expectedRevision, DraftSnapshot? draft, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (BeforeSave is { } callback) await callback(Calls);
            if (Fail || FailState is { } state && draft?.State == state) throw new IOException("Simulated storage failure.");
            return await _inner.SaveAsync(expectedRevision, draft, cancellationToken);
        }
    }
}
