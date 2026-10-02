using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

/// <summary>UI-07: window geometry and the inbox split are remembered quietly and compatibly.</summary>
public sealed class WindowLayoutPersistenceTests
{
    private static readonly WindowPlacement Placement = new()
    {
        Left = -1500, Top = 80, Width = 1200, Height = 820, ClientWidth = 1180, ClientHeight = 760, Maximized = true,
    };

    [Fact]
    public async Task RememberedLayoutRoundTripsWithoutTouchingStatusOrBusyState()
    {
        using var directory = new TestDirectory();
        var store = new JsonSettingsStore(directory.File("settings.json"));
        var dispatcher = new TestQueueDispatcher();
        var settings = new SettingsViewModel(store, dispatcher, new ApplicationSettings { Theme = AppTheme.Dark }, null);
        int changes = 0;
        settings.Changed += (_, _) => changes++;

        await settings.RememberLayoutAsync(Placement, 0.42);

        Assert.Equal(0, changes);
        Assert.False(settings.IsBusy);
        Assert.Equal("", settings.Status);
        Assert.Null(settings.LayoutSaveError);
        var reloaded = await new JsonSettingsStore(directory.File("settings.json")).LoadAsync();
        Assert.Equal(Placement, reloaded.Window);
        Assert.Equal(0.42, reloaded.InboxSplitterFraction, 3);
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
    }

    [Fact]
    public async Task ExplicitSaveKeepsTheRememberedLayoutUnlessTheSizeChanges()
    {
        using var directory = new TestDirectory();
        var store = new JsonSettingsStore(directory.File("settings.json"));
        var dispatcher = new TestQueueDispatcher();
        var settings = new SettingsViewModel(store, dispatcher, new ApplicationSettings(), null);
        await settings.RememberLayoutAsync(Placement, 0.3);

        settings.Theme = AppTheme.Light;
        await settings.SaveAsync();
        dispatcher.DrainUntil(() => !settings.IsBusy);
        Assert.Equal(Placement, (await store.LoadAsync()).Window);

        // A newly entered initial size wins at the next start.
        settings.WindowWidth = "900";
        await settings.SaveAsync();
        dispatcher.DrainUntil(() => !settings.IsBusy);
        var saved = await store.LoadAsync();
        Assert.Null(saved.Window);
        Assert.Equal(900, saved.WindowWidth);
        Assert.Equal(0.3, saved.InboxSplitterFraction, 3);
    }

    [Fact]
    public async Task FilesWithoutTheRememberedLayoutStillLoad()
    {
        using var directory = new TestDirectory();
        var path = directory.File("settings.json");
        await new JsonSettingsStore(path).SaveAsync(new ApplicationSettings { WindowWidth = 1000, InboxSplitterFraction = 0.5 });
        // Simulate a file written before the property existed.
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var data = document.Single(pair => pair.Value is JsonObject).Value!.AsObject();
        foreach (var key in data.Select(pair => pair.Key).Where(key => key.Equals("Window", StringComparison.OrdinalIgnoreCase)).ToArray())
            data.Remove(key);
        await File.WriteAllTextAsync(path, document.ToJsonString());

        var loaded = await new JsonSettingsStore(path).LoadAsync();
        Assert.Null(loaded.Window);
        Assert.Equal(1000, loaded.WindowWidth);
        Assert.Equal(0.5, loaded.InboxSplitterFraction, 3);
    }

    [Fact]
    public async Task UnreadableSettingsAndInvalidLayoutsAreNeverWritten()
    {
        using var directory = new TestDirectory();
        var path = directory.File("settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "broken");
        var unreadable = new SettingsViewModel(new JsonSettingsStore(path), new TestQueueDispatcher(), new ApplicationSettings(), "Settings could not be loaded.");
        await unreadable.RememberLayoutAsync(Placement, 0.4);
        Assert.Equal("broken", await File.ReadAllTextAsync(path));

        var other = directory.File("other.json");
        var settings = new SettingsViewModel(new JsonSettingsStore(other), new TestQueueDispatcher(), new ApplicationSettings(), null);
        await settings.RememberLayoutAsync(Placement with { ClientWidth = 100 }, 0.4);
        Assert.NotNull(settings.LayoutSaveError);
        Assert.False(File.Exists(other));
    }

    [Fact]
    public void RememberedSplitAppliesToTheInbox()
    {
        using var directory = new TestDirectory();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new ApplicationSettings { InboxSplitterFraction = 0.6 }, null),
            new(receiver, dispatcher));
        Assert.Equal(0.6, model.Inbox.SplitterFraction, 3);
    }
}
