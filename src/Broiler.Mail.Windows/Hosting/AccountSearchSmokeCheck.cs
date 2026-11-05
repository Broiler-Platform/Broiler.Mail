using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class AccountSearchSmokeCheck
{
    internal static async Task RunAsync(string directory,AccountProfile account)
    {
        Directory.CreateDirectory(directory);var cache=new SqliteMailCache(Path.Combine(directory,"mail.db"));
        var rows=Enumerable.Range(1,501).Select(uid=>new MailMessageSummary{Key=new(account.Id,"Z",9,(uint)uid),Subject="needle",Sender="fixture@example.test"}).ToArray();
        foreach(var batch in rows.Chunk(500))await cache.SaveSummariesAsync(new(account.Id,"Z",9),batch,DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(new(account.Id,"Z",9),new(new(account.Id,"Z",9,1),"Saved Z body"));
        await cache.SaveSummariesAsync(new(account.Id,"INBOX",9),[new(){Key=new(account.Id,"INBOX",9,1),Subject="needle",Sender="fixture@example.test"}],DateTimeOffset.UtcNow);
        using var model=new InboxViewModel(new Receiver(),new ImmediateUiDispatcher(),cache);model.SetAccount(account with{IsEnabled=false});await model.CacheLoadTask;
        using var view=new InboxView(model).CreateContent();var scope=All(view).OfType<StandardComboBox>().Single(combo=>combo.AccessibleName=="Saved search folder scope");
        if(scope.SelectedIndex!=0)throw new InvalidOperationException("Published search default scope changed.");scope.SelectedIndex=1;model.SetSearch("needle");await model.SearchSavedHeadersAsync();
        if(model.ListProblem is not null || !model.IsAccountSearchActive || model.Messages.Count!=500 || !model.CanLoadNextSearchPage)throw new InvalidOperationException("Published all-folder search failed.");
        await model.LoadNextSearchPageAsync();if(model.Messages.Count!=2 || model.ListProblem is not null)throw new InvalidOperationException("Published cross-folder continuation failed.");
        await model.SelectAsync(new(account.Id,"Z",9,1));
        if(model.Body?.PlainText!="Saved Z body" || model.SelectedMailboxId!="INBOX" || !All(view).OfType<StandardLabel>().Any(label=>label.Text.Contains("Folder Z") && label.Text.Contains(account.EmailAddress)))throw new InvalidOperationException("Published all-folder reader attribution failed.");
        await model.LoadPreviousSearchPageAsync();await cache.SaveSummariesAsync(new(account.Id,"INBOX",10),[],DateTimeOffset.UtcNow);await model.LoadNextSearchPageAsync();
        if(model.ListProblem is null || !model.ListProblem.Text.Contains("Start a new search"))throw new InvalidOperationException("Published all-folder epoch change was not explained.");
        await model.SearchSavedHeadersAsync();if(model.ListProblem is not null || !model.IsAccountSearchActive)throw new InvalidOperationException("Published all-folder search restart failed.");
    }
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class Receiver:IMailReceiver
    {
        public Task TestConnectionAsync(AccountProfile account,CancellationToken token=default)=>throw new InvalidOperationException("No network in search fixture.");
        public Task<MailInboxPage> GetInboxAsync(AccountProfile account,int count,MailInboxCursor? older=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("No receive in search fixture.");
        public Task<MailMessageBody> GetBodyAsync(AccountProfile account,MailMessageKey key,CancellationToken token=default)=>throw new InvalidOperationException("No body fetch in search fixture.");
    }
}
