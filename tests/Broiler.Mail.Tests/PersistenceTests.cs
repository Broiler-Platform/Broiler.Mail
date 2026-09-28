using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task MissingConfigurationReturnsDefaultsWithoutCreatingFiles()
    {
        using var directory = new TestDirectory();
        Assert.Empty(await new JsonAccountStore(directory.File("accounts.json")).LoadAsync());
        Assert.Equal(new ApplicationSettings(), await new JsonSettingsStore(directory.File("settings.json")).LoadAsync());
        Assert.False(Directory.Exists(directory.Root));
    }

    [Fact]
    public async Task AccountRoundTripsAndEditsKeepTheSameIdentity()
    {
        using var directory = new TestDirectory();
        string path = directory.File("accounts.json");
        var original = TestDirectory.Profile();
        await new JsonAccountStore(path).SaveAsync(original);
        Assert.Equal(original, Assert.Single(await new JsonAccountStore(path).LoadAsync()));

        var edited = original with { DisplayName = "Renamed account", IncomingServer = original.IncomingServer with { Port = 143, Security = TransportSecurity.StartTls } };
        await new JsonAccountStore(path).SaveAsync(edited);
        Assert.Equal(edited, Assert.Single(await new JsonAccountStore(path).LoadAsync()));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.tmp"));
    }

    [Fact]
    public async Task SettingsRoundTripAcrossInstances()
    {
        using var directory = new TestDirectory();
        string path = directory.File("settings.json");
        var expected = new ApplicationSettings { Theme = AppTheme.Dark, WindowWidth = 1280, WindowHeight = 800 };
        await new JsonSettingsStore(path).SaveAsync(expected);
        Assert.Equal(expected, await new JsonSettingsStore(path).LoadAsync());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":2,\"data\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"data\":null}")]
    [InlineData("{\"schemaVersion\":1,\"data\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"data\":[],\"unknown\":true}")]
    public async Task InvalidAccountFilesAreReportedAndNeverOverwritten(string contents)
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("accounts.json");
        await System.IO.File.WriteAllTextAsync(path, contents);
        var store = new JsonAccountStore(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(TestDirectory.Profile()));
        Assert.Equal(contents, await System.IO.File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"data\":{\"windowWidth\":10}}")]
    [InlineData("{\"schemaVersion\":1,\"data\":{\"theme\":\"Unknown\"}}")]
    [InlineData("{\"schemaVersion\":9,\"data\":{}}")]
    public async Task InvalidSettingsCannotBeResetBySavingDefaults(string contents)
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("settings.json");
        await System.IO.File.WriteAllTextAsync(path, contents);
        var store = new JsonSettingsStore(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new()));
        Assert.Equal(contents, await System.IO.File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task InvalidProfileCannotReplaceAnExistingAccount()
    {
        using var directory = new TestDirectory();
        string path = directory.File("accounts.json");
        var store = new JsonAccountStore(path);
        var profile = TestDirectory.Profile();
        await store.SaveAsync(profile);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile with { IncomingServer = profile.IncomingServer with { Port = 70000 } }));
        Assert.Equal(profile, Assert.Single(await store.LoadAsync()));
    }

    [Fact]
    public async Task CompetingNewAccountsCannotOverwriteOneAnother()
    {
        using var directory = new TestDirectory();
        string path = directory.File("accounts.json");
        var first = TestDirectory.Profile();
        var second = TestDirectory.Profile();
        var outcomes = await Task.WhenAll(
            Record.ExceptionAsync(() => new JsonAccountStore(path).SaveAsync(first)),
            Record.ExceptionAsync(() => new JsonAccountStore(path).SaveAsync(second)));
        Assert.Single(outcomes, result => result is null);
        Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, result => result is not null));
        var saved = Assert.Single(await new JsonAccountStore(path).LoadAsync());
        Assert.Contains(saved.Id, new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task CancellationWhileWaitingForLockPreservesPreviousFile()
    {
        using var directory = new TestDirectory();
        string path = directory.File("settings.json");
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(new());
        string previous = await System.IO.File.ReadAllTextAsync(path);
        using (var lease = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = store.SaveAsync(new() { Theme = AppTheme.Dark }, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.Equal(previous, await System.IO.File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.tmp"));
        await store.SaveAsync(new() { Theme = AppTheme.Dark });
        Assert.Equal(AppTheme.Dark, (await store.LoadAsync()).Theme);
    }

    [Fact]
    public async Task RemovingAnUnknownAccountDoesNotRemoveSavedProfile()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile();
        await store.SaveAsync(profile);
        await store.RemoveAsync(AccountId.New());
        Assert.Equal(profile, Assert.Single(await store.LoadAsync()));
        await store.RemoveAsync(profile.Id);
        Assert.Empty(await store.LoadAsync());
    }
}
