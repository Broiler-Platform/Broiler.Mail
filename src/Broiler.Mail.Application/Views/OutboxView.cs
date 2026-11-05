using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Live local records only. Every submission still requires the ordinary Composer's explicit send action.</summary>
public sealed class OutboxView(ComposerViewModel model,Action openComposer)
{
    public Task OpenTask { get; private set; } = Task.CompletedTask;
    public UiElement CreateContent()
    {
        var panel=new StandardPanel{Spacing=8};
        var notice=ConfigurationForm.AddText(panel,model.OutboxNotice); var storage=ConfigurationForm.AddText(panel,model.StorageStatus);
        var filter=new StandardComboBox{AccessibleName="Outbox record filter"};
        filter.SetItems(new[]{"All retained records","Unsent drafts / rejected submissions","Sending / saving Sent copy","Needs outcome review","SMTP accepted"}.Select((text,index)=>new UiComboBoxItem(index.ToString(),text)));
        panel.AddChild(filter);
        var picker=new StandardComboBox{AccessibleName="Outbox records for selected account"};panel.AddChild(picker);
        var open=new StandardButton{Text="Open selected in Compose (save current first)"};panel.AddChild(open);
        var details=new ScrollableMessageText();details.Editor.AccessibleName="Retained submission details";
        panel.AddChild(new BoundedScrollArea(details,0.55,"Outbox details"));
        ConfigurationForm.AddText(panel,"Current composer plus at most 20 saved drafts across the profile. Records disappear when explicitly discarded/removed. The outbox never sends, retries, removes records or replays Sent copies. Accepted/unknown outcomes remain protected in Compose.");
        OutboxRecord[] shown=[]; AccountId? shownAccount=null; bool updating=false;
        OutboxRecord? Selected()=>picker.SelectedIndex>=0 && picker.SelectedIndex<shown.Length ? shown[picker.SelectedIndex] : null;
        void Refresh()
        {
            if(panel.IsDisposed)return; updating=true;
            var selected=Selected(); var rows=model.OutboxRecords.ToArray(); var account=model.OutboxAccount?.Id;
            bool changedAccount=shownAccount!=account;
            if(changedAccount || !shown.SequenceEqual(rows))
            {
                shownAccount=account;shown=rows;
                picker.SetItems(rows.Select(row=>new UiComboBoxItem(row.Id.ToString(),(row.IsActive?"Current · ":"Saved · ")+ComposerViewModel.OutboxDisplay(string.IsNullOrWhiteSpace(row.Subject)?"(No subject)":row.Subject,100)+$" · SMTP: {row.SubmissionLabel} · Sent copy: {row.CopyLabel}")));
                picker.SelectedIndex=changedAccount || selected is null ? -1 : Array.FindIndex(rows,row=>row.Id==selected.Id);
            }
            filter.SelectedIndex=(int)model.SelectedOutboxFilter; filter.IsEnabled=model.OutboxAccount is not null && !model.HasUnreadableDraft;
            picker.IsEnabled=model.OutboxAccount is not null && !model.HasUnreadableDraft;
            var current=Selected(); open.IsEnabled=current is not null && model.CanOpenOutboxRecord(current.Id);
            string text=current is null ? "Select a retained record to inspect its independent SMTP and Sent-copy outcomes." : model.OutboxDetails(current.Id);
            if(details.Text!=text)details.Text=text;
            notice.Text=model.OutboxNotice; storage.Text=model.StorageStatus;
            updating=false;
        }
        filter.SelectionChanged+=(_,_)=>{if(!updating && filter.SelectedIndex>=0)model.SetOutboxFilter((OutboxFilter)filter.SelectedIndex);};
        picker.SelectionChanged+=(_,_)=>{if(!updating)Refresh();};
        open.Clicked+=(_,_)=>OpenTask=OpenSelectedAsync();
        async Task OpenSelectedAsync()
        {
            if(Selected() is not {} selected)return; var account=selected.AccountId;
            var originalSession=panel.Session; var modal=originalSession?.ModalElement;
            if(!await model.OpenOutboxRecordAsync(selected.Id).ConfigureAwait(false))return;
            void Navigate()
            {
                if(originalSession is not null && (originalSession.IsDisposed || !ReferenceEquals(originalSession,panel.Session) || !ReferenceEquals(originalSession.ModalElement,modal)))return;
                if(!panel.IsDisposed && model.OutboxAccount?.Id==account && Selected()?.Id==selected.Id)openComposer();
            }
            try{if(panel.Session?.Dispatcher is {} dispatcher)dispatcher.Post(Navigate);else Navigate();}catch(ObjectDisposedException){}
        }
        model.Changed+=(_,_)=>Refresh();Refresh();return new BoundedScrollArea(panel,1,"Outbox");
    }
}
