using Broiler.Graphics.Geometry;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Explicit local inventory/save of attachments in one current verified original-source export.</summary>
public sealed class MessageAttachmentActions:UiElement
{
    private readonly MailMessageKey _key;private readonly IMessageAttachmentExports? _exports;private readonly Func<bool> _current;
    private readonly StandardPanel _layout=new(){Spacing=4};
    private readonly StandardButton _list=new(){Text="Read attachments from saved .eml"},_save=new(){Text="Save selected attachment"},_cancel=new(){Text="Cancel attachment operation"};
    private readonly StandardComboBox _picker=new(){AccessibleName="Attachments in current saved source"};
    private readonly StandardRichEdit _path=new(){IsReadOnly=true,AccessibleName="Saved attachment path and checksum",Wrapping=RichEditWrapping.Wrap,VerticalScrollPolicy=RichEditScrollPolicy.Never,HorizontalScrollPolicy=RichEditScrollPolicy.Never};
    private readonly InlineFeedback _feedback=new();
    private MessageSourceExport? _source;private IReadOnlyList<ReceivedAttachment> _attachments=[];private CancellationTokenSource? _operation;private int _generation;
    public IReadOnlyList<ReceivedAttachment> Attachments=>_attachments;public bool IsBusy=>_operation is not null;public string Status=>_feedback.Message;
    public MessageAttachmentActions(MailMessageKey key,IMessageAttachmentExports? exports,Func<bool> current)
    {
        _key=key;_exports=exports;_current=current;AddChild(_layout);Visibility=UiVisibility.Collapsed;AccessibleName="Received attachment export controls";
        ConfigurationForm.AddText(_layout,"Save up to 32 supported attachments from a verified original of up to 12 MiB. Files use unique safe names in attachment-exports and stay outside profile backups. Inline resources may be omitted.");
        var actions=new StandardToolbar{Overflow=UiToolbarOverflow.Wrap,Padding=4,Spacing=6};actions.AddChild(_list);actions.AddChild(_save);actions.AddChild(_cancel);
        _layout.AddChild(actions);_layout.AddChild(_picker);_layout.AddChild(_feedback);_layout.AddChild(_path);
        _list.Clicked+=async(_,_)=>await ListAsync();_save.Clicked+=async(_,_)=>{if(Selected() is {} selected)await SaveAsync(selected);};_cancel.Clicked+=(_,_)=>Cancel();_picker.SelectionChanged+=(_,_)=>UpdateControls();UpdateControls();
    }
    public void SetExport(MessageSourceExport? source)
    {
        if(IsDisposed)return;if(source is not null && source.Key!=_key)throw new ArgumentException("Attachment source belongs to another message.");
        ++_generation;Cancel();_source=source;ClearInventory();Visibility=source is not null && _exports is not null ? UiVisibility.Visible:UiVisibility.Collapsed;
        _feedback.Set("No attachment inventory loaded. Read explicitly from the saved original; no server request.",FeedbackKind.Information);UpdateControls();
    }
    private ReceivedAttachment? Selected()=>_picker.SelectedIndex>=0 && _picker.SelectedIndex<_attachments.Count ? _attachments[_picker.SelectedIndex]:null;
    private void ClearInventory(){_attachments=[];_picker.SetItems([]);_picker.SelectedIndex=-1;_path.SetPlainText("");}
    public void RefreshContext()
    {
        if(IsDisposed)return;if(!_current()){++_generation;Cancel();ClearInventory();Visibility=UiVisibility.Collapsed;}UpdateControls();
    }
    public void Cancel(){if(!IsDisposed)_operation?.Cancel();}
    public Task ListAsync()=>RunAsync(null);
    public Task SaveAsync(ReceivedAttachment attachment)=>RunAsync(attachment);
    private async Task RunAsync(ReceivedAttachment? selected)
    {
        if(IsDisposed || IsBusy || !_current() || _source is not {} source || _exports is null)return;
        if(selected is not null && !_attachments.Contains(selected))return;
        int generation=++_generation;var cancellation=_operation=new CancellationTokenSource();var token=cancellation.Token;
        if(selected is null)ClearInventory();else _path.SetPlainText("");
        _feedback.Set(selected is null?"Verifying source and reading attachment inventory…":"Verifying source and saving selected attachment…",FeedbackKind.Progress);UpdateControls();
        if(Session is {} session)FocusNavigation.FocusAndReveal(session,_cancel);
        IReadOnlyList<ReceivedAttachment>? rows=null;SavedAttachment? saved=null;string? error=null;
        try
        {
            if(selected is null)
            {
                rows=await _exports.ListAttachmentsAsync(source,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
                if(rows.Count>32 || rows.Any(row=>row.Key!=_key || !string.Equals(row.SourceSha256,source.Sha256,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Attachment inventory returned another source identity.");
            }
            else
            {
                saved=await _exports.SaveAttachmentAsync(source,selected,token).ConfigureAwait(false);
                if(saved.Attachment!=selected)throw new InvalidDataException("Attachment save returned another identity.");
            }
        }
        catch(OperationCanceledException){error="Attachment cancellation requested. A file completed just before cancellation may remain; no file was opened.";}
        catch(Exception failure)when(failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {error="Attachment operation did not finish. "+failure.Message;}
        if(IsDisposed){cancellation.Dispose();return;}
        void Commit()
        {
            if(IsDisposed || !ReferenceEquals(_operation,cancellation)){cancellation.Dispose();return;}
            bool canceled=cancellation.IsCancellationRequested;_operation=null;cancellation.Dispose();
            if(generation==_generation && _current())
            {
                if(error is null && !canceled && rows is not null)
                {
                    _attachments=Array.AsReadOnly(rows.ToArray());_picker.SetItems(_attachments.Select(row=>new UiComboBoxItem(row.PartNumber.ToString(),$"{row.FileName} · {row.MediaType} · {row.Bytes} bytes")));_picker.SelectedIndex=-1;
                    _feedback.Set($"{rows.Count} supported attachment parts found locally. Choose one, then Save. Inline resources are not general attachments. Nothing was downloaded or opened.",FeedbackKind.Information);
                }
                else if(error is null && !canceled && saved is not null)
                {
                    _path.SetPlainText(saved.Path+"\n"+selected!.Bytes+" decoded bytes; SHA-256 "+selected.Sha256);
                    _feedback.Set("Attachment bytes saved. The file was not opened. Exports are retained on cache cleanup and excluded from profile backups.",FeedbackKind.Success);
                }
                else{ClearInventory();_feedback.Set(error??"Attachment operation canceled. Read the saved inventory again to retry.",FeedbackKind.Information);}
            }
            UpdateControls();if(Session is {} current && current.FocusedElement==_cancel)FocusNavigation.FocusAndReveal(current,_attachments.Count>0?_picker:_list);
        }
        try{if(Session?.Dispatcher is {} dispatcher)dispatcher.Post(Commit);else Commit();}catch(ObjectDisposedException){_operation=null;cancellation.Dispose();}
    }
    private void UpdateControls()
    {
        if(IsDisposed)return;bool available=_current() && _exports is not null && _source is not null;
        _list.IsEnabled=available&&!IsBusy;_picker.IsEnabled=available&&!IsBusy&&_attachments.Count>0;_save.IsEnabled=available&&!IsBusy&&Selected() is not null;_cancel.IsEnabled=IsBusy;
    }
    protected override BSize MeasureCore(BSize size)=>_layout.Measure(size);protected override void ArrangeCore(BRect rect)=>_layout.Arrange(rect);
    protected override void Dispose(bool disposing){if(disposing){_operation?.Cancel();_operation?.Dispose();_operation=null;_attachments=[];_source=null;}base.Dispose(disposing);}
}
