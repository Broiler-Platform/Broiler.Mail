using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class UnifiedSavedInboxSmokeCheck
{
    internal static async Task RunAsync(string directory, AccountProfile first)
    {
        using var profile = MailProfile.Open(directory); var cache = new SqliteMailCache(profile.CachePath);
        var second = first with { Id = AccountId.New(), DisplayName = "Second saved account", IsEnabled = false };
        var accounts = new JsonAccountCollectionStore(directory); await accounts.SaveAndSelectAsync(second); await accounts.SaveAndSelectAsync(first);
        foreach (var account in new[] { first, second })
        {
            var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
            await cache.SaveSummariesAsync(new(account.Id, "INBOX", 7), [new() { Key = key, Sender = "fixture@example.test", Subject = "Same UID in two accounts", ReceivedAt = DateTimeOffset.UnixEpoch }], DateTimeOffset.UtcNow);
            await cache.SaveBodyAsync(new(account.Id, "INBOX", 7), new(key, "Saved text for " + account.Id));
        }
        using var application = new MailApplication(accounts, new JsonSettingsStore(profile.SettingsPath), new Receiver(), new Sender(), new Credentials(), cache: cache);
        await application.InitializeAsync(); var model = application.CreateViewModel(); await model.Inbox.CacheLoadTask;
        if (!model.Composer.StartNew()) throw new InvalidOperationException("Published unified fixture could not pin a draft.");
        var draft = model.Composer.DraftId;
        using var shell = new MailShellView(model); using var session = new StandardUiSessionBuilder().WithDispatcher(new ImmediateUiDispatcher()).Build(new Host()); shell.Attach(session);
        if (!shell.CreateKeyboardNavigation(session).Execute(MailCommand.UnifiedSavedInbox) || shell.ActiveViewId != "unified") throw new InvalidOperationException("Published unified navigation failed.");
        var view = shell.UnifiedInbox; if (view.Rows.Count != 0) throw new InvalidOperationException("Unified view loaded automatically.");
        await view.RefreshAsync(); if (view.Rows.Count != 2) throw new InvalidOperationException("Published unified account aggregation failed.");
        var row = view.Rows.Single(row => row.Account.Id == second.Id); await view.ReadAsync(row); session.RenderFrame();
        if (!view.PreviewText.Contains("Saved text for " + second.Id) || !view.PreviewText.Contains("Account " + second.Id) || model.CurrentAccount?.Id != first.Id || model.Composer.DraftId != draft || model.Composer.DraftAccountId != first.Id)
            throw new InvalidOperationException("Published unified reader mixed accounts or changed a pinned draft.");
        shell.CloseDialog(); if (view.Rows.Count != 0 || view.PreviewText.Length != 0) throw new InvalidOperationException("Published unified close retained old rows or text.");
    }
    private sealed class Receiver : IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account, CancellationToken token = default) => throw new InvalidOperationException("No provider calls in unified fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account, int count, MailInboxCursor? older = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No receive in unified fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account, MailMessageKey key, CancellationToken token = default) => throw new InvalidOperationException("No body download in unified fixture.");
    }
    private sealed class Sender : IMailSender { public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default) => throw new InvalidOperationException("No send in unified fixture."); }
    private sealed class Credentials : ICredentialStore
    {
        public Task<string?> ReadAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("No credentials in unified fixture.");
        public Task WriteAsync(CredentialKey key, string value, CancellationToken token = default) => throw new InvalidOperationException("No credentials in unified fixture.");
        public Task DeleteAsync(CredentialKey key, CancellationToken token = default) => throw new InvalidOperationException("No credentials in unified fixture.");
    }
    private sealed class Host : IUiHost
    { public BSize ViewportSize => new(1100, 720); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { } }
}
