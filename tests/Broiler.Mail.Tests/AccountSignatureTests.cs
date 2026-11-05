using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class AccountSignatureTests
{
    [Fact]
    public async Task SignaturesStayAccountScopedAndNewDraftTextSurvivesRestartWithoutReapplication()
    {
        using var directory = new TestDirectory(); var first = TestDirectory.Profile() with { Signature = "Regards,\r\nFirst" };
        var second = TestDirectory.Profile() with { Signature = "Second" };
        var accounts = new JsonAccountCollectionStore(directory.Root); await accounts.SaveAsync(first); await accounts.SaveAsync(second);
        var loaded = await new JsonAccountCollectionStore(directory.Root).LoadAsync();
        Assert.Equal(first.Signature, loaded.Single(a => a.Id == first.Id).Signature);
        Assert.Equal(second.Signature, loaded.Single(a => a.Id == second.Id).Signature);
        var store = new JsonDraftStore(directory.File("drafts.json")); string text;
        using (var model = new ComposerViewModel(store, await store.LoadAsync()))
        {
            model.SetAccount(first); Assert.True(model.StartNew()); text = model.PlainText;
            Assert.Equal("\n\n-- \nRegards,\nFirst", text); Assert.True(await model.SaveAsync());
            model.SetAccount(second); Assert.Equal(text, model.PlainText);
            model.SetAccount(first with { Signature = "Changed" }); Assert.Equal(text, model.PlainText);
        }
        using var reopened = new ComposerViewModel(new JsonDraftStore(directory.File("drafts.json")), await store.LoadAsync());
        reopened.SetAccount(first with { Signature = "Changed" }); Assert.Equal(text, reopened.PlainText);
        Assert.True(await reopened.DiscardAsync()); reopened.SetAccount(second); Assert.True(reopened.StartNew());
        Assert.Equal("\n\n-- \nSecond", reopened.PlainText); Assert.True(await reopened.SaveAsync());
    }

    [Theory]
    [InlineData(CompositionKind.Reply)]
    [InlineData(CompositionKind.ReplyAll)]
    [InlineData(CompositionKind.Forward)]
    public void SignaturePrecedesQuotedContentAndPreservesOriginalMessage(CompositionKind kind)
    {
        var account = TestDirectory.Profile() with { Signature = "Signed" };
        var body = new MailMessageBody(new(account.Id, "INBOX", 7, 1), "Original text")
            { Composition = new() { From = ["sender@example.test"], Subject = "Topic" } };
        using var model = new ComposerViewModel(); model.SetAccount(account);
        Assert.True(model.StartFromMessage(body, kind));
        Assert.StartsWith("\n\n-- \nSigned\n\n", model.PlainText); Assert.Contains("Original text", model.PlainText);
        Assert.Equal("Original text", body.PlainText);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void EmptySignaturePreservesExistingNewMessageBehavior(string signature)
    {
        using var model = new ComposerViewModel(); model.SetAccount(TestDirectory.Profile() with { Signature = signature });
        Assert.True(model.StartNew()); Assert.Equal("", model.PlainText);
    }

    [Fact]
    public void InvalidSignaturesAreRejectedBeforeStartingOrSavingConfiguration()
    {
        foreach (var signature in new[] { new string('a', 4001), "bad\0text", "\uD800" })
        {
            var account = TestDirectory.Profile() with { Signature = signature };
            var error = Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(account)); Assert.Equal("Signature", error.Field);
            using var model = new ComposerViewModel(); model.SetAccount(account); Assert.False(model.StartNew()); Assert.False(model.HasDraft);
        }
        ConfigurationValidator.Validate(TestDirectory.Profile() with { Signature = "Valid\nUnicode 😀\t" });
    }
}
