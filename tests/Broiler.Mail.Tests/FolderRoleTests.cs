using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class FolderRoleTests
{
    [Fact]
    public void ServerRolesUseAttributesWithoutGuessingNamesOrAllMail()
    {
        MailFolder[] folders = [new("Sent", "/", true, []), new("All mail", "/", true, ["\\All"]),
            new("Envoyes", "/", true, ["\\sEnT"]), new("Brouillons", "/", true, ["\\Drafts"]),
            new("Old", "/", true, ["\\Archive"]), new("Bin", "/", true, ["\\Trash"]), new("Spam", "/", true, ["\\Junk"])];
        var roles = MailFolderRoles.Resolve(new(), folders);
        Assert.Equal(new[] { "Envoyes", "Brouillons", "Old", "Bin", "Spam" }, roles.Select(role => role.MailboxId));
        Assert.All(roles, role => { Assert.True(role.IsConfirmed); Assert.False(role.IsOverride); Assert.Null(role.Problem); });
    }

    [Fact]
    public void AmbiguousAndNonselectableServerRolesRemainUnassigned()
    {
        var roles = MailFolderRoles.Resolve(new(), [new("A", "/", true, ["\\Trash"]), new("B", "/", true, ["\\Trash"]),
            new("Container", "/", false, ["\\Sent"])]);
        Assert.Contains("ambiguous", roles.Single(role => role.Role == MailFolderRole.Trash).Problem);
        Assert.Null(roles.Single(role => role.Role == MailFolderRole.Trash).MailboxId);
        Assert.Null(roles.Single(role => role.Role == MailFolderRole.Sent).MailboxId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOverrideNeverFallsBackToAutomaticRole(bool found)
    {
        MailFolder[] folders = found ? [new("Manual", null, false, []), new("Auto", null, true, ["\\Archive"])]
            : [new("Auto", null, true, ["\\Archive"])];
        var mapping = MailFolderRoles.Resolve(new() { Archive = "Manual" }, folders).Single(role => role.Role == MailFolderRole.Archive);
        Assert.Equal("Manual", mapping.MailboxId); Assert.True(mapping.IsOverride); Assert.False(mapping.IsConfirmed);
        Assert.Equal(found ? "not selectable" : "not found on server", mapping.Problem);
    }

    [Fact]
    public void OverridesPreserveExactNamesAndOnlyCanonicalizeInbox()
    {
        var configured = new AccountFolderOverrides { Sent = "inbox", Drafts = "Drafts", Archive = " archive " };
        var roles = MailFolderRoles.Resolve(configured, [new("INBOX", null, true, []), new("drafts", null, true, []), new(" archive ", null, true, [])]);
        Assert.Equal("INBOX", roles[0].MailboxId); Assert.True(roles[0].IsConfirmed);
        Assert.False(roles[1].IsConfirmed); Assert.Equal("Drafts", roles[1].MailboxId);
        Assert.Equal(" archive ", roles[2].MailboxId); Assert.True(roles[2].IsConfirmed);
        Assert.All(MailFolderRoles.Resolve(configured, null).Where(role => role.IsOverride), role => Assert.False(role.IsConfirmed));
    }

    [Fact]
    public async Task OverrideFieldsPersistWithoutSmtpAndReloadIntoEditor()
    {
        using var directory = new TestDirectory(); var profile = TestDirectory.Profile();
        var store = new JsonAccountStore(directory.File("accounts.json")); await store.SaveAsync(profile);
        var model = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);
        using var form = new AccountProfileView(model).CreateContent();
        var fields = Descendants(form).OfType<StandardLabel>().Where(label => label.Target is not null).ToDictionary(label => label.Text, label => label.Target!);
        foreach (string role in new[] { "Sent", "Drafts", "Archive", "Trash", "Junk" })
            Assert.IsType<StandardEdit>(fields[role + " role override (exact mailbox name)"]).Text = role + "/\u53f0\u5317&";
        Assert.True(model.HasUnsavedChanges);
        await model.SaveAsync();
        var saved = Assert.Single(await store.LoadAsync());
        Assert.Equal("Archive/\u53f0\u5317&", saved.FolderOverrides.Archive); Assert.Equal("Sent/\u53f0\u5317&", saved.FolderOverrides.Sent);
        Assert.Equal(SentCopyMode.NotConfigured, saved.SentCopyMode); Assert.Null(saved.SentFolder); Assert.Null(saved.OutgoingServer);
        Assert.Equal(CredentialKey.For(profile, MailProtocol.Imap), CredentialKey.For(saved, MailProtocol.Imap));
        var collection = new JsonAccountCollectionStore(directory.Root); await collection.SaveAsync(saved);
        Assert.Equal(saved, Assert.Single((await new JsonAccountCollectionStore(directory.Root).LoadStateAsync()).Accounts));
        var reopened = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), saved, null);
        Assert.Equal(saved.FolderOverrides.Archive, reopened.FolderArchive);
        var fresh = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), null, null);
        Assert.Equal("", fresh.FolderArchive);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Bad\r\nName")]
    [InlineData("Bad\0Name")]
    public void InvalidOverridesAreRejected(string name)
    {
        var error = Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(TestDirectory.Profile() with { FolderOverrides = new() { Trash = name } }));
        Assert.Equal("FolderOverrides.Trash", error.Field);
    }

    [Fact]
    public void InvalidUnicodeAndOversizedOverridesAreRejected()
    {
        foreach (string value in new[] { new string((char)0xd800, 1), new string('x', 1025) })
            Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(TestDirectory.Profile() with { FolderOverrides = new() { Junk = value } }));
    }

    [Fact]
    public async Task LegacyAccountWithoutOverridesLoadsAutomaticDefaults()
    {
        using var directory = new TestDirectory(); var store = new JsonAccountStore(directory.File("accounts.json"));
        await store.SaveAsync(TestDirectory.Profile());
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(directory.File("accounts.json")))!;
        json["data"]![0]!.AsObject().Remove("folderOverrides");
        File.WriteAllText(directory.File("accounts.json"), json.ToJsonString());
        Assert.Equal(new AccountFolderOverrides(), Assert.Single(await store.LoadAsync()).FolderOverrides);
    }

    [Fact]
    public async Task ReaderShowsOverrideOfflineThenConfirmsAgainstDiscovery()
    {
        var account = TestDirectory.Profile() with { FolderOverrides = new() { Archive = "Manual" } };
        var receiver = new FolderNavigationTests.FolderReceiver();
        using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher()); model.SetAccount(account);
        Assert.Contains(model.Folders, folder => folder.MailboxId == "Manual");
        Assert.Contains("unverified", model.FolderRoleLabel("Manual")); Assert.True(model.SelectFolder("Manual"));
        receiver.Discover = _ => Task.FromResult<IReadOnlyList<MailFolder>>([new("Manual", null, true, []), new("Auto", null, true, ["\\Archive"])]);
        await model.RefreshFoldersAsync();
        Assert.Equal("Archive override", model.FolderRoleLabel("Manual")); Assert.Equal("", model.FolderRoleLabel("Auto"));
        Assert.True(model.FolderRoles.Single(role => role.Role == MailFolderRole.Archive).IsConfirmed);
    }

    private static IEnumerable<UiElement> Descendants(UiElement parent)
    { foreach (var child in parent.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
}
