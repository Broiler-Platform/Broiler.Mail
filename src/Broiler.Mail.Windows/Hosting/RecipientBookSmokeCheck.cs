using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Windows.Hosting;

internal static class RecipientBookSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory);
        var book = new JsonRecipientBook(Path.Combine(directory, "recipients.json"));
        var recipient = new SavedRecipient(account.Id, "saved@example.test", "Saved person");
        await book.SaveAsync(recipient); await book.SaveAsync(new(AccountId.New(), "other@example.test", "Other account"));
        var drafts = new JsonDraftStore(profile.DraftsPath);
        using (var model = new ComposerViewModel(drafts, recipientBook: book))
        {
            model.SetAccount(account); await model.RecipientBook!.LoadTask;
            if (!model.StartNew() || model.RecipientBook.Suggest("SAVED").Single() != recipient || model.RecipientBook.Suggest("other").Count != 0 ||
                !model.InsertRecipient("Bcc", recipient) || !await model.SaveAsync()) throw new InvalidOperationException("Published recipient suggestions/insertion failed.");
        }
        if ((await drafts.LoadAsync()).Draft?.BccText != recipient.Address) throw new InvalidOperationException("Published recipient draft persistence failed.");
        var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        string backup = await storage.CreateBackupAsync(directory + "-backup");
        string restored = await storage.RestoreBackupAsync(backup, directory + "-restored");
        if ((await new JsonRecipientBook(Path.Combine(restored, "recipients.json")).LoadAsync(account.Id)).Single() != recipient)
            throw new InvalidOperationException("Published recipient backup/restore failed.");
        await book.RemoveAsync(account.Id, recipient.Address);
        if ((await book.LoadAsync(account.Id)).Count != 0 || (await drafts.LoadAsync()).Draft?.BccText != recipient.Address)
            throw new InvalidOperationException("Published recipient removal changed draft.");
        string bookPath = Path.Combine(directory, "recipients.json"); byte[] validBook = File.ReadAllBytes(bookPath);
        File.WriteAllText(bookPath, "synthetic corrupt recipient book");
        using (var manual = new ComposerViewModel(drafts, await drafts.LoadAsync(), recipientBook: book))
        {
            manual.SetAccount(account); await manual.RecipientBook!.LoadTask;
            manual.Edit("manual@example.test", "", recipient.Address, "Manual fixture", "Retained body");
            if (!manual.RecipientBook.IsError || manual.RecipientBook.IsReady || !manual.CanEdit || !await manual.SaveAsync() ||
                File.ReadAllText(bookPath) != "synthetic corrupt recipient book")
                throw new InvalidOperationException("Published corrupt recipient-book isolation failed.");
        }
        File.WriteAllBytes(bookPath, validBook);
    }
}
