using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class AccountSelectionTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task LocalMailCleanupHasSeparateKeepFirstConfirmationAndRetainsDraftAccountAndOtherMail(double scale)
    {
        using var fixture = await Fixture.Create(removal: true); var model = fixture.Model;
        Assert.True(model.Composer.StartNew()); model.Composer.Edit("recipient@example.test", "", "", "Keep draft", "Unsent local text"); var draftId = model.Composer.DraftId;
        await model.Accounts!.InspectLocalMailAsync(); fixture.Dispatcher.Drain(); Assert.Equal(1, model.Accounts.LocalMailInfo!.HeaderCount);
        using var shell = new MailShellView(model); using var session = new StandardUiSessionBuilder().WithDispatcher(fixture.Dispatcher).Build(new Host()); shell.Attach(session);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(shell.Window, theme); session.RenderFrame();
        var root = shell.AccountSelectionContent!;
        Broiler.UI.Button.Standard.StandardButton Button(string label) => Descendants(root).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(button => button.Text == label);
        Button("Manage accounts").Click(); Button("Clear this account's downloaded mail…").Click(); session.RenderFrame();
        Assert.Same(Button("Keep downloaded mail"), session.FocusedElement); Assert.False(Button("Back up and clear local mail").IsDefault);
        Assert.Null(model.Accounts.PendingRemoval); Assert.False(model.Accounts.CanSwitch); Assert.False(model.Accounts.CanRemove);
        Button("Keep downloaded mail").Click(); Assert.Null(model.Accounts.PendingLocalMailCleanup); Assert.True(model.Accounts.CanSwitch);
        Button("Clear this account's downloaded mail…").Click(); Button("Back up and clear local mail").Click(); fixture.Dispatcher.DrainUntil(() => !model.Accounts.IsBusy);
        await model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain(); Assert.Empty(model.Inbox.Messages); Assert.Null(model.Inbox.Body); Assert.True(model.Inbox.CanReceive);
        Assert.Equal(fixture.First, model.CurrentAccount); Assert.Equal(2, (await fixture.Store.LoadAsync()).Count); Assert.Equal(draftId, model.Composer.DraftId); Assert.Equal("Unsent local text", model.Composer.PlainText);
        Assert.NotNull(model.Accounts.LastLocalMailCleanupBackup); Assert.Null(model.Accounts.PendingLocalMailCleanup); Assert.True(model.Accounts.CanInspectLocalMail);
        var recovered = new SqliteMailCache(Path.Combine(model.Accounts.LastLocalMailCleanupBackup, "mail-cache.db")); Assert.Equal("Body A", (await recovered.ReadBodyAsync(new(fixture.First.Id, "INBOX", 7), 1))!.PlainText);
        await fixture.Switch(fixture.Second.Id); await model.Inbox.SelectAsync(new(fixture.Second.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain(); Assert.Equal("Body B", model.Inbox.Body!.PlainText);
        Assert.Null(model.Accounts.LocalMailInfo); Assert.Null(model.Accounts.LastLocalMailCleanupBackup);
    }

    [Fact]
    public async Task CleanupFailureReleasesReaderAndKeepsItsSavedMail()
    {
        using var fixture = await Fixture.Create(removal: true); var model = fixture.Model;
        // The verified snapshot rejects this unrelated corrupt configuration before any deletion.
        File.WriteAllText(fixture.Directory.File("settings.json"), "broken");
        Assert.True(model.Accounts!.RequestLocalMailCleanup()); await model.Accounts.ConfirmLocalMailCleanupAsync(); fixture.Dispatcher.Drain();
        Assert.Null(model.Accounts.PendingLocalMailCleanup); Assert.False(model.Accounts.IsBusy); Assert.True(model.Inbox.CanReceive); Assert.True(model.Accounts.CanSwitch);
        Assert.Single(model.Inbox.Messages); Assert.Contains("did not finish", model.Accounts.Status);
        await model.Inbox.SelectAsync(new(fixture.First.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain(); Assert.Equal("Body A", model.Inbox.Body!.PlainText);
    }

    [Fact]
    public async Task PreCanceledCleanupDoesNotDeleteMailOrLeaveStorageMaintenanceActive()
    {
        using var fixture = await Fixture.Create(removal: true); var model = fixture.Model;
        Assert.True(model.Accounts!.RequestLocalMailCleanup()); await model.Accounts.ConfirmLocalMailCleanupAsync(new(true)); fixture.Dispatcher.Drain();
        Assert.False(model.Accounts.IsBusy); Assert.Null(model.Accounts.PendingLocalMailCleanup); Assert.True(model.Inbox.CanReceive); Assert.Single(model.Inbox.Messages); Assert.Null(model.Accounts.LastLocalMailCleanupBackup);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task RemovalRequiresExplicitConfirmationAndLastRemovalLeavesFirstSetupReachable(double scale)
    {
        using var fixture = await Fixture.Create(removal: true); var model = fixture.Model;
        using var shell = new MailShellView(model); using var session = new StandardUiSessionBuilder().WithDispatcher(fixture.Dispatcher).Build(new Host()); shell.Attach(session);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(shell.Window, theme); session.RenderFrame();
        var root = shell.AccountSelectionContent!;
        Broiler.UI.Button.Standard.StandardButton Button(string text) => Descendants(root).OfType<Broiler.UI.Button.Standard.StandardButton>().Single(b => b.Text == text);
        Button("Manage accounts").Click(); session.RenderFrame(); Button("Remove account…").Click();
        Assert.Equal(fixture.First, model.Accounts!.PendingRemoval); Assert.False(model.Accounts.CanSwitch);
        Assert.Equal("Cancel removal", Assert.IsType<Broiler.UI.Button.Standard.StandardButton>(session.FocusedElement).Text);
        Assert.Equal(2, (await fixture.Store.LoadStateAsync()).Accounts.Length);
        Button("Cancel removal").Click(); Assert.Null(model.Accounts.PendingRemoval);
        Button("Remove account…").Click(); Button("Remove, keep passwords").Click();
        fixture.Dispatcher.DrainUntil(() => !model.Accounts.IsBusy); await model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain();
        Assert.Equal(fixture.Second, model.CurrentAccount); Assert.NotNull(model.Accounts.LastRemovalBackup);
        Assert.Equal(2, (await new JsonAccountCollectionStore(model.Accounts.LastRemovalBackup).LoadStateAsync()).Accounts.Length);
        Assert.True(model.Accounts.RequestRemoval()); await model.Accounts.ConfirmRemovalAsync(false); fixture.Dispatcher.Drain();
        session.RenderFrame(); Assert.Null(model.CurrentAccount); Assert.Empty((await fixture.Store.LoadStateAsync()).Accounts);
        Assert.Equal("Add account", Assert.IsType<Broiler.UI.Button.Standard.StandardButton>(session.FocusedElement).Text);
        Assert.True(session.FocusedElement!.CanFocus);
    }

    [Fact]
    public async Task RetainedDraftBlocksItsOwnAccountRemovalButSurvivesRemovingAnother()
    {
        using var fixture = await Fixture.Create(removal: true); var model = fixture.Model;
        Assert.True(model.Composer.StartNew()); model.Composer.Edit("to@example.test", "", "", "Retained draft", "Keep text");
        var draftId = model.Composer.DraftId; Assert.False(model.Accounts!.CanRemove); Assert.False(model.Accounts.RequestRemoval());
        await fixture.Switch(fixture.Second.Id); Assert.True(model.Accounts.RequestRemoval());
        await model.Accounts.ConfirmRemovalAsync(false); fixture.Dispatcher.Drain(); await model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain();
        Assert.Equal(fixture.First, model.CurrentAccount); Assert.Equal(draftId, model.Composer.DraftId); Assert.True(model.Composer.CanSend);
        Assert.False(model.Accounts.CanRemove);
    }

    [Fact]
    public async Task DisableAndEnableRetainCredentialCacheAndPinnedDraftWithoutReceiving()
    {
        int received = 0;
        using var fixture = await Fixture.Create(new TestMailReceiver { Inbox = (_, _) => { received++; return Task.FromResult(new MailInboxPage([], null)); } });
        var model = fixture.Model;
        fixture.Dispatcher.DrainUntil(() => model.Account.HasPassword is not null);
        var key = CredentialKey.For(fixture.First, MailProtocol.Imap);
        await fixture.Application.Credentials.WriteAsync(key, "synthetic retained secret");
        Assert.True(model.Composer.StartNew()); model.Composer.Edit("to@example.test", "", "", "Draft", "Keep");
        await model.Accounts!.SetEnabledAsync(false); fixture.Dispatcher.Drain(); await model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain();
        Assert.False(model.CurrentAccount!.IsEnabled); Assert.False(model.Inbox.CanReceive); Assert.False(model.Composer.CanSend);
        Assert.Equal(fixture.First.Id, model.CurrentAccount.Id); Assert.Equal(fixture.First.EmailAddress, model.Composer.FromAddress);
        Assert.Equal("synthetic retained secret", await fixture.Application.Credentials.ReadAsync(key));
        await model.Inbox.SelectAsync(new(fixture.First.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain(); Assert.Equal("Body A", model.Inbox.Body!.PlainText);
        Assert.False((await fixture.Store.LoadStateAsync()).Accounts.Single(p => p.Id == fixture.First.Id).IsEnabled);
        await model.Accounts.SetEnabledAsync(true); fixture.Dispatcher.Drain(); await model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain();
        Assert.True(model.Composer.CanSend); Assert.True(model.Inbox.CanReceive); Assert.Equal(0, received);
    }

    [Fact]
    public async Task OrderingPersistsWithoutChangingSelectionOrResettingTheReader()
    {
        using var fixture = await Fixture.Create(); var model = fixture.Model;
        await model.Inbox.SelectAsync(new(fixture.First.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain(); var body = model.Inbox.Body;
        Assert.False(model.Accounts!.CanMoveEarlier); Assert.True(model.Accounts.CanMoveLater);
        await model.Accounts.MoveSelectedAsync(1); fixture.Dispatcher.Drain();
        var state = await fixture.Store.LoadStateAsync(); Assert.Equal([fixture.Second.Id, fixture.First.Id], state.Accounts.Select(p => p.Id));
        Assert.Equal(fixture.First.Id, state.SelectedAccountId); Assert.Same(body, model.Inbox.Body);
        Assert.True(model.Accounts.CanMoveEarlier); Assert.False(model.Accounts.CanMoveLater);
        await model.Accounts.MoveSelectedAsync(-1); fixture.Dispatcher.Drain();
        Assert.Equal(fixture.First.Id, (await fixture.Store.LoadStateAsync()).Accounts[0].Id);
    }

    [Fact]
    public async Task UnsavedEditsAndCancellationCannotPartiallyChangeManagementState()
    {
        using var fixture = await Fixture.Create(); var model = fixture.Model;
        string path = fixture.Directory.File("account-collection.json"); byte[] original = File.ReadAllBytes(path);
        model.Account.DisplayName = "Unsaved"; model.Account.NotifyEdited();
        await model.Accounts!.SetEnabledAsync(false); await model.Accounts.MoveSelectedAsync(1);
        Assert.Equal(original, File.ReadAllBytes(path));
        model.Account.DisplayName = fixture.First.DisplayName; model.Account.NotifyEdited();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await model.Accounts.SetEnabledAsync(false, cancel.Token); fixture.Dispatcher.Drain();
        await model.Accounts.MoveSelectedAsync(1, cancel.Token); fixture.Dispatcher.Drain();
        Assert.Equal(original, File.ReadAllBytes(path)); Assert.True(model.CurrentAccount!.IsEnabled); Assert.True(model.Account.CanSave);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task ManagementStaysBoundedAndMoveBoundaryHandsFocusToAnEnabledControl(double textScale)
    {
        using var fixture = await Fixture.Create(); using var shell = new MailShellView(fixture.Model);
        using var session = new StandardUiSessionBuilder().WithDispatcher(fixture.Dispatcher).Build(new Host()); shell.Attach(session);
        var theme = StandardThemeTokens.Light.WithTextScale(textScale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(shell.Window, theme); session.RenderFrame();
        var area = Assert.IsType<BoundedScrollArea>(shell.AccountSelectionContent);
        var buttons = Descendants(area).OfType<Broiler.UI.Button.Standard.StandardButton>().ToArray();
        buttons.Single(b => b.Text == "Manage accounts").Click(); session.RenderFrame();
        Assert.True(area.Bounds.Height <= area.AvailableHeight * 0.4 + 1);
        var later = buttons.Single(b => b.Text == "Move account later"); session.SetFocus(later); later.Click();
        fixture.Dispatcher.DrainUntil(() => !fixture.Model.Accounts!.IsBusy); session.RenderFrame();
        Assert.False(later.IsEnabled); Assert.Equal("Disable account", Assert.IsType<Broiler.UI.Button.Standard.StandardButton>(session.FocusedElement).Text);
        Assert.True(session.FocusedElement!.CanFocus);
        Assert.InRange(session.FocusedElement.Bounds.Top, area.Scroll.Bounds.Top - 1, area.Scroll.Bounds.Bottom + 1);
        var manage = buttons.Single(b => b.Text == "Hide account management"); manage.Click();
        Assert.Same(manage, session.FocusedElement); Assert.True(manage.CanFocus);
    }

    [Fact]
    public async Task FirstAccountSetupCanBeReopenedWithoutDiscardingTypedDetails()
    {
        using var directory = new TestDirectory();
        using var application = new MailApplication(new JsonAccountCollectionStore(directory.Root), new JsonSettingsStore(directory.File("settings.json")), new TestMailReceiver(), new Sender(), new TestCredentialStore());
        await application.InitializeAsync(); var model = application.CreateViewModel();
        try
        {
            model.Account.EmailAddress = "unfinished@example.test";
            Assert.True(model.Accounts!.CanAdd); Assert.True(model.Accounts.BeginAdd());
            Assert.Equal("unfinished@example.test", model.Account.EmailAddress); Assert.False(model.Accounts.IsAdding);
        }
        finally { model.Inbox.Dispose(); model.Composer.Dispose(); }
    }

    [Fact]
    public async Task ActiveSubmissionRefusesSwitchAndKeepsItsCapturedSender()
    {
        var sender = new HoldingSender(); using var fixture = await Fixture.Create(sender: sender); var model = fixture.Model;
        Assert.True(model.Composer.StartNew()); model.Composer.Edit("to@example.test", "", "", "Subject", "Text");
        var sending = model.Composer.SendAsync(); await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Switch(fixture.Second.Id); Assert.Equal(fixture.First, model.CurrentAccount);
        await model.Accounts!.SetEnabledAsync(false); await model.Accounts.MoveSelectedAsync(1);
        Assert.True(model.CurrentAccount!.IsEnabled); Assert.Equal(fixture.First.Id, (await fixture.Store.LoadStateAsync()).Accounts[0].Id);
        Assert.Equal(fixture.First.Id, (await fixture.Store.LoadStateAsync()).SelectedAccountId);
        sender.Result.SetResult(new(SubmissionStatus.Rejected, "Synthetic rejection"));
        fixture.Dispatcher.DrainUntil(() => !model.Composer.IsBusy); await sending;
        Assert.Equal(fixture.First.Id, sender.Account!.Id);
        await fixture.Switch(fixture.Second.Id); Assert.False(model.Composer.CanSend);
    }

    [Fact]
    public async Task DisabledSelectionReadsItsCacheWithoutEnablingReceiveOrCompose()
    {
        using var fixture = await Fixture.Create(disabled: true); await fixture.Switch(fixture.Second.Id);
        Assert.False(fixture.Model.Inbox.CanReceive); Assert.False(fixture.Model.Composer.CanStart);
        await fixture.Model.Inbox.SelectAsync(new(fixture.Second.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain();
        Assert.Equal("Body B", fixture.Model.Inbox.Body!.PlainText);
    }

    [Fact]
    public async Task StartupAndSwitchKeepOverlappingOfflineUidsAndDraftSenderIndependent()
    {
        using var fixture = await Fixture.Create(); var model = fixture.Model;
        Assert.Equal(fixture.First, model.CurrentAccount);
        Assert.True(model.Composer.StartNew()); model.Composer.Edit("to@example.test", "", "", "Draft A", "Keep this text");
        Assert.True(model.Composer.CanSend);
        await fixture.Switch(fixture.Second.Id);
        Assert.Equal(fixture.Second, model.CurrentAccount); Assert.Equal(fixture.First.EmailAddress, model.Composer.FromAddress);
        Assert.False(model.Composer.CanSend); Assert.Throws<InvalidOperationException>(() => model.Composer.BuildDraft());
        await model.Inbox.CacheLoadTask; await model.Inbox.SelectAsync(new(fixture.Second.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain();
        Assert.Equal("Body B", model.Inbox.Body!.PlainText);
        using var reopened = new MailApplication(fixture.Store, new JsonSettingsStore(fixture.Directory.File("settings.json")), new TestMailReceiver(), new Sender(), new TestCredentialStore());
        await reopened.InitializeAsync(); Assert.Equal(fixture.Second, reopened.LoadedAccount);
        await fixture.Switch(fixture.First.Id); await model.Inbox.CacheLoadTask;
        Assert.True(model.Composer.CanSend); Assert.Equal(fixture.First.Id, model.Composer.BuildDraft().AccountId);
        await model.Inbox.SelectAsync(new(fixture.First.Id, "INBOX", 7, 1)); fixture.Dispatcher.Drain(); Assert.Equal("Body A", model.Inbox.Body!.PlainText);
    }

    [Fact]
    public async Task UnsavedEditsRefuseSwitchAndNewAccountSaveSelectsAtomically()
    {
        using var fixture = await Fixture.Create(); var model = fixture.Model;
        model.Account.DisplayName = "Unsaved edit"; model.Account.NotifyEdited();
        await fixture.Switch(fixture.Second.Id); Assert.Equal(fixture.First, model.CurrentAccount);
        Assert.Equal(fixture.First.Id, (await fixture.Store.LoadStateAsync()).SelectedAccountId);
        model.Account.DisplayName = fixture.First.DisplayName; model.Account.NotifyEdited();
        Assert.True(model.Accounts!.BeginAdd()); Assert.Null(model.CurrentAccount);
        model.Account.DisplayName = "New account"; model.Account.EmailAddress = "new@example.test";
        model.Account.Host = "imap.example.test"; model.Account.UserName = "new@example.test";
        await model.Account.SaveAsync(); fixture.Dispatcher.Drain();
        var profile = Assert.IsType<AccountProfile>(model.CurrentAccount);
        Assert.Equal(3, model.Accounts.Profiles.Count); Assert.False(model.Accounts.IsAdding);
        Assert.Equal(profile.Id, (await fixture.Store.LoadStateAsync()).SelectedAccountId);
        Assert.NotEqual(fixture.First.Id, profile.Id); Assert.NotEqual(fixture.Second.Id, profile.Id);
    }

    [Fact]
    public async Task CancelNewAccountRestoresSavedIdentityWithoutWritingIt()
    {
        using var fixture = await Fixture.Create(); var model = fixture.Model;
        Assert.True(model.Accounts!.BeginAdd()); model.Account.EmailAddress = "unfinished@example.test";
        Assert.True(model.Accounts.CancelAdd()); Assert.Equal(fixture.First, model.CurrentAccount);
        Assert.Equal(2, (await fixture.Store.LoadStateAsync()).Accounts.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingOrDisablingCancelsReceiveAndRejectsItsLateResult(bool disable)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<MailInboxPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestMailReceiver { Inbox = (_, _) => { entered.TrySetResult(); return held.Task; } };
        using var fixture = await Fixture.Create(receiver); var model = fixture.Model;
        var receiving = model.Inbox.ReceiveAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (disable) { await model.Accounts!.SetEnabledAsync(false); fixture.Dispatcher.Drain(); }
        else await fixture.Switch(fixture.Second.Id);
        await model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain();
        held.SetResult(new MailInboxPage([new() { Key = new(fixture.First.Id, "INBOX", 7, 99), Subject = "Late A", Sender = "A" }], null));
        await receiving; fixture.Dispatcher.Drain(); Assert.DoesNotContain(model.Inbox.Messages, message => message.Key.Uid == 99);
        if (disable) Assert.False(model.Inbox.CanReceive);
        else Assert.DoesNotContain(model.Inbox.Messages, message => message.Key.AccountId == fixture.First.Id);
    }

    [Fact]
    public async Task SelectorIsKeyboardReachableAndSwitchReloadsFormWithoutPasswordOrAuthLeak()
    {
        using var fixture = await Fixture.Create(unsupported: true); var model = fixture.Model;
        using var shell = new MailShellView(model); using var session = new StandardUiSessionBuilder().WithDispatcher(fixture.Dispatcher).Build(new Host()); shell.Attach(session); session.RenderFrame();
        var selector = Descendants(shell.AccountSelectionContent!).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == "Mail account");
        session.SetFocus(shell.NavigationFocus); shell.CreateKeyboardNavigation(session).MoveFocus(1); Assert.Same(selector, session.FocusedElement);
        shell.ShowView("account"); var account = shell.GetContent("account");
        Assert.False(selector.IsEnabled);
        StandardEdit Field(string text) => (StandardEdit)Descendants(account).OfType<StandardLabel>().Single(label => label.Text == text).Target!;
        Field("Password / app password").Text = "unsaved secret";
        shell.CloseDialog(); await fixture.Switch(fixture.Second.Id); session.RenderFrame();
        Assert.True(selector.IsEnabled);
        Assert.Equal(fixture.Second.EmailAddress, Field("Email address").Text);
        Assert.Equal(fixture.Second.IncomingServer.UserName, Field("Username").Text);
        Assert.Equal("", Field("Password / app password").Text);
        Assert.False(model.Account.HasUnsavedChanges);
        Field("Display name").Text = "Renamed work";
        await model.Account.SaveAsync(); fixture.Dispatcher.Drain();
        Assert.Equal(AuthenticationMethod.OAuth2, model.Account.Profile!.IncomingServer.Authentication);
        Assert.Equal(AuthenticationMethod.OAuth2, model.Account.Profile.OutgoingServer!.Authentication);
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    { foreach (var child in element.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }

    private sealed class Fixture : IDisposable
    {
        public TestDirectory Directory = new(); public TestQueueDispatcher Dispatcher = new();
        public AccountProfile First = Profile("Personal", "a@example.test"), Second = Profile("Work", "b@example.test");
        public JsonAccountCollectionStore Store = null!; public MailApplication Application = null!; public MailShellViewModel Model = null!;
        private MailProfile? _profile;
        public static async Task<Fixture> Create(TestMailReceiver? receiver = null, bool unsupported = false, bool disabled = false, IMailSender? sender = null, bool removal = false)
        {
            var fixture = new Fixture(); if (removal) fixture._profile = MailProfile.Open(fixture.Directory.Root);
            fixture.Store = new(fixture.Directory.Root);
            if (unsupported) fixture.Second = fixture.Second with { IncomingServer = fixture.Second.IncomingServer with { Authentication = AuthenticationMethod.OAuth2 },
                OutgoingServer = fixture.Second.OutgoingServer! with { Authentication = AuthenticationMethod.OAuth2 } };
            if (disabled) fixture.Second = fixture.Second with { IsEnabled = false };
            await fixture.Store.SaveAsync(fixture.First); await fixture.Store.SaveAsync(fixture.Second);
            var cache = new SqliteMailCache(fixture.Directory.File("mail-cache.db"));
            foreach (var (profile, text) in new[] { (fixture.First, "Body A"), (fixture.Second, "Body B") })
            {
                var mailbox = new MailboxKey(profile.Id, "INBOX", 7);
                await cache.SaveSummariesAsync(mailbox, [SqliteMailCacheTests.Summary(mailbox, 1)], DateTimeOffset.UtcNow);
                await cache.SaveBodyAsync(mailbox, new(new(profile.Id, "INBOX", 7, 1), text));
            }
            var credentials = new TestCredentialStore();
            var storage = fixture._profile is { } profileRoot ? new ProfileStorageService(profileRoot, cache) : null;
            fixture.Application = new(fixture.Store, new JsonSettingsStore(fixture.Directory.File("settings.json")), receiver ?? new TestMailReceiver(), sender ?? new Sender(), credentials,
                drafts: fixture._profile is { } draftProfile ? new JsonDraftStore(draftProfile.DraftsPath) : null, cache: cache, storage: storage,
                accountRemoval: storage is null ? null : new AccountRemovalService(fixture.Store, storage, credentials));
            await fixture.Application.InitializeAsync(); fixture.Model = fixture.Application.CreateViewModel(fixture.Dispatcher);
            await fixture.Model.Inbox.CacheLoadTask; fixture.Dispatcher.Drain(); return fixture;
        }
        public async Task Switch(AccountId id) { await Model.Accounts!.SwitchAsync(id); Dispatcher.Drain(); await Model.Inbox.CacheLoadTask; Dispatcher.Drain(); }
        public void Dispose() { Model.Composer.SaveAsync().GetAwaiter().GetResult(); Model.Composer.Dispose(); Model.Inbox.Dispose(); Model.Inbox.CacheLoadTask.GetAwaiter().GetResult(); Application.Dispose(); _profile?.Dispose(); Directory.Dispose(); }
        private static AccountProfile Profile(string name, string address) => TestDirectory.Profile() with
        { DisplayName = name, EmailAddress = address, IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = address },
            OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = address, Security = TransportSecurity.StartTls } };
    }
    private sealed class Sender : IMailSender
    { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default) => throw new InvalidOperationException("No test sends mail"); }
    private sealed class HoldingSender : IMailSender
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SendResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AccountProfile? Account;
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default)
        { Account = account; Entered.SetResult(); return Result.Task; }
    }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
