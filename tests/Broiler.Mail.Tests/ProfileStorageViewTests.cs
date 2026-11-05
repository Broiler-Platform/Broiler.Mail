using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class ProfileStorageViewTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task UncertainDismissalFocusesKeepAndRequiresSeparateConfirmation(double scale)
    {
        using var directory = new TestDirectory(); using var profile = Broiler.Mail.Infrastructure.Persistence.MailProfile.Open(directory.File("profile"));
        var cache = new Broiler.Mail.Infrastructure.Persistence.SqliteMailCache(profile.CachePath);
        var key = new Broiler.Mail.Core.Messages.MailMessageKey(Broiler.Mail.Core.Accounts.AccountId.New(), "INBOX", 7, 1);
        var journal = new Broiler.Mail.Infrastructure.Persistence.MutationIntentJournal(profile.CachePath); var id = await journal.BeginAsync(key, null);
        var service = new Broiler.Mail.Infrastructure.Persistence.ProfileStorageService(profile, cache); var dispatcher = new TestQueueDispatcher();
        using var model = new ProfileStorageViewModel(service, dispatcher); await model.ReviewUncertainActionsAsync(); dispatcher.Drain(); model.SelectUncertainAction(id);
        using var content = new ProfileStorageView(model).CreateContent(); using var surface = new FormSurface(content, FormSurface.ActionBar(new StandardButton { Text = "Close" }), new InlineFeedback());
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(surface);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(surface, theme);
        Descendants(content).OfType<FormSection>().Single(section => section.GetSemanticNode().Name == "Uncertain server actions").Expand(); session.RenderFrame();
        StandardButton Button(string text) => Descendants(content).OfType<StandardButton>().Single(button => button.Text == text);
        Button("Dismiss checked record…").Click(); session.RenderFrame(); var keep = Button("Keep unresolved record");
        Assert.Same(keep, session.FocusedElement); var confirm = Button("I checked the server — back up and dismiss record"); Assert.False(confirm.IsDefault);
        keep.Click(); Assert.Null(model.PendingUncertainDismissal); Assert.Single(await journal.ReadAsync());
        Button("Dismiss checked record…").Click(); confirm.Click(); Assert.True(model.IsBusy); Assert.Equal("Cancel storage operation", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame(); Assert.Empty(await journal.ReadAsync()); Assert.True(session.FocusedElement?.CanFocus);
    }

    [Fact]
    public void ClearRequiresExplicitConfirmationAndReturnsFocusWhenConfirmationCloses()
    {
        var service = new Storage();
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var content = new ProfileStorageView(model).CreateContent();
        using var surface = new FormSurface(content, FormSurface.ActionBar(new StandardButton { Text = "Close" }), new InlineFeedback());
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(surface); session.RenderFrame();
        var buttons = Descendants(content).OfType<StandardButton>().ToArray();
        var clear = buttons.Single(button => button.Text == "Clear downloaded mail…");
        var confirm = buttons.Single(button => button.Text == "Back up and clear downloaded mail");
        var keep = buttons.Single(button => button.Text == "Keep downloaded mail");
        Assert.False(confirm.CanFocus); clear.Click(); session.RenderFrame();
        Assert.True(model.ConfirmClear); Assert.Equal(0, service.ClearCount);
        session.SetFocus(keep); keep.Click(); session.RenderFrame();
        Assert.False(model.ConfirmClear); Assert.Same(clear, session.FocusedElement); Assert.Equal(0, service.ClearCount);
        clear.Click(); confirm.Click(); session.RenderFrame();
        Assert.False(model.ConfirmClear); Assert.Equal(1, service.ClearCount); Assert.Contains("verified recovery backup", model.Status);
    }

    [Fact]
    public async Task DraftFlushFailureStopsSnapshotAndReleasesTheComposeMaintenanceState()
    {
        var service = new Storage(); using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        model.SaveDraftBeforeSnapshot = () => Task.FromResult(false);
        await model.BackupAsync();
        Assert.True(model.IsError); Assert.False(model.IsBusy); Assert.False(model.IsSnapshotBusy); Assert.Equal(0, service.BackupCount);
        Assert.Contains("draft could not be saved", model.Status);
        model.CanStartSnapshot = () => false; await model.BackupAsync();
        Assert.False(model.IsError); Assert.Equal(0, service.BackupCount); Assert.Contains("compose operation", model.Status);
    }

    [Fact]
    public async Task CancelledBackupReturnsToIdleWithoutAnErrorOrCleanup()
    {
        var service = new Storage { WaitForCancel = true };
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var content = new ProfileStorageView(model).CreateContent();
        using var surface = new FormSurface(content, FormSurface.ActionBar(new StandardButton { Text = "Close" }), new InlineFeedback());
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(surface); session.RenderFrame();
        var save = Descendants(content).OfType<StandardButton>().Single(button => button.Text == "Create verified backup");
        var cancel = Descendants(content).OfType<StandardButton>().Single(button => button.Text == "Cancel storage operation");
        session.SetFocus(save);
        var backingUp = model.BackupAsync();
        session.RenderFrame(); Assert.Same(cancel, session.FocusedElement);
        var scroll = surface.Content.Scroll;
        Assert.InRange(cancel.Bounds.Bottom, scroll.ContentBounds.Top, scroll.ContentBounds.Bottom + 1);
        Assert.True(model.IsSnapshotBusy); cancel.Click(); await backingUp;
        session.RenderFrame(); Assert.Equal("Refresh storage usage", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        Assert.False(model.IsBusy); Assert.False(model.IsSnapshotBusy); Assert.False(model.IsError);
        Assert.Equal(0, service.ClearCount); Assert.Contains("canceled", model.Status);
    }

    [Fact]
    public void PolicyControlsApplyNamedModeAndQuotaAndRefuseInvalidEdits()
    {
        var service = new Storage(); using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var content = new ProfileStorageView(model).CreateContent();
        var mode = Descendants(content).OfType<StandardComboBox>().Single();
        var quotaField = Descendants(content).OfType<FormField>().Single(field => field.Label.Text.StartsWith("Cache limit"));
        var quota = Assert.IsType<StandardEdit>(quotaField.Control);
        var apply = Descendants(content).OfType<StandardButton>().Single(button => button.Text == "Apply storage policy");
        mode.SelectedIndex = (int)OfflineDownloadPolicy.HeadersOnly; quota.Text = "32"; apply.Click();
        Assert.Equal(OfflineDownloadPolicy.HeadersOnly, service.Policy.Downloads); Assert.Equal(32 * 1024 * 1024, service.Policy.MaximumBytes);
        Assert.False(model.IsError);
        quota.Text = "0"; apply.Click();
        Assert.True(model.IsError); Assert.Equal(32 * 1024 * 1024, service.Policy.MaximumBytes);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var nested in Descendants(child)) yield return nested; }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
    private sealed class Storage : IProfileStorage
    {
        public string ProfileDirectory => Path.Combine(Path.GetTempPath(), "MailStorageViewFixture");
        public ProfileStoragePolicy Policy { get; private set; } = new(268435456, OfflineDownloadPolicy.OnDemand);
        public Task<ProfileStoragePolicy> SetPolicyAsync(ProfileStoragePolicy policy, CancellationToken cancellationToken = default)
        { Policy = policy; return Task.FromResult(policy); }
        public int ClearCount { get; private set; } public int BackupCount { get; private set; }
        public bool WaitForCancel { get; init; }
        public Task<ProfileStorageInfo> GetInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProfileStorageInfo(4096, Policy.MaximumBytes, 2, 1, 0));
        public async Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default)
        { BackupCount++; if (WaitForCancel) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return destinationDirectory; }
        public Task<string> RestoreBackupAsync(string backupDirectory, string newProfileDirectory, CancellationToken cancellationToken = default) => Task.FromResult(newProfileDirectory);
        public Task<ProfileCleanupResult> ClearDownloadedMailAsync(string recoveryDirectory, CancellationToken cancellationToken = default)
        { ClearCount++; return Task.FromResult(new ProfileCleanupResult(recoveryDirectory, true)); }
    }
}
