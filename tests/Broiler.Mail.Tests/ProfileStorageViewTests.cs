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
using Broiler.UI.Label.Standard;
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

    [Fact]
    public void ProfileStorageViewHasNoTypedPathFields()
    {
        using var directory = new TestDirectory(); var service = new Storage { Root = directory.File("profile") };
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var content = new ProfileStorageView(model, new FakeMailFilePicker()).CreateContent();
        var fields = Descendants(content).OfType<FormField>().ToArray();
        Assert.Equal(["Cache limit in MiB (1–16384)"], fields.Where(field => field.Control is StandardEdit).Select(field => field.Label.Text));
        string Shown(string name) => Assert.IsType<StandardLabel>(fields.Single(field => field.Label.Text == name).Control).Text;
        Assert.Equal(model.BackupLocation, Shown("Backup location")); Assert.Equal(Path.Combine(service.Root, "backups"), model.BackupLocation);
        Assert.Contains("No backup chosen", Shown("Backup to restore")); Assert.Equal(model.RestoreDestination, Shown("Restored profile folder"));
        foreach (var name in new[] { "Backup location", "Backup to restore", "Restored profile folder" })
        {
            var label = Assert.IsType<StandardLabel>(fields.Single(field => field.Label.Text == name).Control);
            // The path itself is announced, not only the field name the form label already gives.
            Assert.False(label.UseMnemonic); Assert.Equal(name + ": " + label.Text, label.GetSemanticNode().Name);
        }
        var buttons = Descendants(content).OfType<StandardButton>().ToArray();
        Assert.DoesNotContain(buttons, button => button.Text == "Use a new default backup folder");
        foreach (var text in new[] { "Change backup location…", "Choose backup folder…", "Change restore location…" })
            Assert.True(buttons.Single(button => button.Text == text).IsEnabled, text);
        Assert.Equal(UiVisibility.Collapsed, buttons.Single(button => button.Text == "Use default location").Visibility);
        Assert.False(buttons.Single(button => button.Text == "Restore backup").IsEnabled);
        Assert.StartsWith("Broiler Mail backup ", Path.GetFileName(model.BackupDestination));
        Assert.Equal(model.BackupLocation, Path.GetDirectoryName(model.BackupDestination));
    }

    [Fact]
    public void ChangeBackupLocationUsesPickedFolderAndTwoBackupsSucceed()
    {
        using var directory = new TestDirectory(); string elsewhere = directory.File("elsewhere"); Directory.CreateDirectory(elsewhere);
        var service = new Storage { Root = directory.File("profile"), CreateFolders = true };
        var picker = new FakeMailFilePicker().WillChooseFolder(elsewhere);
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var shown = new Shown(new ProfileStorageView(model, picker));
        var change = shown.Button("Change backup location…"); shown.Session.SetFocus(change); change.Click(); shown.Session.RenderFrame();
        var request = Assert.Single(picker.Requests);
        Assert.Equal("directory", request.Kind); Assert.Equal(MailFilePurpose.BackupLocation, request.Request.Purpose); Assert.Equal("Use this folder", request.Request.AcceptText);
        Assert.Equal(elsewhere, model.BackupLocation); Assert.False(model.IsDefaultBackupLocation); Assert.False(model.IsError);
        Assert.Equal(elsewhere, shown.Label("Backup location").Text); Assert.Equal("Backup location: " + elsewhere, shown.Label("Backup location").GetSemanticNode().Name);
        var useDefault = shown.Button("Use default location"); Assert.Equal(UiVisibility.Visible, useDefault.Visibility);
        shown.Button("Create verified backup").Click(); shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError, model.Status); Assert.Equal(2, service.Backups.Count); Assert.NotEqual(service.Backups[0], service.Backups[1]);
        Assert.All(service.Backups, backup =>
        {
            Assert.Equal(elsewhere, Path.GetDirectoryName(backup)); Assert.StartsWith("Broiler Mail backup ", Path.GetFileName(backup)); Assert.True(Directory.Exists(backup));
        });
        Assert.Equal(service.Backups[1], model.RestoreSource); Assert.DoesNotContain(model.BackupDestination, service.Backups);
        shown.Session.SetFocus(useDefault); useDefault.Click(); shown.Session.RenderFrame();
        Assert.True(model.IsDefaultBackupLocation); Assert.Equal(Path.Combine(service.Root, "backups"), Path.GetDirectoryName(model.BackupDestination));
        Assert.Equal(UiVisibility.Collapsed, useDefault.Visibility); Assert.Same(change, shown.Session.FocusedElement);
    }

    [Fact]
    public void CanceledOrRejectedPickKeepsPreviousLocation()
    {
        using var directory = new TestDirectory(); var service = new Storage { Root = directory.File("profile") };
        var picker = new FakeMailFilePicker();
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var shown = new Shown(new ProfileStorageView(model, picker));
        string location = model.BackupLocation, destination = model.BackupDestination, status = model.Status;
        var change = shown.Button("Change backup location…");
        void Pick() { shown.Session.SetFocus(change); change.Click(); shown.Session.RenderFrame(); }
        Pick(); // nothing queued: the person closed the picker
        Assert.Equal(location, model.BackupLocation); Assert.Equal(status, model.Status); Assert.False(model.IsError); Assert.False(model.IsWarning);
        Assert.Same(change, shown.Session.FocusedElement);
        picker.DirectoryResults.Enqueue(MailFilePick<IMailPickedDirectory>.Rejected("That folder cannot be opened."));
        Pick(); Assert.Equal(location, model.BackupLocation); Assert.True(model.IsWarning); Assert.Contains("That folder cannot be opened.", model.Status);
        picker.DirectoryResults.Enqueue(MailFilePick<IMailPickedDirectory>.Picked(new PathlessFolder()));
        Pick(); Assert.Equal(location, model.BackupLocation); Assert.True(model.IsWarning); Assert.Contains("no path on this device", model.Status);
        Assert.Same(change, shown.Session.FocusedElement);
        Assert.False(model.SetBackupLocation(Path.Combine("relative", "backups"))); Assert.True(model.IsError);
        Assert.False(model.SetRestoreLocation("")); Assert.Equal(location, model.BackupLocation); Assert.Equal(destination, model.BackupDestination);
        Assert.Equal(location, shown.Label("Backup location").Text);
    }

    [Fact]
    public void RestoreNeedsChosenBackupWithManifest()
    {
        using var directory = new TestDirectory();
        string notBackup = directory.File("photos"), backup = directory.File("saved backup"), target = directory.File("restored here");
        Directory.CreateDirectory(notBackup); Directory.CreateDirectory(backup); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(backup, "backup.json"), "{}");
        var service = new Storage { Root = directory.File("profile"), CreateFolders = true };
        var picker = new FakeMailFilePicker().WillChooseFolder(notBackup).WillChooseFolder(backup).WillChooseFolder(target);
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var shown = new Shown(new ProfileStorageView(model, picker));
        var restore = shown.Button("Restore backup"); Assert.False(restore.IsEnabled);
        shown.Button("Choose backup folder…").Click(); shown.Session.RenderFrame();
        Assert.True(model.IsError); Assert.Equal(ProfileStorageViewModel.NotABackupMessage, model.Status); Assert.Equal("", model.RestoreSource); Assert.False(restore.IsEnabled);
        shown.Button("Choose backup folder…").Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError); Assert.Equal(backup, model.RestoreSource); Assert.Equal(backup, shown.Label("Backup to restore").Text); Assert.True(restore.IsEnabled);
        Assert.All(picker.Requests.Take(2), request => { Assert.Equal(MailFilePurpose.RestoreBackup, request.Request.Purpose); Assert.Equal("Use this backup", request.Request.AcceptText); });
        Assert.False(model.SetRestoreSource(notBackup)); Assert.Equal(backup, model.RestoreSource);
        shown.Button("Change restore location…").Click(); shown.Session.RenderFrame();
        Assert.Equal(MailFilePurpose.RestoreLocation, picker.Requests[2].Request.Purpose); Assert.Equal("Use this folder", picker.Requests[2].Request.AcceptText);
        Assert.Equal(target, model.RestoreLocation); Assert.Equal(target, Path.GetDirectoryName(model.RestoreDestination));
        Assert.StartsWith("Mail-restored-", Path.GetFileName(model.RestoreDestination)); Assert.Equal(model.RestoreDestination, shown.Label("Restored profile folder").Text);
        string first = model.RestoreDestination;
        restore.Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError, model.Status); Assert.Equal((backup, first), Assert.Single(service.Restores)); Assert.Equal(first, model.RestoredProfile);
        Assert.NotEqual(first, model.RestoreDestination); Assert.Equal(target, Path.GetDirectoryName(model.RestoreDestination));
    }

    [Fact]
    public void UnavailablePickerKeepsDefaultBackup()
    {
        using var directory = new TestDirectory(); var service = new Storage { Root = directory.File("profile"), CreateFolders = true };
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var shown = new Shown(new ProfileStorageView(model));
        // Without a folder picker the typed path fields stay, so nothing is lost on such a device.
        var typed = Descendants(shown.Content).OfType<FormField>().Where(field => field.Control is StandardEdit).ToDictionary(field => field.Label.Text, field => (StandardEdit)field.Control);
        Assert.Equal(model.BackupDestination, typed["New backup folder"].Text); Assert.Equal("", typed["Backup folder"].Text); Assert.Equal(model.RestoreDestination, typed["New profile folder"].Text);
        Assert.DoesNotContain(Descendants(shown.Content).OfType<FormField>(), field => field.Label.Text is "Backup location" or "Backup to restore" or "Restored profile folder");
        foreach (var text in new[] { "Change backup location…", "Choose backup folder…", "Change restore location…" })
        {
            var button = shown.Button(text); Assert.False(button.IsEnabled, text); Assert.Equal(UnavailableMailFilePicker.NotOnThisDeviceYet.Reason, button.ToolTipText);
        }
        Assert.DoesNotContain(Descendants(shown.Content).OfType<StandardButton>(), button => button.Text == "Use default location");
        string first = model.BackupDestination;
        shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError, model.Status); Assert.Equal(first, Assert.Single(service.Backups));
        Assert.Equal(Path.Combine(service.Root, "backups"), Path.GetDirectoryName(first)); Assert.True(Directory.Exists(first));
        Assert.NotEqual(first, typed["New backup folder"].Text); Assert.Equal(model.BackupDestination, typed["New backup folder"].Text);
        shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError, model.Status); Assert.Equal(2, service.Backups.Distinct().Count());
        Assert.Equal(service.Backups[1], model.RestoreSource); Assert.Equal(service.Backups[0], typed["Backup folder"].Text); // never rewritten once filled
        string custom = directory.File("typed-backup"); typed["New backup folder"].Text = custom;
        shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.Equal(custom, service.Backups[2]); Assert.NotEqual(custom, typed["New backup folder"].Text);
        string restoredTo = directory.File("typed-restore"); typed["Backup folder"].Text = custom; typed["New profile folder"].Text = restoredTo;
        shown.Button("Restore backup").Click(); shown.Session.RenderFrame();
        Assert.Equal((custom, restoredTo), Assert.Single(service.Restores)); Assert.Equal(model.RestoreDestination, typed["New profile folder"].Text);
    }

    [Fact]
    public void UnavailablePickerStillGeneratesANewDefaultBackupFolder()
    {
        using var directory = new TestDirectory(); var service = new Storage { Root = directory.File("profile"), CreateFolders = true };
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var shown = new Shown(new ProfileStorageView(model));
        var field = (StandardEdit)Descendants(shown.Content).OfType<FormField>().Single(item => item.Label.Text == "New backup folder").Control;
        // A typed folder that already exists is refused; one click replaces it with a new unique folder.
        string existing = directory.File("already-there"); Directory.CreateDirectory(existing); field.Text = existing;
        shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.True(model.IsError); Assert.Empty(service.Backups); Assert.Equal(existing, field.Text);
        var generate = shown.Button("Use a new default backup folder");
        Assert.Equal(UiVisibility.Visible, generate.Visibility); Assert.True(generate.IsEnabled);
        shown.Session.SetFocus(generate); generate.Click(); shown.Session.RenderFrame();
        Assert.NotEqual(existing, field.Text); Assert.Equal(model.BackupDestination, field.Text);
        Assert.Equal(Path.Combine(service.Root, "backups"), Path.GetDirectoryName(field.Text)); Assert.False(Directory.Exists(field.Text));
        Assert.Same(generate, shown.Session.FocusedElement);
        string generated = field.Text;
        shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError, model.Status); Assert.Equal(generated, Assert.Single(service.Backups));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void PickerFlowsAt200PercentText(double scale)
    {
        using var directory = new TestDirectory();
        string deep = directory.File(Path.Combine("A rather long folder name for backups", "kept on another disk", "Broiler Mail", "every week"));
        Directory.CreateDirectory(deep);
        var service = new Storage { Root = directory.File("profile"), CreateFolders = true };
        var picker = new FakeMailFilePicker().WillChooseFolder(deep);
        using var model = new ProfileStorageViewModel(service, new ImmediateUiDispatcher());
        using var shown = new Shown(new ProfileStorageView(model, picker), scale);
        var change = shown.Button("Change backup location…"); shown.Session.SetFocus(change); change.Click(); shown.Session.RenderFrame();
        Assert.Equal(deep, model.BackupLocation); Assert.Same(change, shown.Session.FocusedElement);
        shown.Button("Create verified backup").Click(); shown.Session.RenderFrame();
        Assert.False(model.IsError, model.Status); Assert.Equal(model.RestoreSource, shown.Label("Backup to restore").Text);
        var scroll = shown.Surface.Content.Scroll;
        UiElement[] flow = [shown.Label("Backup location"), change, shown.Button("Use default location"), shown.Button("Create verified backup"),
            shown.Label("Backup to restore"), shown.Button("Choose backup folder…"), shown.Label("Restored profile folder"), shown.Button("Change restore location…"), shown.Button("Restore backup")];
        foreach (var element in flow)
        {
            element.BringIntoView(); shown.Session.RenderFrame();
            Assert.True(element.Bounds.Width > 0, element.GetSemanticNode().Name);
            Assert.InRange(element.Bounds.Left, scroll.ContentBounds.Left - 1, scroll.ContentBounds.Right);
            Assert.InRange(element.Bounds.Right, scroll.ContentBounds.Left, scroll.ContentBounds.Right + 1);
        }
        Assert.True(shown.Button("Restore backup").IsEnabled);
    }

    [Fact]
    public void UniqueChildSkipsExistingFoldersAndFiles()
    {
        using var directory = new TestDirectory(); directory.Create();
        Assert.Equal(directory.File("x"), ProfileStorageViewModel.UniqueChild(directory.Root, "x"));
        Directory.CreateDirectory(directory.File("x"));
        Assert.Equal(directory.File("x (2)"), ProfileStorageViewModel.UniqueChild(directory.Root, "x"));
        File.WriteAllText(directory.File("x (2)"), "");
        Assert.Equal(directory.File("x (3)"), ProfileStorageViewModel.UniqueChild(directory.Root, "x"));
        for (int number = 3; number <= 99; number++) Directory.CreateDirectory(directory.File($"x ({number})"));
        string unique = ProfileStorageViewModel.UniqueChild(directory.Root, "x");
        Assert.Equal(directory.Root, Path.GetDirectoryName(unique)); Assert.StartsWith("x ", Path.GetFileName(unique));
        Assert.DoesNotContain("(", Path.GetFileName(unique)); Assert.False(Directory.Exists(unique) || File.Exists(unique));
    }

    private sealed class Shown : IDisposable
    {
        public Shown(ProfileStorageView view, double scale = 1)
        {
            Content = view.CreateContent();
            Surface = new FormSurface(Content, FormSurface.ActionBar(new StandardButton { Text = "Close" }), new InlineFeedback());
            Session = new StandardUiSessionBuilder().Build(new Host()); Session.AddRoot(Surface);
            var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(Session, theme); StandardThemeController.ApplyToSubtree(Surface, theme);
            Descendants(Content).OfType<FormSection>().Single(section => section.GetSemanticNode().Name == "Restore as a separate profile").Expand();
            Session.RenderFrame();
        }
        public UiElement Content { get; }
        public FormSurface Surface { get; }
        public UiSession Session { get; }
        public StandardButton Button(string text) => Descendants(Content).OfType<StandardButton>().Single(button => button.Text == text);
        public StandardLabel Label(string field) => Assert.IsType<StandardLabel>(Descendants(Content).OfType<FormField>().Single(item => item.Label.Text == field).Control);
        public void Dispose() { Session.Dispose(); Surface.Dispose(); Content.Dispose(); }
    }
    private sealed class PathlessFolder : IMailPickedDirectory
    {
        public string DisplayName => "Shared documents"; public string? LocalPath => null;
        public Task<MailWrittenFile> WriteNewFileAsync(string fileName, Func<Stream, CancellationToken, Task> write, CancellationToken token = default) => throw new NotSupportedException();
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
        public string Root { get; init; } = Path.Combine(Path.GetTempPath(), "MailStorageViewFixture");
        public string ProfileDirectory => Root;
        /// <summary>Creates each backup folder with a manifest and each restored folder, as the real service does.</summary>
        public bool CreateFolders { get; init; }
        public List<string> Backups { get; } = [];
        public List<(string Source, string Destination)> Restores { get; } = [];
        public ProfileStoragePolicy Policy { get; private set; } = new(268435456, OfflineDownloadPolicy.OnDemand);
        public Task<ProfileStoragePolicy> SetPolicyAsync(ProfileStoragePolicy policy, CancellationToken cancellationToken = default)
        { Policy = policy; return Task.FromResult(policy); }
        public int ClearCount { get; private set; } public int BackupCount { get; private set; }
        public bool WaitForCancel { get; init; }
        public Task<ProfileStorageInfo> GetInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProfileStorageInfo(4096, Policy.MaximumBytes, 2, 1, 0));
        public async Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default)
        {
            BackupCount++; if (WaitForCancel) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (CreateFolders)
            {
                if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory)) throw new IOException("Choose a new, unused destination folder.");
                Directory.CreateDirectory(destinationDirectory); File.WriteAllText(Path.Combine(destinationDirectory, "backup.json"), "{}");
            }
            Backups.Add(destinationDirectory); return destinationDirectory;
        }
        public Task<string> RestoreBackupAsync(string backupDirectory, string newProfileDirectory, CancellationToken cancellationToken = default)
        {
            if (CreateFolders) { if (Directory.Exists(newProfileDirectory)) throw new IOException("Choose a new, unused destination folder."); Directory.CreateDirectory(newProfileDirectory); }
            Restores.Add((backupDirectory, newProfileDirectory)); return Task.FromResult(newProfileDirectory);
        }
        public Task<ProfileCleanupResult> ClearDownloadedMailAsync(string recoveryDirectory, CancellationToken cancellationToken = default)
        { ClearCount++; return Task.FromResult(new ProfileCleanupResult(recoveryDirectory, true)); }
    }
}
