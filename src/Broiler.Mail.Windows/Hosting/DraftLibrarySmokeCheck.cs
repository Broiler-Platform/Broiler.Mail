using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class DraftLibrarySmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var drafts = new JsonDraftStore(profile.DraftsPath); Guid id;
        using (var model = new ComposerViewModel(drafts))
        {
            model.SetAccount(account); model.StartNew(); id = model.DraftId!.Value;
            model.Edit("unfinished <", "", "private@example.test", "Saved fixture", "First body");
            if (!await model.ParkDraftAsync() || model.HasDraft || model.SavedDrafts.Single().Draft.Id != id || !model.StartNew())
                throw new InvalidOperationException("Published multi-draft park failed.");
            model.Edit("second@example.test", "", "", "Second fixture", "Second body");
            if (!await model.ResumeDraftAsync(id) || model.To != "unfinished <" || model.PlainText != "First body" || model.SavedDrafts.Single().Draft.Subject != "Second fixture")
                throw new InvalidOperationException("Published multi-draft swap failed.");
            using var inbox = new InboxViewModel(new NoNetwork(), new Broiler.UI.Standard.ImmediateUiDispatcher());
            using var root = new ComposerView(model, inbox).CreateContent();
            var picker = All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Saved drafts for selected account");
            if (picker.Items.Count != 1 || !All(root).OfType<FormSection>().Any(item => item.Toggle?.Text.Contains("Saved drafts", StringComparison.Ordinal) == true))
                throw new InvalidOperationException("Published saved-draft controls missing.");
            if (!await model.SaveAsync()) throw new InvalidOperationException("Published multi-draft flush failed.");
        }
        var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        string backup = await storage.CreateBackupAsync(directory + "-backup"); string restored = await storage.RestoreBackupAsync(backup, directory + "-restored");
        var restoredDrafts = await new JsonDraftStore(Path.Combine(restored, "drafts.json")).LoadAsync();
        if (restoredDrafts.Draft?.Draft.Id != id || restoredDrafts.SavedDrafts?.Single().Draft.Subject != "Second fixture")
            throw new InvalidOperationException("Published multi-draft backup/restore failed.");
        using var reopened = new ComposerViewModel(drafts, await drafts.LoadAsync()); reopened.SetAccount(account);
        Guid other = reopened.SavedDrafts.Single().Draft.Id;
        if (await reopened.ConfirmSavedDraftRemovalAsync() || !reopened.RequestSavedDraftRemoval(other) || !await reopened.ConfirmSavedDraftRemovalAsync() || reopened.DraftId != id)
            throw new InvalidOperationException("Published confirmed library removal failed.");
        await reopened.ParkDraftAsync(); var state = await drafts.LoadAsync();
        var interrupted = await drafts.ActivateAsync(state.Revision, id);
        interrupted = await drafts.SaveAsync(interrupted.Revision, interrupted.Draft! with { State = DraftSubmissionState.Sending });
        interrupted = await drafts.ParkAsync(interrupted.Revision);
        using var uncertain = new ComposerViewModel(drafts, interrupted); uncertain.SetAccount(account);
        if (!await uncertain.ResumeDraftAsync(id) || uncertain.SubmissionState != DraftSubmissionState.Unknown || uncertain.CanSend || !await uncertain.SaveAsync())
            throw new InvalidOperationException("Published interrupted saved draft could resend.");
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var row in All(child)) yield return row; }
    private sealed class NoNetwork : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in draft fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken token = default) => throw new InvalidOperationException("No network in draft fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No network in draft fixture.");
    }
}
