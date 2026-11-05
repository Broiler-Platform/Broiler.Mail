using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Windows.Hosting;

internal static class SignatureSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        Directory.CreateDirectory(directory);
        var accounts = new JsonAccountCollectionStore(directory);
        await accounts.SaveAsync(account with { Signature = "Synthetic signature\nSecond line", SenderAliases = "alias@example.test\nreceiving@example.test", DefaultSenderAddress = "alias@example.test", ReplyFromReceivingAlias = true, UseSeparateReplySignature = true, ReplySignature = "Synthetic reply signature" });
        var saved = (await new JsonAccountCollectionStore(directory).LoadAsync()).Single();
        if (saved.DefaultSender != "alias@example.test" || !saved.ReplyFromReceivingAlias || !saved.UseSeparateReplySignature || saved.ReplySignature != "Synthetic reply signature")
            throw new InvalidOperationException("Published composition defaults restart failed.");
        if (!saved.HasSenderAddress("alias@example.test")) throw new InvalidOperationException("Published alias account restart failed.");
        if (saved.Signature != "Synthetic signature\nSecond line") throw new InvalidOperationException("Published signature account restart failed.");
        var store = new JsonDraftStore(Path.Combine(directory, "drafts.json")); string text;
        using (var model = new ComposerViewModel(store, await store.LoadAsync()))
        {
            model.SetAccount(saved);
            if (!model.SelectSender("alias@example.test")) throw new InvalidOperationException("Published alias selection failed.");
            if (!model.StartNew() || !model.PlainText.Contains("-- \nSynthetic signature\nSecond line", StringComparison.Ordinal) || !await model.SaveAsync())
                throw new InvalidOperationException("Published signature draft creation failed.");
            if (model.FromAddress != "alias@example.test") throw new InvalidOperationException("Published draft did not pin alias sender.");
            text = model.PlainText;
        }
        using var reopened = new ComposerViewModel(store, await store.LoadAsync());
        reopened.SetAccount(saved with { Signature = "Changed later" });
        if (reopened.FromAddress != "alias@example.test") throw new InvalidOperationException("Published alias draft restart failed.");
        if (reopened.PlainText != text) throw new InvalidOperationException("Published signature draft restart reapplied configuration.");
        reopened.Edit("recipient@example.test", "", "", "Fixture", "See attached file.");
        if (reopened.AttachmentReminderText is null || !reopened.CanEdit)
            throw new InvalidOperationException("Published attachment reminder failed.");
        reopened.Edit("recipient@example.test", "", "", "Fixture", "No file mentioned.");
        if (reopened.AttachmentReminderText is not null) throw new InvalidOperationException("Published attachment reminder did not clear.");
        reopened.SetAccount(saved with { SenderAliases = "" });
        try { _ = reopened.BuildDraft(); throw new InvalidOperationException("Published removed alias was accepted."); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("The saved sender changed", StringComparison.Ordinal)) { }
        reopened.SetAccount(saved);
        if (reopened.BuildDraft().FromAddress != "alias@example.test") throw new InvalidOperationException("Published alias reauthorization failed.");
        using var reply = new ComposerViewModel(); reply.SetAccount(saved);
        var source = new Broiler.Mail.Core.Messages.MailMessageBody(new(saved.Id, "INBOX", 7, 1), "Original text")
            { Composition = new() { From = ["sender@example.test"], To = ["receiving@example.test"] } };
        if (!reply.StartFromMessage(source, Broiler.Mail.Core.Messages.CompositionKind.Reply) || reply.FromAddress != "receiving@example.test" ||
            !reply.PlainText.Contains("-- \nSynthetic reply signature", StringComparison.Ordinal))
            throw new InvalidOperationException("Published automatic receiving sender/reply signature failed.");
        if (!await reopened.SaveAsync()) throw new InvalidOperationException("Published signature draft flush failed.");
    }
}
