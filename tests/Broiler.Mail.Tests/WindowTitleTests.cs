using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class WindowTitleTests
{
    [Theory]
    [InlineData("INBOX", 3, "alex@example.test", "Inbox (3 unread loaded) — alex@example.test — Broiler Mail")]
    [InlineData("INBOX", 0, "alex@example.test", "Inbox — alex@example.test — Broiler Mail")]
    [InlineData("Archive/2026", 0, null, "Archive/2026 — Broiler Mail")]
    [InlineData("Bad\u0007Name", 1, "", "BadName (1 unread loaded) — Broiler Mail")]
    public void CaptionNamesFolderLoadedUnreadAndAccount(string mailbox, int unread, string? account, string expected) =>
        Assert.Equal(expected, MailShellViewModel.FormatWindowTitle(mailbox, unread, account));

    [Theory]
    [InlineData(null, "alex@example.test", "Search in all folders — alex@example.test — Broiler Mail")]
    [InlineData("INBOX", null, "Search in Inbox — Broiler Mail")]
    [InlineData("Bad\u0007Name", "", "Search in BadName — Broiler Mail")]
    public void SearchCaptionNamesTheSearchWithoutACount(string? mailbox, string? account, string expected) =>
        Assert.Equal(expected, MailShellViewModel.FormatSearchWindowTitle(mailbox, account));

    [Fact]
    public async Task AnAllFolderSearchDoesNotLabelItsUnreadResultsWithTheSelectedFolder()
    {
        using var f = await AccountSearchTests.Fixture.Create();
        using var inbox = await f.Model();
        var dispatcher = new ImmediateUiDispatcher();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(f.Directory.File("accounts.json")), new TestCredentialStore(), new InboxSearchTests.NoNetworkReceiver(), dispatcher, f.Account, null),
            new SettingsViewModel(new JsonSettingsStore(f.Directory.File("settings.json")), dispatcher, new(), null), inbox, new ComposerViewModel(dispatcher: dispatcher));
        Assert.Equal("INBOX", inbox.SelectedMailboxId);
        Assert.StartsWith("Inbox (2 unread loaded) — ", model.WindowTitle);

        inbox.SetSearch("needle");
        inbox.SetSearchAllSavedFolders(true);
        await inbox.SearchSavedHeadersAsync();
        Assert.True(inbox.IsAccountSearchActive);
        Assert.Contains(inbox.Messages, message => message.Key.MailboxId != "INBOX" && !message.IsRead);
        Assert.StartsWith("Search in all folders — ", model.WindowTitle);
        Assert.DoesNotContain("unread", model.WindowTitle);

        await inbox.ClearSearchAsync();
        Assert.StartsWith("Inbox (2 unread loaded) — ", model.WindowTitle);
    }
}
