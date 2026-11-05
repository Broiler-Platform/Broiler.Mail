using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class MailboxSnapshotSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile account)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath); var storage = new ProfileStorageService(profile, cache); var box = new MailboxKey(account.Id, "INBOX", 7);
        var retained = new MailMessageKey(account.Id, "INBOX", 7, 99);
        await cache.SaveSummariesAsync(box, [new() { Key = retained, Sender = "saved@example.test", Subject = "Retained absent mail" }], DateTimeOffset.UtcNow.AddMinutes(-1)); await cache.SaveBodyAsync(box, new(retained, "Retained offline body"));
        var receiver = new Receiver(); using var model = new InboxViewModel(receiver, new ImmediateUiDispatcher(), cache); model.SetAccount(account with { IsEnabled = true }); await model.CacheLoadTask;
        using var root = new InboxView(model).CreateContent(); using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root); session.RenderFrame();
        All(root).OfType<StandardButton>().Single(button => button.Text == "Show offline folders").Click(); session.RenderFrame();
        if (receiver.Calls != 0 || !All(root).OfType<StandardButton>().Single(button => button.Text == "Snapshot all headers in current folder").IsEnabled) throw new InvalidOperationException("Published snapshot controls started networking or were unavailable.");
        await model.CaptureMailboxSnapshotAsync(); session.RenderFrame();
        var checkpoint = await new SqliteMailCache(profile.CachePath).ReadMailboxCheckpointAsync(account.Id, "INBOX");
        if (model.ListProblem is not null || model.Messages.Count != 3 || checkpoint is not { ServerMessages: 2, RetainedAbsentMessages: 1 } || !model.MailboxSnapshotNotice.Contains("historical membership")) throw new InvalidOperationException("Published complete snapshot/absent retention/checkpoint failed.");
        await model.SelectAsync(retained); if (model.Body?.PlainText != "Retained offline body" || !model.IsBodyFromCache) throw new InvalidOperationException("Published snapshot discarded absent downloaded mail.");
        model.SetAccount(account with { IsEnabled = false }); await model.CacheLoadTask; await model.InspectMailboxSnapshotAsync(); session.RenderFrame();
        if (receiver.Calls != 1 || model.CanCaptureMailboxSnapshot || !model.CanInspectMailboxSnapshot || !model.MailboxSnapshotNotice.Contains("2 server headers")) throw new InvalidOperationException("Published disabled-account checkpoint inspection connected to server.");
        string backup = await storage.CreateBackupAsync(directory + "-backup"); string restored = await storage.RestoreBackupAsync(backup, directory + "-restored"); using var recovery = MailProfile.Open(restored);
        if (await new SqliteMailCache(recovery.CachePath).ReadMailboxCheckpointAsync(account.Id, "INBOX") != checkpoint) throw new InvalidOperationException("Published snapshot checkpoint restore failed.");
        await storage.ClearAccountDownloadedMailAsync(account.Id, directory + "-cleanup-backup");
        if (await cache.ReadMailboxCheckpointAsync(account.Id, "INBOX") is not null) throw new InvalidOperationException("Published account cleanup retained stale census/checkpoint data.");
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class Receiver : IMailReceiver, IMailMailboxSnapshotReceiver
    {
        public int Calls;
        public Task<MailMailboxSnapshot> GetMailboxSnapshotAsync(AccountProfile account, string folder, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(new MailMailboxSnapshot(new(account.Id, folder, 7), [new() { Key = new(account.Id, folder, 7, 1), Sender = "fixture@example.test", Subject = "First" }, new() { Key = new(account.Id, folder, 7, 2), Sender = "fixture@example.test", Subject = "Second", IsRead = true }], DateTimeOffset.UtcNow));
        }
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No network in published snapshot fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No ordinary receive in snapshot fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No body acquisition in snapshot fixture.");
    }
    private sealed class Host : IUiHost { public BSize ViewportSize => new(1100, 720); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { } }
}
