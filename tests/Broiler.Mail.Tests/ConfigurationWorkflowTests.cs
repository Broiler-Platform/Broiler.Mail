using Broiler.Mail.Application;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class ConfigurationWorkflowTests
{
    [Fact]
    public void SaveResultIsPublishedThroughTheDispatcher()
    {
        var dispatcher = new StandardQueuedUiDispatcher();
        var store = new MemoryAccountStore();
        var model = NewModel(store, dispatcher);
        // The memory store finishes at once, so the result is posted from the UI thread itself.
        // It still waits for the next drain rather than running inside the save.
        Assert.True(model.SaveAsync().IsCompletedSuccessfully);
        Assert.True(model.IsBusy);
        Assert.Null(model.Profile);
        Assert.Equal(1, dispatcher.Drain());
        Assert.False(model.IsBusy);
        Assert.Equal(store.Saved, model.Profile);
        Assert.Contains("saved", model.Status);
    }

    [Fact]
    public async Task FailedSaveDoesNotClaimSuccessAndCanBeRetried()
    {
        var store = new MemoryAccountStore { Fail = true };
        var model = NewModel(store, new ImmediateUiDispatcher());
        await model.SaveAsync();
        Assert.Null(model.Profile);
        Assert.StartsWith("Not saved:", model.Status);
        Assert.True(model.CanSave);
        store.Fail = false;
        await model.SaveAsync();
        var id = model.Profile!.Id;
        model.DisplayName = "Changed";
        await model.SaveAsync();
        Assert.Equal(id, model.Profile!.Id);
        Assert.Equal("Changed", model.Profile.DisplayName);
    }

    [Theory]
    [InlineData("https://imap.example.test")]
    [InlineData("imap.example.test:993")]
    [InlineData("")]
    public async Task InvalidHostDoesNotReachPersistence(string host)
    {
        var store = new MemoryAccountStore();
        var model = NewModel(store, new ImmediateUiDispatcher());
        model.Host = host;
        await model.SaveAsync();
        Assert.Null(store.Saved);
        Assert.StartsWith("Not saved:", model.Status);
    }

    [Fact]
    public void AccountFormSavesTheEditedFields()
    {
        var store = new MemoryAccountStore();
        var model = NewModel(store, new ImmediateUiDispatcher());
        using var form = new AccountProfileView(model).CreateContent();
        var labels = Descendants(form).OfType<StandardLabel>().Where(label => label.Target is StandardEdit).ToDictionary(label => label.Text);
        ((StandardEdit)labels["Display name"].Target!).Text = "My account";
        ((StandardEdit)labels["Email address"].Target!).Text = "reader@example.test";
        ((StandardEdit)labels["IMAP server (hostname only)"].Target!).Text = "imap.example.test";
        ((StandardEdit)labels["IMAP port"].Target!).Text = "993";
        ((StandardEdit)labels["Username"].Target!).Text = "reader";
        Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Save account").Click();
        Assert.NotNull(store.Saved);
        Assert.Equal("My account", store.Saved.DisplayName);
        Assert.Equal("reader", store.Saved.IncomingServer.UserName);
    }

    [Fact]
    public async Task SettingsValidationRetainsTheLastSavedSettings()
    {
        using var directory = new TestDirectory();
        var store = new JsonSettingsStore(directory.File("settings.json"));
        var model = new SettingsViewModel(store, new ImmediateUiDispatcher(), new(), null) { WindowWidth = "1" };
        await model.SaveAsync();
        Assert.StartsWith("Not saved:", model.Status);
        Assert.Equal(1100, model.Settings.WindowWidth);
        Assert.False(Directory.Exists(directory.Root));
        model.WindowWidth = "1280";
        model.Theme = AppTheme.Dark;
        await model.SaveAsync();
        Assert.Equal(1280, (await store.LoadAsync()).WindowWidth);
        Assert.Equal(AppTheme.Dark, model.Settings.Theme);
    }

    [Fact]
    public async Task RestartLoadsSavedProfileAndSettings()
    {
        using var directory = new TestDirectory();
        var accounts = new JsonAccountStore(directory.File("accounts.json"));
        var settings = new JsonSettingsStore(directory.File("settings.json"));
        var profile = TestDirectory.Profile();
        await accounts.SaveAsync(profile);
        await settings.SaveAsync(new() { Theme = AppTheme.Dark, WindowWidth = 1200 });
        var application = CreateApplication(directory);
        await application.InitializeAsync();
        Assert.Equal(profile, application.LoadedAccount);
        Assert.Equal(1200, application.LoadedSettings.WindowWidth);
        Assert.Equal(AppTheme.Dark, application.LoadedSettings.Theme);
        Assert.Null(application.AccountLoadError);
    }

    [Fact]
    public async Task CorruptAccountDataDoesNotPreventSettingsLoadingAndBlocksAccountSave()
    {
        using var directory = new TestDirectory();
        directory.Create();
        await File.WriteAllTextAsync(directory.File("accounts.json"), "broken");
        await new JsonSettingsStore(directory.File("settings.json")).SaveAsync(new() { WindowWidth = 1200 });
        var application = CreateApplication(directory);
        await application.InitializeAsync();
        Assert.NotNull(application.AccountLoadError);
        Assert.Equal(1200, application.LoadedSettings.WindowWidth);
        var store = new MemoryAccountStore();
        var credentials = new TestCredentialStore();
        var model = new AccountProfileViewModel(store, credentials, new TestMailReceiver(), new ImmediateUiDispatcher(), null, application.AccountLoadError);
        await model.SaveAsync();
        Assert.False(model.CanSave);
        Assert.Null(store.Saved);
        Assert.Equal("broken", await File.ReadAllTextAsync(directory.File("accounts.json")));
    }

    [Fact]
    public async Task CorruptionAfterStartupIsDisplayedAsASaveFailure()
    {
        using var directory = new TestDirectory();
        var model = NewModel(new JsonAccountStore(directory.File("accounts.json")), new ImmediateUiDispatcher());
        directory.Create();
        await File.WriteAllTextAsync(directory.File("accounts.json"), "broken");
        await model.SaveAsync();
        Assert.StartsWith("Not saved:", model.Status);
        Assert.True(model.CanSave);
        Assert.Null(model.Profile);
        Assert.Equal("broken", await File.ReadAllTextAsync(directory.File("accounts.json")));
    }

    [Fact]
    public async Task CorruptSettingsUseStartupDefaultsWithoutResettingTheFile()
    {
        using var directory = new TestDirectory();
        directory.Create();
        await File.WriteAllTextAsync(directory.File("settings.json"), "broken");
        var application = CreateApplication(directory);
        await application.InitializeAsync();
        Assert.NotNull(application.SettingsLoadError);
        Assert.Equal(new ApplicationSettings(), application.LoadedSettings);
        Assert.Equal("broken", await File.ReadAllTextAsync(directory.File("settings.json")));
    }

    private static AccountProfileViewModel NewModel(IAccountStore store, IUiDispatcher dispatcher) => new(store, new TestCredentialStore(), new TestMailReceiver(), dispatcher, null, null)
    {
        DisplayName = "Test", EmailAddress = "test@example.test", Host = "imap.example.test", Port = "993", UserName = "test",
    };

    private static MailApplication CreateApplication(TestDirectory directory)
    {
        var credentials = new TestCredentialStore();
        return new(new JsonAccountStore(directory.File("accounts.json")), new JsonSettingsStore(directory.File("settings.json")),
            new ImapMailReceiver(credentials), new SmtpMailSender(credentials), credentials);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var element in Descendants(child))
                yield return element;
    }

    private sealed class MemoryAccountStore : IAccountStore
    {
        public bool Fail { get; set; }
        public AccountProfile? Saved { get; private set; }
        public Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountProfile>>(Saved is null ? [] : [Saved]);
        public Task SaveAsync(AccountProfile profile, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Simulated disk failure.");
            Saved = profile;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(AccountId accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

}
