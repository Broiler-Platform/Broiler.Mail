// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Persistence;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.4: the composer attaches picked files, lists each with its size and Remove, and keeps the quota in sight.</summary>
[Collection("UI theme")]
public sealed class ComposerAttachmentTests
{
    private const int MiB = 1024 * 1024;

    [Fact]
    public void AttachFileButtonReplacesPathEditAndComboBox()
    {
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();

        var attach = fixture.Button("Attach file…");
        Assert.Equal("Attach file", attach.GetSemanticNode().Name);
        Assert.True(attach.IsEnabled);
        Assert.True(attach.Bounds.Width > 0);
        // No typed path, no combo box of attachments, no disclosure to open first.
        Assert.DoesNotContain(fixture.Descendants().OfType<StandardLabel>(), label => label.Text == ComposerAttachmentsBar.TypedPathHint);
        Assert.DoesNotContain(fixture.Descendants().OfType<StandardEdit>(), edit => edit.AccessibleName == ComposerAttachmentsBar.PathLabel);
        Assert.Equal(4, fixture.Descendants().OfType<StandardEdit>().Count());
        Assert.DoesNotContain(fixture.Descendants().OfType<StandardComboBox>(), combo => combo.AccessibleName == "Draft attachments");
        Assert.DoesNotContain(fixture.Descendants().OfType<FormSection>(), section => section.GetSemanticNode().Name == "Attachments");
        Assert.DoesNotContain(fixture.Descendants().OfType<StandardButton>(), button => button.Text is "Add attachment" or "Remove selected attachment");
        Assert.Equal(ComposerAttachmentsBar.EmptyQuotaText, fixture.Quota.Text);
        // The accessible name says the quota itself, not only what the line is.
        Assert.Equal("Attachment quota: " + ComposerAttachmentsBar.EmptyQuotaText, fixture.Quota.GetSemanticNode().Name);

        // The bar sits after Subject and before the message.
        var subject = fixture.Descendants().OfType<FormField>().Single(field => field.Label.Text == "Subject");
        Assert.True(subject.Bounds.Bottom <= fixture.Bar.Bounds.Top + 0.5);
        Assert.True(fixture.Bar.Bounds.Bottom <= fixture.Body.Bounds.Top + 0.5);

        // Without a draft there is nothing to attach to.
        _ = fixture.Composer.DiscardAsync();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.HasDraft && !fixture.Composer.IsBusy);
        Assert.Equal(UiVisibility.Collapsed, fixture.Bar.Visibility);
    }

    [Fact]
    public async Task PickedFileIsCopiedIntoDraftAndListedWithSizeAndRemove()
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("report.pdf");
        byte[] bytes = Enumerable.Range(0, 1_258_291).Select(value => (byte)(value * 7)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var store = new JsonDraftStore(directory.File("drafts.json"));
        var picker = new FakeMailFilePicker().WillOpen(path);
        using (var fixture = new Fixture(picker, store))
        {
            Assert.True(fixture.Composer.StartNew());
            fixture.Render();
            fixture.Session.SetFocus(fixture.Button("Attach file…"));
            fixture.Button("Attach file…").Click();
            fixture.Dispatcher.DrainUntil(() => fixture.Composer.Attachments.Count == 1 && fixture.Bar.RemoveButtons.Count == 1 && fixture.Session.FocusedElement == fixture.Bar.RemoveButtons[0]);

            var request = Assert.Single(picker.Requests);
            Assert.Equal(("open", MailFilePurpose.Attachment, "Attach file"), (request.Kind, request.Request.Purpose, request.Request.Title));
            var part = Assert.Single(fixture.Composer.Attachments);
            Assert.Equal("report.pdf", part.FileName);
            Assert.Equal(bytes, part.Content.ToArray());
            Assert.Contains(fixture.Descendants().OfType<StandardLabel>(), label => label.Text == "report.pdf · 1.2 MiB");
            Assert.Equal("Remove report.pdf", fixture.Bar.RemoveButtons[0].GetSemanticNode().Name);
            Assert.Equal("1.2 / 8.0 MiB · 1/4 files", fixture.Quota.Text);
            Assert.Equal("Attachment quota: 1.2 / 8.0 MiB · 1/4 files", fixture.Quota.GetSemanticNode().Name);
            Assert.Equal("1.2 / 8.0 MiB · 1/4 files", fixture.Composer.AttachmentQuotaText);
            Assert.Equal(DraftAttachment.MaximumBytes - bytes.Length, fixture.Composer.AttachmentRoomBytes);
            Assert.Equal(bytes.Length, fixture.Composer.AttachmentBytes);
            Assert.True(fixture.SaveAndDrain());
        }

        // The copy belongs to the draft: it survives a reopen even after the original file is gone.
        File.Delete(path);
        using var reopened = new ComposerViewModel(store, await store.LoadAsync());
        reopened.SetAccount(TestDirectory.Profile());
        var restored = Assert.Single(reopened.Attachments);
        Assert.Equal("report.pdf", restored.FileName);
        Assert.Equal(bytes, restored.Content.ToArray());
        Assert.Equal("1.2 / 8.0 MiB · 1/4 files", reopened.AttachmentQuotaText);
    }

    [Fact]
    public void CanceledPickerChangesNothingAndKeepsFocusOnAttachFile()
    {
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();
        var attach = fixture.Button("Attach file…");
        fixture.Session.SetFocus(attach);
        var before = fixture.Composer.Attachments;
        attach.Click();
        fixture.Render();

        Assert.Same(before, fixture.Composer.Attachments);
        Assert.Same(attach, fixture.Session.FocusedElement);
        Assert.False(fixture.Composer.IsAddingAttachment);
        Assert.DoesNotContain(fixture.Feedback, line => line.Kind is FeedbackKind.Warning or FeedbackKind.Error);

        // A rejected pick says why, and changes nothing either.
        var picker = (FakeMailFilePicker)fixture.Files;
        picker.OpenResults.Enqueue(MailFilePick<IMailReadableFile>.Rejected("That file cannot be opened."));
        attach.Click();
        fixture.Render();
        Assert.Same(before, fixture.Composer.Attachments);
        Assert.Contains(fixture.Feedback, line => line.Kind == FeedbackKind.Warning && line.Message == "That file cannot be opened.");
        Assert.Same(attach, fixture.Session.FocusedElement);
    }

    [Fact]
    public void OversizedFileIsRefusedBeforeReading()
    {
        var oversized = new TestReadableFile("video.mp4", new byte[16], length: 9L * MiB);
        var empty = new TestReadableFile("empty.txt", [], length: 0);
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();

        fixture.Add(oversized);
        Assert.Equal(0, oversized.Opened);
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("video.mp4 is 9.0 MiB; this draft has 8.0 MiB left (8 MiB per draft).", fixture.Composer.Status);
        Assert.Equal(FeedbackKind.Warning, fixture.Composer.StatusKind);

        fixture.Add(empty);
        Assert.Equal(0, empty.Opened);
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("empty.txt is empty.", fixture.Composer.Status);
    }

    [Fact]
    public void UnknownLengthStreamOverQuotaIsRefused()
    {
        var file = new TestReadableFile("stream.bin", new byte[DraftAttachment.MaximumBytes + 600_000], length: null);
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();

        fixture.Add(file);
        Assert.Equal(1, file.Opened);
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("stream.bin is larger than the 8.0 MiB this draft has left (8 MiB per draft).", fixture.Composer.Status);
        // Reading stops one byte past the room left.
        Assert.Equal(DraftAttachment.MaximumBytes + 1, file.BytesRead);

        // A stream of unknown length that fits is added, with its exact bytes.
        byte[] small = [1, 2, 3, 4, 5];
        fixture.Add(new TestReadableFile("small.bin", small, length: null));
        Assert.Equal(small, Assert.Single(fixture.Composer.Attachments).Content.ToArray());
    }

    [Fact]
    public void CancelDuringReadLeavesDraftUnchanged()
    {
        var file = new TestReadableFile("slow.bin", new byte[3 * ComposerViewModel.AttachmentReadChunkBytes], length: 3 * ComposerViewModel.AttachmentReadChunkBytes) { Gated = true };
        var picker = new FakeMailFilePicker();
        picker.OpenResults.Enqueue(MailFilePick<IMailReadableFile>.Picked(file));
        using var fixture = new Fixture(picker);
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();
        var attach = fixture.Button("Attach file…");
        fixture.Session.SetFocus(attach);
        attach.Click();
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.IsAddingAttachment && file.BytesRead > 0);
        fixture.Render();

        // While the file is read, focus is on Cancel and the progress names the file.
        var cancel = fixture.Descendants().OfType<StandardButton>().Single(button => button.AccessibleName == "Cancel adding attachment");
        Assert.Same(cancel, fixture.Session.FocusedElement);
        Assert.Equal(UiVisibility.Visible, cancel.Parent!.Visibility);
        Assert.Contains(fixture.Feedback, line => line.Kind == FeedbackKind.Progress && line.Message.StartsWith("Reading slow.bin", StringComparison.Ordinal));
        // The status line below the form no longer repeats the progress.
        Assert.Single(fixture.Feedback, line => line.Message.StartsWith("Reading slow.bin", StringComparison.Ordinal));

        cancel.Click();
        file.Release();
        fixture.Dispatcher.DrainUntil(() => !fixture.Composer.IsAddingAttachment && fixture.Session.FocusedElement == attach);
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("Attachment not added; reading was canceled.", fixture.Composer.Status);
        Assert.Equal(UiVisibility.Collapsed, cancel.Parent!.Visibility);
    }

    [Fact]
    public void FifthFileAndFullQuotaDisableAttachWithReason()
    {
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();
        for (int index = 0; index < DraftAttachment.MaximumCount; index++)
            fixture.Add(new TestReadableFile($"file{index}.txt", [(byte)index, 1, 2], length: 3));
        fixture.Render();

        var attach = fixture.Button("Attach file…");
        Assert.Equal(4, fixture.Composer.Attachments.Count);
        Assert.False(attach.IsEnabled);
        Assert.False(fixture.Composer.CanAddAttachment);
        string reason = Assert.IsType<string>(fixture.Composer.AttachmentLimitReason);
        Assert.Contains("4 files", reason);
        Assert.Equal(reason, attach.ToolTipText);
        Assert.Contains(fixture.Descendants().OfType<StandardLabel>(), label => label.Text == reason && label.Visibility == UiVisibility.Visible);
        Assert.Equal("0.1 / 8.0 MiB · 4/4 files", fixture.Quota.Text);

        // Removing one makes room again.
        fixture.Bar.RemoveButtons[0].Click();
        fixture.Render();
        Assert.True(attach.IsEnabled);
        Assert.Null(fixture.Composer.AttachmentLimitReason);

        // A draft whose attachments use all 8 MiB takes no more files either.
        foreach (var part in fixture.Composer.Attachments.ToArray()) fixture.Composer.RemoveAttachment(part);
        fixture.Add(new TestReadableFile("full.bin", new byte[DraftAttachment.MaximumBytes], length: DraftAttachment.MaximumBytes));
        fixture.Render();
        Assert.Single(fixture.Composer.Attachments);
        Assert.False(attach.IsEnabled);
        Assert.Contains("8 MiB", fixture.Composer.AttachmentLimitReason);
        Assert.Equal("8.0 / 8.0 MiB · 1/4 files", fixture.Quota.Text);
        Assert.Equal(0, fixture.Composer.AttachmentRoomBytes);
    }

    [Fact]
    public void RemoveMovesFocusToNextRemoveThenAttachFile()
    {
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt" }) fixture.Add(new TestReadableFile(name, [1, 2], length: 2));
        fixture.Render();
        StandardButton Remove(string name) => fixture.Bar.RemoveButtons.Single(button => button.AccessibleName == $"Remove {name}");

        // The next file's Remove takes focus.
        fixture.Session.SetFocus(Remove("b.txt"));
        Remove("b.txt").Click();
        fixture.Render();
        Assert.Equal(["a.txt", "c.txt"], fixture.Composer.Attachments.Select(part => part.FileName));
        Assert.Same(Remove("c.txt"), fixture.Session.FocusedElement);

        // The last row has no next one, so the previous Remove takes focus.
        Remove("c.txt").Click();
        fixture.Render();
        Assert.Same(Remove("a.txt"), fixture.Session.FocusedElement);

        // With none left, focus returns to Attach file….
        Remove("a.txt").Click();
        fixture.Render();
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Same(fixture.Button("Attach file…"), fixture.Session.FocusedElement);
        Assert.Equal(ComposerAttachmentsBar.EmptyQuotaText, fixture.Quota.Text);

        // The index overload removes the same entry as the reference one.
        foreach (string name in new[] { "x.txt", "y.txt" }) fixture.Add(new TestReadableFile(name, [1], length: 1));
        fixture.Composer.RemoveAttachment(0);
        Assert.Equal("y.txt", Assert.Single(fixture.Composer.Attachments).FileName);
        fixture.Composer.RemoveAttachment(new DraftAttachment("y.txt", new byte[] { 1 }));
        Assert.Single(fixture.Composer.Attachments);
    }

    [Theory]
    [InlineData("a:b😀.txt", "a_b_.txt")]
    [InlineData(@"C:\Users\someone\report.pdf", "report.pdf")]
    [InlineData("/home/someone/notes.md", "notes.md")]
    [InlineData("tab\there.txt", "tab_here.txt")]
    [InlineData("trailing. . ", "trailing")]
    [InlineData("..", DraftAttachment.FallbackFileName)]
    [InlineData("", DraftAttachment.FallbackFileName)]
    [InlineData("folder/", DraftAttachment.FallbackFileName)]
    [InlineData("Bericht – März.pdf", "Bericht – März.pdf")]
    public void EmojiAndColonFileNamesAreSanitizedForDrafts(string name, string expected)
    {
        string safe = DraftAttachment.SafeName(name);
        Assert.Equal(expected, safe);
        DraftAttachment.Validate([new DraftAttachment(safe, new byte[] { 1 })]);
    }

    [Fact]
    public void LongFileNamesAreShortenedKeepingTheExtension()
    {
        string safe = DraftAttachment.SafeName(new string('n', 300) + ".pdf");
        Assert.Equal(DraftAttachment.MaximumFileNameLength, safe.Length);
        Assert.EndsWith(".pdf", safe);
        string wide = DraftAttachment.SafeName(new string('ä', 150) + ".txt");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(wide) <= DraftAttachment.MaximumFileNameBytes);
        Assert.EndsWith(".txt", wide);
        foreach (string name in new[] { safe, wide }) DraftAttachment.Validate([new DraftAttachment(name, new byte[] { 1 })]);

        // A picked file gets the same safe name in the draft.
        using var fixture = new Fixture(new FakeMailFilePicker());
        Assert.True(fixture.Composer.StartNew());
        fixture.Add(new TestReadableFile("a:b😀.txt", [1, 2, 3], length: 3));
        Assert.Equal("a_b_.txt", Assert.Single(fixture.Composer.Attachments).FileName);
    }

    [Fact]
    public void DraftSwitchDuringReadDiscardsResult()
    {
        // The draft is replaced while the picker is open: nothing is attached to the new one.
        var picker = new PendingPicker();
        using (var fixture = new Fixture(picker))
        {
            Assert.True(fixture.Composer.StartNew());
            fixture.Render();
            fixture.Button("Attach file…").Click();
            _ = fixture.Composer.DiscardAsync();
            fixture.Dispatcher.DrainUntil(() => !fixture.Composer.HasDraft && !fixture.Composer.IsBusy);
            Assert.True(fixture.Composer.StartNew());
            var newDraft = fixture.Composer.DraftId;
            picker.Complete(MailFilePick<IMailReadableFile>.Picked(new TestReadableFile("late.txt", [1], length: 1)));
            fixture.Dispatcher.DrainUntil(() => fixture.Feedback.Any(line => line.Kind == FeedbackKind.Warning));
            Assert.Equal(newDraft, fixture.Composer.DraftId);
            Assert.Empty(fixture.Composer.Attachments);
            Assert.Contains(fixture.Feedback, line => line.Message.Contains("Nothing was attached", StringComparison.Ordinal));
        }

        // Profile storage takes the draft while the file is read: the bytes read are discarded.
        var file = new TestReadableFile("held.bin", new byte[1024], length: 1024) { Gated = true };
        using var reading = new Fixture(new FakeMailFilePicker());
        Assert.True(reading.Composer.StartNew());
        var add = reading.Composer.AddAttachmentAsync(file);
        reading.Dispatcher.DrainUntil(() => reading.Composer.IsAddingAttachment && file.Opened == 1);
        reading.Composer.SetProfileStorageBusy(true);
        file.Release();
        reading.Dispatcher.DrainUntil(() => add.IsCompleted);
        reading.Composer.SetProfileStorageBusy(false);
        Assert.Empty(reading.Composer.Attachments);
        Assert.Equal("The draft context changed while reading the attachment.", reading.Composer.Status);
    }

    [Fact]
    public void ANoPickerDeviceKeepsTheTypedPathFallback()
    {
        using var directory = new TestDirectory();
        directory.Create();
        string path = directory.File("typed.txt");
        File.WriteAllBytes(path, [9, 8, 7]);
        using var fixture = new Fixture(UnavailableMailFilePicker.NotOnThisDeviceYet);
        Assert.True(fixture.Composer.StartNew());
        fixture.Render();

        // No Attach file…, which could only fail; the hint says why and the typed path still works.
        Assert.DoesNotContain(fixture.Descendants().OfType<StandardButton>(), button => button.Text == "Attach file…");
        Assert.Contains(fixture.Descendants().OfType<StandardLabel>(), label => label.Text == ComposerAttachmentsBar.TypedPathHint && label.Bounds.Width > 0);
        var edit = fixture.Descendants().OfType<StandardEdit>().Single(item => item.AccessibleName == ComposerAttachmentsBar.PathLabel);
        Assert.Equal(ComposerAttachmentsBar.PathPlaceholder, edit.PlaceholderText);
        var add = fixture.Button("Add attachment");
        Assert.False(add.IsEnabled);

        edit.Text = "relative.txt";
        add.Click();
        fixture.Render();
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Equal("Could not add relative.txt. Type the full path of a regular file on this device.", fixture.Composer.Status);

        edit.Text = directory.Root;
        add.Click();
        fixture.Render();
        Assert.Empty(fixture.Composer.Attachments);

        edit.Text = path;
        add.Click();
        fixture.Dispatcher.DrainUntil(() => fixture.Composer.Attachments.Count == 1 && fixture.Bar.RemoveButtons.Count == 1 && fixture.Session.FocusedElement == fixture.Bar.RemoveButtons[0]);
        Assert.Equal("typed.txt", fixture.Composer.Attachments[0].FileName);
        Assert.Equal(new byte[] { 9, 8, 7 }, fixture.Composer.Attachments[0].Content.ToArray());
        Assert.Equal("", edit.Text);
        Assert.Contains(fixture.Descendants().OfType<StandardLabel>(), label => label.Text == "typed.txt · 3 bytes");

        // Remove works the same way, and focus goes back to the path field.
        fixture.Bar.RemoveButtons[0].Click();
        fixture.Render();
        Assert.Empty(fixture.Composer.Attachments);
        Assert.Same(edit, fixture.Session.FocusedElement);
    }

    [Fact]
    public void SavedDraftsLeaveLessRoomAndTheQuotaLineSaysSo()
    {
        var account = TestDirectory.Profile();
        var saved = new DraftSnapshot
        {
            Draft = new MailDraft
            {
                AccountId = account.Id, FromAddress = account.EmailAddress, Subject = "Parked",
                Attachments = [new DraftAttachment("big.bin", new byte[DraftAttachment.MaximumBytes]), new DraftAttachment("more.bin", new byte[6 * MiB])],
            },
            ToText = "", CcText = "", BccText = "", State = DraftSubmissionState.Editing,
        };
        var store = new MemoryDraftStore();
        var initial = new DraftStoreState(0, null) { SavedDrafts = [saved] };
        using var composer = new ComposerViewModel(store, initial, new ImmediateUiDispatcher());
        composer.SetAccount(account);
        Assert.True(composer.StartNew());
        Assert.Equal(2 * MiB, composer.AttachmentRoomBytes);
        Assert.Equal("0.0 / 8.0 MiB · 0/4 files · 2.0 MiB left for saved drafts", composer.AttachmentQuotaText);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(IMailFilePicker files, IDraftStore? store = null, int width = 1100, int height = 720)
        {
            Files = files;
            Account = TestDirectory.Profile();
            Dispatcher = new TestQueueDispatcher();
            Composer = new ComposerViewModel(store ?? new MemoryDraftStore(), dispatcher: Dispatcher);
            Composer.SetAccount(Account);
            Inbox = new InboxViewModel(new TestMailReceiver(), Dispatcher);
            Inbox.SetAccount(Account);
            View = new ComposerView(Composer, Inbox, files: files);
            Surface = (FormSurface)View.CreateContent();
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host(width, height));
            Session.AddRoot(Surface);
            Render();
        }

        public IMailFilePicker Files { get; }
        public AccountProfile Account { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public ComposerViewModel Composer { get; }
        public InboxViewModel Inbox { get; }
        public ComposerView View { get; }
        public UiSession Session { get; }
        public FormSurface Surface { get; }
        public ComposerAttachmentsBar Bar => View.Attachments!;
        public StandardLabel Quota => Descendants().OfType<StandardLabel>().Single(label => label.AccessibleName?.StartsWith("Attachment quota", StringComparison.Ordinal) == true);
        public UiElement Body => View.Body!;
        public IEnumerable<InlineFeedback> Feedback => Descendants().OfType<InlineFeedback>().Where(item => item.Message.Length > 0);
        public StandardButton Button(string text) => Descendants().OfType<StandardButton>().Single(button => button.Text == text);

        public IEnumerable<UiElement> Descendants() => Walk(Surface);

        /// <summary>Adds <paramref name="file"/> through the view model, running its posted commit.</summary>
        public void Add(IMailReadableFile file)
        {
            var add = Composer.AddAttachmentAsync(file);
            Dispatcher.DrainUntil(() => add.IsCompleted);
            add.GetAwaiter().GetResult();
            Render();
        }

        public bool SaveAndDrain()
        {
            var save = Composer.SaveAsync();
            Dispatcher.DrainUntil(() => save.IsCompleted);
            return save.GetAwaiter().GetResult();
        }

        public void Render()
        {
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            Surface.Dispose();
            Inbox.Dispose();
            Composer.Dispose();
        }

        private static IEnumerable<UiElement> Walk(UiElement root)
        {
            yield return root;
            foreach (var child in root.Children)
                foreach (var item in Walk(child)) yield return item;
        }
    }

    /// <summary>A picked file with scripted bytes and length; a gated one waits before each read until released.</summary>
    internal sealed class TestReadableFile(string name, byte[] content, long? length) : IMailReadableFile
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _read;
        public string DisplayName => name;
        public string? LocalPath => null;
        public long? Length => length;
        public bool Gated { get; init; }
        public int Opened { get; private set; }
        public long BytesRead => Interlocked.Read(ref _read);
        public void Release() => _gate.TrySetResult();

        public Task<Stream> OpenReadAsync(CancellationToken token = default)
        {
            Opened++;
            return Task.FromResult<Stream>(new Reader(this));
        }

        private sealed class Reader(TestReadableFile file) : Stream
        {
            private int _position;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                // A gated file hands out its first chunk at once, then waits, so a read is seen running.
                if (file.Gated && _position > 0) await file._gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                int count = Math.Min(buffer.Length, file.Content.Length - _position);
                file.Content.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                Interlocked.Add(ref file._read, count);
                return count;
            }
        }

        private byte[] Content => content;
    }

    /// <summary>A picker whose open pick completes only when the test says so.</summary>
    private sealed class PendingPicker : IMailFilePicker
    {
        private readonly TaskCompletionSource<MailFilePick<IMailReadableFile>> _open = new();
        public MailFilePickerCapabilities Capabilities => MailFilePickerCapabilities.OpenFile;
        public void Complete(MailFilePick<IMailReadableFile> result) => _open.SetResult(result);
        public Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default) => _open.Task;
        public Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default) =>
            Task.FromResult(MailFilePick<IMailWritableFile>.Canceled);
        public Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default) =>
            Task.FromResult(MailFilePick<IMailPickedDirectory>.Canceled);
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
