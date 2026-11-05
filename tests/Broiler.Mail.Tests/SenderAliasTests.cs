using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class SenderAliasTests
{
    [Fact]
    public async Task AliasesPersistAndRestoredDraftKeepsItsSenderUntilExplicitlyReauthorized()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile() with { SenderAliases = "alias@example.test\nsecond@example.test" };
        var accounts = new JsonAccountCollectionStore(directory.Root); await accounts.SaveAsync(account);
        account = (await new JsonAccountCollectionStore(directory.Root).LoadAsync()).Single(); Assert.Contains("alias@example.test", account.SenderAddresses());
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using (var model = new ComposerViewModel(store, await store.LoadAsync()))
        {
            model.SetAccount(account); Assert.True(model.SelectSender("alias@example.test")); Assert.True(model.StartNew());
            model.Edit("to@example.test", "", "", "Topic", "Body"); Assert.Equal("alias@example.test", model.BuildDraft().FromAddress); Assert.True(await model.SaveAsync());
        }
        using var reopened = new ComposerViewModel(store, await store.LoadAsync()); reopened.SetAccount(account with { SenderAliases = "" });
        Assert.Equal("alias@example.test", reopened.FromAddress); Assert.Throws<InvalidOperationException>(() => reopened.BuildDraft());
        reopened.SetAccount(account); Assert.Equal("alias@example.test", reopened.BuildDraft().FromAddress);
        Assert.False(reopened.SelectSender(account.EmailAddress)); Assert.True(await reopened.SaveAsync());
    }

    [Theory]
    [InlineData("bad address")]
    [InlineData("Name <alias@example.test>")]
    [InlineData("alias@example.test\nALIAS@example.test")]
    [InlineData("alias@example.test\0")]
    public void InvalidAliasesAreRejected(string aliases)
    {
        Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(TestDirectory.Profile() with { SenderAliases = aliases }));
    }

    [Fact]
    public void DuplicatePrimaryAndExcessiveAliasesAreRejectedAndBlankLinesCannotAuthorizeEmptySender()
    {
        var account = TestDirectory.Profile();
        Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(account with { SenderAliases = account.EmailAddress }));
        Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(account with { SenderAliases = string.Join("\n", Enumerable.Range(1, 17).Select(i => $"alias{i}@example.test")) }));
        account = account with { SenderAliases = " \n\n" }; ConfigurationValidator.Validate(account); Assert.False(account.HasSenderAddress(""));
        Assert.Throws<ArgumentException>(() => MailComposition.Create(account, "arbitrary@example.test"));
    }

    [Fact]
    public void ReplyAllExcludesPrimaryAndEveryOwnAliasWithoutLosingVisibleForeignRecipients()
    {
        var account = TestDirectory.Profile() with { SenderAliases = "alias@example.test\nother-own@example.test" };
        var body = new MailMessageBody(new(account.Id, "INBOX", 7, 1), "Body") { Composition = new()
            { From = ["sender@example.test"], To = [account.EmailAddress, "ALIAS@example.test", "friend@example.test"], Cc = ["other-own@example.test", "copy@example.test"] } };
        var draft = MailComposition.Create(account, body, CompositionKind.ReplyAll, "alias@example.test");
        Assert.Equal("alias@example.test", draft.FromAddress); Assert.Equal(new[] { "sender@example.test", "friend@example.test" }, draft.To); Assert.Equal(new[] { "copy@example.test" }, draft.Cc);
        MailComposition.ValidateDraft(account, draft);
        Assert.Throws<InvalidOperationException>(() => MailComposition.ValidateDraft(account with { SenderAliases = "" }, draft));
    }
}
