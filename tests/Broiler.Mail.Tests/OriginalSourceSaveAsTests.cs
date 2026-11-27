// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// UX-03.5d: the reader's Save as .eml… streams the exact original to a file the person chooses, without a
/// saved-originals receipt, without marking the message read, and leaves nothing behind when it does not finish.
/// </summary>
[Collection("UI theme")]
public sealed class OriginalSourceSaveAsTests
{
    private static readonly byte[] Original = Encoding.ASCII.GetBytes(
        "From: sender@example.test\r\nSubject: Saved copy\r\nContent-Type: text/plain\r\n\r\nExact bytes.\r\n");

    [Fact]
    public async Task SaveAsStreamsToChosenFileWithoutUsingACatalogSlot()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, Encoding.ASCII.GetString(Original)));
        var account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(account, MailProtocol.Imap), LocalImapServer.Password);
        var receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(10));
        using var directory = new TestDirectory(); directory.Create();
        var exports = new FileMessageSourceExports(directory.Root);
        string target = directory.File("Saved copy 2026-03-09.eml");
        var files = new FakeMailFilePicker().WillSave(target);
        using var actions = new MessageSourceExportActions(account, new(account.Id, "INBOX", 7, 1), receiver, exports, () => true, () => true,
            files, "Saved copy 2026-03-09.eml");

        Assert.True(actions.CanSaveAs);
        Assert.True(await actions.SaveAsAsync());

        var (kind, request) = Assert.Single(files.Requests);
        Assert.Equal("save", kind);
        Assert.Equal(MailFilePurpose.SaveMessage, request.Purpose);
        Assert.Equal("Saved copy 2026-03-09.eml", request.SuggestedFileName);
        Assert.Equal([MailFileType.Email], request.Types);
        Assert.Equal("Save", request.AcceptText);
        byte[] saved = File.ReadAllBytes(target);
        Assert.Equal(Original, saved);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(saved)).ToLowerInvariant(), actions.LastSaved!.Value.Sha256);
        Assert.Equal("Message saved as Saved copy 2026-03-09.eml. The file was not opened.", actions.Status);
        // One peek at the original; the message stays unread and no saved-originals slot or receipt is used.
        Assert.Single(server.MailCommands, command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("BODY[", StringComparison.Ordinal) || command.Contains("STORE", StringComparison.Ordinal));
        Assert.False(Assert.Single(server.Messages).IsRead);
        Assert.False(Directory.Exists(exports.ExportDirectory));
        Assert.Empty(await exports.ListAccountExportsAsync(account.Id));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }

    [Fact]
    public async Task SaveAsWithABufferedReceiverWritesTheSameBytes()
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var receiver = new BufferedReceiver(key, Original);
        string target = directory.File("copy.eml");
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            new FakeMailFilePicker().WillSave(target), "copy.eml");
        Assert.True(await actions.SaveAsAsync());
        Assert.Equal(Original, File.ReadAllBytes(target));
        Assert.Equal(1, receiver.Calls);

        // Another identity is never written.
        receiver.Key = key with { Uid = 2 };
        string other = directory.File("other.eml");
        using var wrong = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            new FakeMailFilePicker().WillSave(other), "other.eml");
        Assert.False(await wrong.SaveAsAsync());
        Assert.Contains("another message identity", wrong.Status);
        Assert.False(File.Exists(other));
    }

    [Fact]
    public async Task SaveAsCanceledMidStreamLeavesNoFile()
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, destination, _, progress, token) =>
            {
                await destination.WriteAsync(Original.AsMemory(0, 10), token);
                progress?.Invoke(10, Original.Length);
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return Original.Length;
            },
        };
        string target = directory.File("partial.eml");
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            new FakeMailFilePicker().WillSave(target), "partial.eml");
        var saving = actions.SaveAsAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(actions.IsBusy);
        Assert.False(actions.CanSaveAs);
        actions.Cancel();

        Assert.False(await saving);
        Assert.Equal(MessageSourceExportActions.SaveAsCanceledText, actions.Status);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
        Assert.False(actions.IsBusy);
        Assert.Null(actions.LastSaved);
    }

    [Theory]
    [InlineData("pick")]
    [InlineData("write")]
    public async Task SaveAsAbortsWhenSelectionChangesDuringPick(string when)
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        bool current = true;
        var receiver = TestSourceReceiver.Returning(Original);
        if (when == "write")
        {
            var copy = receiver.CopySource!;
            receiver.CopySource = async (message, destination, maximum, progress, token) =>
            {
                int size = await copy(message, destination, maximum, progress, token);
                current = false;
                return size;
            };
        }
        string target = directory.File("changed.eml");
        var picker = new HookPicker(new FakeMailFilePicker().WillSave(target), () => { if (when == "pick") current = false; });
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => current, () => true,
            picker, "changed.eml");

        Assert.False(await actions.SaveAsAsync());
        Assert.Equal(MessageSourceExportActions.SaveAsChangedText, actions.Status);
        Assert.Equal(when == "pick" ? 0 : 1, receiver.CopySourceCalls);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }

    [Fact]
    public async Task CanceledOrRejectedPickWritesNothingAndOversizedOriginalsAreRefused()
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var receiver = new TestSourceReceiver
        {
            // A server that ignores the limit is still stopped one byte past it.
            CopySource = async (_, destination, _, _, token) =>
            {
                var chunk = new byte[1024 * 1024];
                for (int written = 0; written <= IStreamingMessageSourceExports.MaximumBytes; written += chunk.Length)
                    await destination.WriteAsync(chunk, token);
                return IStreamingMessageSourceExports.MaximumBytes + chunk.Length;
            },
        };
        var files = new FakeMailFilePicker();
        string target = directory.File("large.eml");
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            files, "large.eml");

        Assert.False(await actions.SaveAsAsync());
        Assert.Equal("No file chosen. Nothing was saved.", actions.Status);
        files.SaveResults.Enqueue(MailFilePick<IMailWritableFile>.Rejected("Broiler Mail never replaces files."));
        Assert.False(await actions.SaveAsAsync());
        Assert.Equal("Broiler Mail never replaces files. Nothing was saved.", actions.Status);
        Assert.Equal(0, receiver.CopySourceCalls);

        files.WillSave(target);
        Assert.False(await actions.SaveAsAsync());
        Assert.StartsWith("Message not saved. ", actions.Status);
        Assert.Contains("12 MiB", actions.Status);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }

    [Theory]
    [InlineData("saved")]
    [InlineData("too large")]
    [InlineData("canceled")]
    [InlineData("other size")]
    public async Task SaveAsReleasesItsHashingStreamOnEveryWayOut(string outcome)
    {
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        Stream? seen = null;
        var receiver = new TestSourceReceiver
        {
            CopySource = async (_, destination, maximum, _, token) =>
            {
                seen = destination;
                switch (outcome)
                {
                    case "too large": await destination.WriteAsync(new byte[maximum + 1], token); break;
                    case "canceled": throw new OperationCanceledException(token);
                    default: await destination.WriteAsync(Original, token); break;
                }
                return outcome == "other size" ? Original.Length + 1 : Original.Length;
            },
        };
        string target = directory.File("released.eml");
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            new FakeMailFilePicker().WillSave(target), "released.eml");

        Assert.Equal(outcome == "saved", await actions.SaveAsAsync());

        // The wrapper the original was copied through was disposed, which frees its SHA-256 state.
        Assert.NotNull(seen);
        Assert.False(seen!.CanWrite);
        Assert.Equal(outcome == "saved", File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.part"));
    }

    [Fact]
    public async Task WithoutAFilePickerSaveAsIsDisabledWithTheReasonAndDownloadStillWorks()
    {
        // Product-owner decision 2: a device without a file picker keeps the existing Download original .eml.
        using var directory = new TestDirectory(); directory.Create();
        var account = TestDirectory.Profile(); var key = new MailMessageKey(account.Id, "INBOX", 7, 1);
        var receiver = TestSourceReceiver.Returning(Original);
        using var actions = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            UnavailableMailFilePicker.NotOnThisDeviceYet, "copy.eml");
        var saveAs = All(actions).OfType<StandardButton>().Single(button => button.Text == MessageSourceExportActions.SaveAsText);
        Assert.False(saveAs.IsEnabled);
        Assert.Equal(UnavailableMailFilePicker.NotOnThisDeviceYet.Reason, saveAs.ToolTipText);
        Assert.False(actions.CanSaveAs);
        Assert.False(await actions.SaveAsAsync());
        Assert.Equal(0, receiver.CopySourceCalls);

        await actions.ExportAsync();
        Assert.Contains("not opened", actions.Status);
        Assert.Single(Directory.GetFiles(Path.Combine(directory.Root, "source-exports"), "*.eml"));

        // With a picker the button is the first action and explains what it does.
        using var desktop = new MessageSourceExportActions(account, key, receiver, new FileMessageSourceExports(directory.Root), () => true, () => true,
            new FakeMailFilePicker(), "copy.eml");
        var buttons = All(desktop).OfType<StandardButton>().ToList();
        var first = buttons.First(button => button.Text is MessageSourceExportActions.SaveAsText or "Download original .eml");
        Assert.Equal(MessageSourceExportActions.SaveAsText, first.Text);
        Assert.True(first.IsEnabled);
        Assert.Equal(MessageSourceExportActions.SaveAsTip, first.ToolTipText);
        Assert.False(first.IsDefault);
    }

    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var nested in All(child)) yield return nested; }

    private sealed class BufferedReceiver(MailMessageKey key, byte[] bytes) : IMailSourceReceiver
    {
        public MailMessageKey Key = key; public int Calls;
        public Task<MailMessageSource> GetSourceAsync(AccountProfile account, MailMessageKey message, CancellationToken token = default)
        { Calls++; return Task.FromResult(new MailMessageSource(Key, bytes)); }
    }

    /// <summary>A picker that runs <paramref name="during"/> while the person would be choosing.</summary>
    private sealed class HookPicker(FakeMailFilePicker inner, Action during) : IMailFilePicker
    {
        public MailFilePickerCapabilities Capabilities => inner.Capabilities;
        public Task<MailFilePick<IMailReadableFile>> PickOpenFileAsync(MailFileRequest request, CancellationToken token = default) { during(); return inner.PickOpenFileAsync(request, token); }
        public Task<MailFilePick<IMailWritableFile>> PickSaveFileAsync(MailFileRequest request, CancellationToken token = default) { during(); return inner.PickSaveFileAsync(request, token); }
        public Task<MailFilePick<IMailPickedDirectory>> PickDirectoryAsync(MailFileRequest request, CancellationToken token = default) { during(); return inner.PickDirectoryAsync(request, token); }
    }
}
