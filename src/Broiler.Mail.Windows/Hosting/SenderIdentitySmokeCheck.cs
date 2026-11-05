using System.Text;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class SenderIdentitySmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile fixture)
    {
        using var profile = MailProfile.Open(directory); var accounts = new JsonAccountCollectionStore(directory);
        var account = fixture with { SenderAliases = "alias@example.test", DefaultSenderAddress = "alias@example.test", Signature = "Account signature",
            ReplyFromReceivingAlias = true, SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Alias, Native", Signature = "Alias signature", ReplySignature = "" }] };
        await accounts.SaveAsync(account); account = (await accounts.LoadAsync()).Single();
        var drafts = new JsonDraftStore(profile.DraftsPath); string body;
        using (var model = new ComposerViewModel(drafts))
        {
            model.SetAccount(account); if (!model.StartNew() || model.FromDisplayName != "Alias, Native" || !model.PlainText.Contains("Alias signature", StringComparison.Ordinal))
                throw new InvalidOperationException("Published sender identity defaults failed.");
            body = model.PlainText; model.Edit("to@example.test", "", "private@example.test", "Fixture", body);
            if (!await model.SaveAsync()) throw new InvalidOperationException("Published sender identity draft save failed.");
        }
        account = account with { SenderIdentities = [new() { Address = "alias@example.test", DisplayName = "Changed Name", Signature = "Changed signature", ReplySignature = "" }] };
        await accounts.SaveAsync(account);
        using var restored = new ComposerViewModel(drafts, await drafts.LoadAsync()); restored.SetAccount(account);
        string wire = Encoding.UTF8.GetString(MimeSerializer.Serialize(account, restored.BuildDraft()));
        if (restored.FromDisplayName != "Alias, Native" || restored.PlainText != body || !wire.Contains("From: \"Alias, Native\" <alias@example.test>", StringComparison.Ordinal) ||
            MimeParser.Parse(Encoding.UTF8.GetBytes(wire)).From.Single() != "alias@example.test") throw new InvalidOperationException("Published pinned sender identity/MIME failed.");
        if (!await restored.ParkDraftAsync() || !restored.StartFromMessage(new(new(account.Id, "INBOX", 7, 1), "Quoted text")
            { Composition = new() { From = ["other@example.test"], To = ["ALIAS@example.test"] } }, CompositionKind.Reply) || restored.FromAddress != "alias@example.test" ||
            restored.FromDisplayName != "Changed Name" || restored.PlainText.Contains("-- ", StringComparison.Ordinal) || !await restored.SaveAsync())
            throw new InvalidOperationException("Published sender reply suppression failed.");
        var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        string backup = await storage.CreateBackupAsync(directory + "-backup"); string recovered = await storage.RestoreBackupAsync(backup, directory + "-restored");
        var saved = (await new JsonAccountCollectionStore(recovered).LoadAsync()).Single();
        if (saved.SenderDisplayName("alias@example.test") != "Changed Name" || saved.SenderSignature("alias@example.test", true) != "") throw new InvalidOperationException("Published sender identity backup/restore failed.");
        var editor = new AccountProfileViewModel(accounts, new Credentials(), new Receiver(), new ImmediateUiDispatcher(), account, null);
        using var content = new AccountProfileView(editor).CreateContent();
        if (All(content).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Sender address to customize").Items.Count != 2)
            throw new InvalidOperationException("Published sender identity editor missing.");
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class Receiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in identity fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in identity fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No network in identity fixture.");
    }
    private sealed class Credentials : ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(CredentialKey key, string secret, CancellationToken token = default) => throw new InvalidOperationException("No credentials in identity fixture.");
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("No credentials in identity fixture.");
    }
}
