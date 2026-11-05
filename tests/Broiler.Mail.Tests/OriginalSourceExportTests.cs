using System.Security.Cryptography;
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
using Broiler.UI.Forms.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class OriginalSourceExportTests
{
    private static readonly byte[] Original = Encoding.ASCII.GetBytes("From: sender@example.test\r\nSubject: =?utf-8?Q?Original?=\r\nContent-Type: application/octet-stream\r\n\r\n").Concat(new byte[] { 0,255,128,13,10 }).ToArray();
    [Fact]
    public async Task ExportPreservesExactOctetsAndChecksumWithUniqueNonOverwritingNames()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new FileMessageSourceExports(directory.Root); var key = Key();
        var one = await store.SaveAsync(new(key, Original)); var two = await store.SaveAsync(new(key, Original));
        Assert.NotEqual(one.Path, two.Path); Assert.Equal(Original, File.ReadAllBytes(one.Path)); Assert.Equal(Original, File.ReadAllBytes(two.Path));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Original)).ToLowerInvariant(), one.Sha256); Assert.Equal(Original.Length, one.Bytes);
        Assert.Equal(store.ExportDirectory, Path.GetDirectoryName(one.Path)); Assert.EndsWith(".eml", one.Path); Assert.DoesNotContain("literal", one.Path); Assert.Empty(Directory.GetFiles(store.ExportDirectory, "*.part"));
    }
    [Fact]
    public async Task InvalidOrCanceledSourceNeverPublishesAFile()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new FileMessageSourceExports(directory.Root); var key = Key();
        foreach (var source in new[] { new MailMessageSource(key, []), new(key, new byte[MailMessageSource.MaximumBytes + 1]), new(key with { Uid = 0 }, Original), new(key with { AccountId = default }, Original), new(key with { MailboxId = "\ud800" }, Original) })
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(source));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(key, Original), new(true)));
        Assert.False(Directory.Exists(store.ExportDirectory));
    }
    [Fact]
    public async Task ConcurrentWritersRespectFileLimitAndDoNotEvictExistingExports()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new FileMessageSourceExports(directory.Root); Directory.CreateDirectory(store.ExportDirectory);
        for (int i = 0; i < 63; i++) File.WriteAllText(Path.Combine(store.ExportDirectory, i + ".eml"), "retained");
        var attempts = await Task.WhenAll(Enumerable.Range(0,2).Select(async _ => { try { await new FileMessageSourceExports(directory.Root).SaveAsync(new(Key(), Original)); return true; } catch (IOException) { return false; } }));
        Assert.Single(attempts, value => value); Assert.Equal(64, Directory.GetFiles(store.ExportDirectory).Length); Assert.Equal("retained", File.ReadAllText(Path.Combine(store.ExportDirectory,"0.eml")));
    }
    [Fact]
    public async Task CallerMutationWhileWaitingForLockCannotRewriteExportSnapshot()
    {
        using var directory = new TestDirectory(); directory.Create(); var store = new FileMessageSourceExports(directory.Root); byte[] mutable = Original.ToArray();
        var lease = new FileStream(directory.File("source-exports.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var saving = store.SaveAsync(new(Key(), mutable)); mutable[0] = 0; lease.Dispose(); var result = await saving; Assert.Equal(Original, File.ReadAllBytes(result.Path));
    }
    [Fact]
    public async Task CacheCleanupRetainsExportsWhileVerifiedProfileBackupsExcludeThem()
    {
        using var directory = new TestDirectory(); directory.Create(); using var profile = MailProfile.Open(directory.File("profile")); var store = new FileMessageSourceExports(profile.DirectoryPath);
        var saved = await store.SaveAsync(new(Key(), Original)); var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        var cleanup = await storage.ClearDownloadedMailAsync(directory.File("backup")); Assert.Equal(Original, File.ReadAllBytes(saved.Path));
        Assert.False(Directory.Exists(Path.Combine(cleanup.BackupDirectory, "source-exports")));
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ExportControlsAreExplicitCopyableAndKeepSafeFocus(double scale)
    {
        using var directory = new TestDirectory(); directory.Create(); var account = TestDirectory.Profile(); var receiver = new SourceReceiver(new(account.Id, "INBOX",7,1), Original); var dispatcher = new TestQueueDispatcher();
        using var actions = new MessageSourceExportActions(account, receiver.Key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true);
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(actions);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(actions, theme); session.RenderFrame();
        var section = All(actions).OfType<FormSection>().Single(); Assert.False(section.IsExpanded); section.Toggle!.Click(); session.RenderFrame(); Assert.Equal(0, receiver.Calls);
        var save = All(actions).OfType<StandardButton>().Single(button => button.Text == "Download original .eml"); Assert.False(save.IsDefault);
        await actions.ExportAsync(); dispatcher.Drain(); Assert.Equal(1, receiver.Calls); Assert.False(actions.IsBusy);
        var path = All(actions).OfType<StandardRichEdit>().Single(editor => editor.AccessibleName == "Saved original source path and checksum"); Assert.Contains(".eml", path.GetPlainText()); Assert.Contains("SHA-256", path.GetPlainText()); Assert.True(path.IsReadOnly); Assert.Same(save, session.FocusedElement);
        Assert.Contains("not opened", actions.Status); Assert.Single(Directory.GetFiles(Path.Combine(directory.Root,"source-exports"),"*.eml"));
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("stale")]
    [InlineData("unavailable")]
    public async Task LateUncancellableSourceCannotPublishAfterContextLoss(string mode)
    {
        using var directory = new TestDirectory(); directory.Create(); var account = TestDirectory.Profile(); var receiver = new SourceReceiver(new(account.Id,"INBOX",7,1),Original) { Hold = true }; bool current = true, available = true;
        using var actions = new MessageSourceExportActions(account, receiver.Key, receiver, new FileMessageSourceExports(directory.Root), () => current, () => available);
        var task = actions.ExportAsync(); await receiver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode == "cancel") actions.Cancel(); else if (mode == "dispose") actions.Dispose(); else if (mode == "stale") current = false; else { available = false; actions.RefreshAvailability(); }
        receiver.Release.SetResult(); await task; Assert.False(Directory.Exists(Path.Combine(directory.Root,"source-exports")));
    }
    [Fact]
    public async Task WrongMessageAndStorageFailureKeepReaderAliveAndAllowExplicitRetry()
    {
        using var directory = new TestDirectory(); directory.Create(); var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id,"INBOX",7,1); var receiver = new SourceReceiver(key with { Uid = 2 }, Original);
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true);
        await actions.ExportAsync(); Assert.Contains("another message", actions.Status); Assert.False(actions.IsBusy); Assert.False(Directory.Exists(Path.Combine(directory.Root,"source-exports")));
        receiver.Key = key; File.WriteAllText(directory.File("source-exports"), "blocked directory"); await actions.ExportAsync(); Assert.Contains("did not finish", actions.Status); Assert.False(actions.IsBusy);
        File.Delete(directory.File("source-exports")); await actions.ExportAsync(); Assert.Contains("not opened", actions.Status);
    }
    private static MailMessageKey Key() => new(AccountId.New(), "../literal-folder",7,1);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }
    private sealed class SourceReceiver(MailMessageKey key, byte[] bytes) : IMailSourceReceiver
    {
        public MailMessageKey Key = key; public int Calls; public bool Hold;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey message, CancellationToken token = default)
        { Calls++; Entered.TrySetResult(); if (Hold) await Release.Task; return new(Key, bytes); }
    }
    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(640,480); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList renderList) { }
    }
}
