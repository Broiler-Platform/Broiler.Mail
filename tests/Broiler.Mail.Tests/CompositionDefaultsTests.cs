using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Validation;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

public sealed class CompositionDefaultsTests
{
    [Fact]
    public async Task SavedDefaultInitializesNewDraftButNeverRewritesRestoredDraft()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile() with
        { SenderAliases = "alias@example.test", DefaultSenderAddress = "alias@example.test", ReplyFromReceivingAlias = true, UseSeparateReplySignature = true, ReplySignature = "Reply sign" };
        var accounts = new JsonAccountCollectionStore(directory.Root); await accounts.SaveAsync(account);
        var saved = (await new JsonAccountCollectionStore(directory.Root).LoadAsync()).Single(); Assert.Equal(account, saved);
        Assert.Equal("alias@example.test", MailComposition.Create(saved).FromAddress);
        var store = new JsonDraftStore(directory.File("drafts.json"));
        using (var model = new ComposerViewModel(store, await store.LoadAsync()))
        { model.SetAccount(saved); Assert.Equal("alias@example.test", model.FromAddress); model.StartNew(); Assert.True(await model.SaveAsync()); }
        using var reopened = new ComposerViewModel(store, await store.LoadAsync()); reopened.SetAccount(saved with { DefaultSenderAddress = "", ReplySignature = "Changed" });
        Assert.Equal("alias@example.test", reopened.FromAddress); Assert.True(await reopened.DiscardAsync());
        Assert.True(reopened.StartNew()); Assert.Equal(saved.EmailAddress, reopened.FromAddress); Assert.True(await reopened.SaveAsync());
    }

    [Theory]
    [InlineData(0, "alias@example.test")]
    [InlineData(1, "default@example.test")]
    [InlineData(2, "default@example.test")]
    [InlineData(3, "alias@example.test")]
    public void OptInReplyPolicyRequiresOneVisibleConfiguredReceivingAddress(int mode, string expected)
    {
        var account = TestDirectory.Profile() with { SenderAliases = "alias@example.test\ndefault@example.test", DefaultSenderAddress = "default@example.test", ReplyFromReceivingAlias = true };
        var source = new MailCompositionSource { From = ["sender@example.test"],
            To = mode switch { 0 => ["alias@example.test"], 1 => ["alias@example.test", account.EmailAddress], _ => ["foreign@example.test"] },
            Cc = mode == 3 ? ["ALIAS@example.test"] : [] };
        using var model = new ComposerViewModel(); model.SetAccount(account);
        Assert.True(model.StartFromMessage(new(new(account.Id, "INBOX", 7, 1), "Body") { Composition = source }, CompositionKind.Reply));
        Assert.Equal(expected, model.FromAddress);
    }

    [Fact]
    public void ExplicitChoiceWinsAndReplyPolicyIsOptIn()
    {
        var account = TestDirectory.Profile() with { SenderAliases = "alias@example.test", ReplyFromReceivingAlias = true };
        var body = new MailMessageBody(new(account.Id, "INBOX", 7, 1), "Body") { Composition = new() { From = ["sender@example.test"], To = ["alias@example.test"] } };
        using var explicitModel = new ComposerViewModel(); explicitModel.SetAccount(account); explicitModel.SelectSender(account.EmailAddress);
        Assert.True(explicitModel.StartFromMessage(body, CompositionKind.Reply)); Assert.Equal(account.EmailAddress, explicitModel.FromAddress);
        using var disabled = new ComposerViewModel(); disabled.SetAccount(account with { ReplyFromReceivingAlias = false });
        disabled.StartFromMessage(body, CompositionKind.Reply); Assert.Equal(account.EmailAddress, disabled.FromAddress);
    }

    [Theory]
    [InlineData(CompositionKind.Reply, "Reply sign")]
    [InlineData(CompositionKind.ReplyAll, "Reply sign")]
    [InlineData(CompositionKind.Forward, "Normal sign")]
    public void SeparateReplySignatureOnlyChangesNewReplies(CompositionKind kind, string expected)
    {
        var account = TestDirectory.Profile() with { Signature = "Normal sign", ReplySignature = "Reply sign", UseSeparateReplySignature = true };
        var body = new MailMessageBody(new(account.Id, "INBOX", 7, 1), "Original text") { Composition = new() { From = ["sender@example.test"] } };
        using var model = new ComposerViewModel(); model.SetAccount(account); Assert.True(model.StartFromMessage(body, kind));
        Assert.StartsWith("\n\n-- \n" + expected, model.PlainText); Assert.Contains("Original text", model.PlainText);
        string before = model.PlainText; model.SetAccount(account with { ReplySignature = "Changed" }); Assert.Equal(before, model.PlainText);
    }

    [Fact]
    public void EmptySeparateReplySignatureDisablesReplySignatureAndInvalidDefaultsAreRejected()
    {
        var account = TestDirectory.Profile() with { Signature = "Normal", UseSeparateReplySignature = true };
        using var model = new ComposerViewModel(); model.SetAccount(account);
        model.StartFromMessage(new(new(account.Id, "INBOX", 7, 1), "Text") { Composition = new() { From = ["sender@example.test"] } }, CompositionKind.Reply);
        Assert.DoesNotContain("-- ", model.PlainText);
        Assert.Equal("DefaultSenderAddress", Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(account with { DefaultSenderAddress = "unconfigured@example.test" })).Field);
        Assert.Equal("ReplySignature", Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.Validate(account with { ReplySignature = new string('a', 4001) })).Field);
    }
}
