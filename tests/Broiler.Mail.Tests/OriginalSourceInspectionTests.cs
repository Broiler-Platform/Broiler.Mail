using System.Text;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OriginalSourceInspectionTests
{
    [Fact]
    public void ByteViewEscapesControlsNonAsciiAndBackslashesWithoutDecodingMarkupOrEncodedWords()
    {
        byte[] original = Encoding.ASCII.GetBytes("Subject: =?utf-8?Q?Hello?=\r\n\r\n<script>unchanged</script>\\path\t").Concat(new byte[]{0,27,127,128,255}).ToArray();
        byte[] before = original.ToArray(); var preview = OriginalSourceText.Format(original);
        Assert.Equal("Subject: =?utf-8?Q?Hello?=\n\n<script>unchanged</script>\\\\path\t\\x00\\x1B\\x7F\\x80\\xFF",preview.Text);
        Assert.Equal(original.Length,preview.BytesShown); Assert.False(preview.IsTruncated); Assert.Equal(before,original);
    }
    [Fact]
    public void ByteViewHasExplicitInputOutputAndPrefixBounds()
    {
        var preview=OriginalSourceText.Format(new byte[MailMessageSource.MaximumBytes]);
        Assert.Equal(OriginalSourceText.MaximumPreviewBytes,preview.BytesShown); Assert.True(preview.IsTruncated); Assert.Equal(4*OriginalSourceText.MaximumPreviewBytes,preview.Text.Length);
        Assert.Empty(OriginalSourceText.Format([]).Text);
        Assert.Throws<ArgumentException>(()=>OriginalSourceText.Format(new byte[IStreamingMessageSourceExports.MaximumBytes+1]));
    }
    [Fact]
    public async Task VerifiedReadPreservesBytesAndRejectsForeignPathChangedLengthAndChangedChecksum()
    {
        using var directory=new TestDirectory(); directory.Create(); var store=new FileMessageSourceExports(directory.Root); var key=Key(); byte[] bytes=Encoding.ASCII.GetBytes("Subject: Exact\r\n\r\nBody");
        var saved=await store.SaveAsync(new(key,bytes)); Assert.Equal(bytes,(await store.ReadVerifiedAsync(saved)).Bytes);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.ReadVerifiedAsync(saved with{Path=directory.File("outside.eml")}));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.ReadVerifiedAsync(saved with{Bytes=IStreamingMessageSourceExports.MaximumBytes+1}));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.ReadVerifiedAsync(saved with{Sha256="invalid"}));
        File.WriteAllBytes(saved.Path,bytes.Append((byte)1).ToArray()); await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadVerifiedAsync(saved));
        byte[] changed=bytes.ToArray(); changed[0]^=1; File.WriteAllBytes(saved.Path,changed); await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadVerifiedAsync(saved));
        Assert.Equal(changed,File.ReadAllBytes(saved.Path));
    }
    [Fact]
    public async Task MissingAndCanceledReadNeverRecreatesOrOverwritesExport()
    {
        using var directory=new TestDirectory(); directory.Create(); var store=new FileMessageSourceExports(directory.Root); var saved=await store.SaveAsync(new(Key(),[65,66]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.ReadVerifiedAsync(saved,new(true))); File.Delete(saved.Path);
        await Assert.ThrowsAsync<FileNotFoundException>(()=>store.ReadVerifiedAsync(saved)); Assert.False(File.Exists(saved.Path));
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task LocalShowHideCopyAreExplicitBoundedReadOnlyAndDoNotReadClipboard(double scale)
    {
        using var directory=new TestDirectory(); directory.Create(); var key=Key(); var store=new FileMessageSourceExports(directory.Root); var saved=await store.SaveAsync(new(key,Encoding.ASCII.GetBytes("Subject: Read only\r\n\r\n<script>plain</script>")));
        var dispatcher=new TestQueueDispatcher(); var host=new Host(); using var inspection=new MessageSourceInspection(key,store,()=>true); inspection.SetExport(saved);
        using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(host); session.AddRoot(inspection);
        var theme=StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session,theme); StandardThemeController.ApplyToSubtree(inspection,theme); session.RenderFrame();
        Assert.False(inspection.IsPreviewVisible); Assert.Empty(inspection.PreviewText);
        await inspection.ReadPreviewAsync(); dispatcher.Drain(); session.RenderFrame(); Assert.True(inspection.IsPreviewVisible); Assert.Contains("<script>plain</script>",inspection.PreviewText);
        var text=All(inspection).OfType<ScrollableMessageText>().Single(); Assert.True(text.Editor.IsReadOnly); Assert.Same(text.Editor,session.FocusedElement); Assert.InRange(text.Bounds.Height,1,240);
        inspection.CopyPreview(); Assert.Equal(inspection.PreviewText,host.Copied); Assert.Equal(0,host.Reads);
        host.Fail=true; inspection.CopyPreview(); Assert.Contains("failed",inspection.Status); Assert.True(inspection.IsPreviewVisible);
        inspection.HidePreview(); Assert.Empty(inspection.PreviewText); Assert.False(inspection.IsPreviewVisible);
        Assert.Equal("Show saved source preview",Assert.IsType<StandardButton>(session.FocusedElement).Text);
        string? previous=host.Copied; inspection.CopyPreview(); Assert.Equal(previous,host.Copied);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("hide")]
    [InlineData("replace")]
    [InlineData("stale")]
    [InlineData("dispose")]
    public async Task LateUncancellableAndQueuedReadsCannotExposeOldSource(string mode)
    {
        var key=Key(); var reader=new HeldReader(); bool current=true; var dispatcher=new TestQueueDispatcher();
        using var inspection=new MessageSourceInspection(key,reader,()=>current); inspection.SetExport(Receipt(key));
        using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(inspection);
        var reading=inspection.ReadPreviewAsync(); await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode=="cancel")inspection.Cancel(); else if(mode=="hide")inspection.HidePreview(); else if(mode=="replace")inspection.SetExport(null); else if(mode=="stale")current=false; else inspection.Dispose();
        reader.Release.SetResult(new(key,Encoding.ASCII.GetBytes("Subject: private source"))); await reading; dispatcher.Drain();
        Assert.Empty(inspection.PreviewText); Assert.False(inspection.IsPreviewVisible);
    }
    [Fact]
    public async Task QueuedSuccessfulReadIsDiscardedAfterHideAndWrongKeyNeverReplacesPreview()
    {
        var key=Key(); var reader=new HeldReader(); var dispatcher=new TestQueueDispatcher();
        using var inspection=new MessageSourceInspection(key,reader,()=>true); inspection.SetExport(Receipt(key));
        using var session=new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(inspection);
        var reading=inspection.ReadPreviewAsync(); reader.Release.SetResult(new(key,Encoding.ASCII.GetBytes("Subject: queued"))); await reading;
        inspection.HidePreview(); dispatcher.Drain(); Assert.Empty(inspection.PreviewText); Assert.False(inspection.IsBusy);
        await inspection.ReadPreviewAsync(); dispatcher.Drain(); Assert.True(inspection.IsPreviewVisible);
        inspection.SetExport(Receipt(key)); reader.Release=new(TaskCreationOptions.RunContinuationsAsynchronously); reader.Release.SetResult(new(key with{Uid=2},[65]));
        await inspection.ReadPreviewAsync(); dispatcher.Drain(); Assert.False(inspection.IsPreviewVisible); Assert.Contains("another message",inspection.Status);
    }
    [Fact]
    public async Task ModifiedSavedSourceClearsPreviewWithoutChangingFileOrFetchingAnything()
    {
        using var directory=new TestDirectory(); directory.Create(); var key=Key(); var store=new FileMessageSourceExports(directory.Root); var saved=await store.SaveAsync(new(key,[65,66]));
        using var inspection=new MessageSourceInspection(key,store,()=>true); inspection.SetExport(saved); await inspection.ReadPreviewAsync(); Assert.Equal("AB",inspection.PreviewText);
        File.WriteAllBytes(saved.Path,[67,68]); await inspection.ReadPreviewAsync(); Assert.Empty(inspection.PreviewText); Assert.Contains("checksum",inspection.Status); Assert.Equal(new byte[]{67,68},File.ReadAllBytes(saved.Path));
    }
    private static MailMessageKey Key()=>new(AccountId.New(),"INBOX",7,1);
    private static MessageSourceExport Receipt(MailMessageKey key)=>new(key,"not-read-by-fixture.eml",1,new string('a',64));
    private static IEnumerable<UiElement> All(UiElement root){yield return root;foreach(var child in root.Children)foreach(var nested in All(child))yield return nested;}
    private sealed class HeldReader:IVerifiedMessageSourceExports
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<MailMessageSource> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MailMessageSource> ReadVerifiedAsync(MessageSourceExport saved,CancellationToken token=default){Entered.TrySetResult();return Release.Task;}
    }
    private sealed class Host:IUiHost,IUiClipboardHost
    {
        public string? Copied;public int Reads;public bool Fail;
        public BSize ViewportSize=>new(640,480);public double Scale=>1;public BRenderList CreateRenderList(int capacity=0)=>new(capacity);
        public void Invalidate(UiInvalidation value){}public void Present(BRenderList value){}
        public void SetText(string text){if(Fail)throw new InvalidOperationException("synthetic busy clipboard");Copied=text;}
        public bool TryGetText(out string text){Reads++;text="";throw new InvalidOperationException("No clipboard reads.");}
    }
}
