// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Mime;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Window;

namespace Broiler.Mail.Tests;

/// <summary>
/// UX-M2 exit gate, end to end through the shell over loopback IMAP, a real profile, its cache and its saved originals:
/// no raw path typing in Compose, Saved originals or Backups (where the device has a file picker); attachments visible
/// in the list (also for a message never opened) and the reader, and savable in one click; 12 MiB .eml files round-trip.
/// Where the picker is unavailable (Android until UX-M5) the typed paths stay, and only file dialogs the person asks
/// for open, the documented exemption to the no-modal rule.
/// </summary>
[Collection("UI theme")]
public sealed class UxM2ExitGateTests
{
    private const int MiB = 1024 * 1024;
    private const string ReportName = "Bericht – März.pdf";
    private static readonly byte[] Report = Random(3 * MiB, 11);
    private static readonly byte[] Notes = Encoding.ASCII.GetBytes("Plain notes\r\nSecond line\r\n");

    // ---- (a) Zero raw path typing --------------------------------------------------------------------------------

    [Fact]
    public async Task NoRawPathFieldsInComposeOriginalsOrBackups()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, Encoding.ASCII.GetString(Plain("Saved original"))));
        var files = new FakeMailFilePicker();
        using var gate = await Gate.OpenAsync(server, files);
        string outside = gate.Outside;

        // Compose: Attach file… picks a file that is copied into the draft.
        string attachment = Path.Combine(outside, "report.pdf");
        File.WriteAllBytes(attachment, Report.AsSpan(0, 4096).ToArray());
        Assert.True(gate.Model.Composer.StartNew());
        Assert.True(gate.Shell.ShowView("compose"));
        gate.Render();
        files.WillOpen(attachment);
        gate.Click("compose", "Attach file…");
        gate.Until(() => gate.Model.Composer.Attachments.Count == 1 && !gate.Model.Composer.IsAddingAttachment);
        Assert.Equal(Report.AsSpan(0, 4096).ToArray(), gate.Model.Composer.Attachments[0].Content.ToArray());
        gate.ExpectOnlyTheView("compose");

        // Saved originals: Open .eml file…, then Export selected import as .eml… and Save copy as .eml….
        var key = new MailMessageKey(gate.Account.Id, "INBOX", 7, 1);
        byte[] saved = Plain("Saved original");
        await gate.Exports.SaveAsync(new MailMessageSource(key, saved));
        Assert.True(gate.Shell.ShowView("originals"));
        gate.Render();
        var imports = gate.Shell.SavedOriginals.LocalImports;
        string eml = Path.Combine(outside, "opened.eml");
        byte[] local = Plain("Opened locally");
        File.WriteAllBytes(eml, local);
        files.WillOpen(eml);
        gate.Click("originals", LocalOriginalsView.OpenButtonText);
        gate.Until(() => !imports.IsBusy && imports.PreviewText.Length > 0);
        Assert.True(imports.ShowsDecoded);
        Assert.Contains("Subject: Opened locally", imports.PreviewText);
        await imports.ImportOpenedAsync(); gate.Render();
        await imports.ListAsync(); gate.Render();
        gate.Combo("originals", "Local imported originals").SelectedIndex = 0;
        string exported = Path.Combine(outside, "exported.eml");
        files.WillSave(exported);
        gate.Click("originals", LocalOriginalsView.ExportButtonText);
        gate.Until(() => !imports.IsBusy && File.Exists(exported));
        Assert.Equal(local, File.ReadAllBytes(exported));

        var library = gate.Shell.SavedOriginals;
        await library.ListAsync(); gate.Render();
        gate.Combo("originals", "Saved originals for current account").SelectedIndex = 0;
        await library.VerifyAsync(library.Exports[0]); gate.Render();
        string copy = Path.Combine(outside, "copy.eml");
        files.WillSave(copy);
        gate.Click("originals", SavedOriginalsView.SaveCopyButtonText);
        gate.Until(() => File.Exists(copy) && !library.IsBusy);
        Assert.Equal(saved, File.ReadAllBytes(copy));
        gate.ExpectOnlyTheView("originals");

        // Settings → Backups: change the backup location, back up there, choose that backup, change where it is restored.
        string backups = Directory.CreateDirectory(Path.Combine(outside, "Backups elsewhere")).FullName;
        string restores = Directory.CreateDirectory(Path.Combine(outside, "Restores elsewhere")).FullName;
        Assert.True(gate.Shell.ShowView("settings"));
        gate.Render();
        var storage = gate.Storage;
        files.WillChooseFolder(backups);
        gate.Click("settings", "Change backup location…");
        gate.Until(() => storage.BackupLocation == backups);
        gate.Click("settings", "Create verified backup");
        gate.Until(() => !storage.IsBusy && Directory.GetDirectories(backups).Length == 1);
        Assert.False(storage.IsError, storage.Status);
        string backup = Directory.GetDirectories(backups).Single();
        files.WillChooseFolder(backup);
        gate.Click("settings", "Choose backup folder…");
        gate.Until(() => storage.RestoreSource == backup);
        files.WillChooseFolder(restores);
        gate.Click("settings", "Change restore location…");
        gate.Until(() => storage.RestoreLocation == restores);
        gate.Click("settings", "Restore backup");
        gate.Until(() => !storage.IsBusy && storage.RestoredProfile is not null);
        Assert.False(storage.IsError, storage.Status);
        Assert.Equal(restores, Path.GetDirectoryName(storage.RestoredProfile));
        Assert.True(Directory.Exists(storage.RestoredProfile));
        gate.ExpectOnlyTheView("settings");

        // Every file operation went through the picker, each from its own button, and nothing asked on its own.
        Assert.Equal([MailFilePurpose.Attachment, MailFilePurpose.OpenOriginal, MailFilePurpose.ExportOriginal, MailFilePurpose.SaveMessage,
            MailFilePurpose.BackupLocation, MailFilePurpose.RestoreBackup, MailFilePurpose.RestoreLocation],
            files.Requests.Select(request => request.Request.Purpose));

        // No field anywhere in the three areas asks for a typed path.
        foreach (string view in new[] { "compose", "originals", "settings" })
            NoTypedPathFields(gate.Shell.GetContent(view), view);
    }

    /// <summary>
    /// Product-owner decision 2: where the picker is unavailable, each area keeps its typed path fields and they still
    /// work; the picker buttons say why they are disabled. This runs over the desktop profile with the unavailable picker
    /// swapped in, not the Android composition (which has no Saved originals or Storage): see AndroidMailSessionTests.
    /// </summary>
    [Fact]
    public async Task AndroidModeKeepsTypedPathsForEveryFileAction()
    {
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        var unavailable = UnavailableMailFilePicker.NotOnThisDeviceYet;
        using var gate = await Gate.OpenAsync(server, unavailable);
        string outside = gate.Outside;

        // Compose: a typed path adds the file, and there is no Attach file….
        string attachment = Path.Combine(outside, "notes.txt");
        File.WriteAllBytes(attachment, Notes);
        Assert.True(gate.Model.Composer.StartNew());
        Assert.True(gate.Shell.ShowView("compose"));
        gate.Render();
        Assert.DoesNotContain(Descendants(gate.Shell.GetContent("compose")).OfType<StandardButton>(), button => button.Text == "Attach file…");
        gate.Edit("compose", ComposerAttachmentsBar.PathLabel).Text = attachment;
        gate.Click("compose", "Add attachment");
        gate.Until(() => gate.Model.Composer.Attachments.Count == 1 && !gate.Model.Composer.IsAddingAttachment);
        Assert.Equal(Notes, gate.Model.Composer.Attachments[0].Content.ToArray());

        // Saved originals: typed open and typed export; the picker buttons are disabled with the reason.
        Assert.True(gate.Shell.ShowView("originals"));
        gate.Render();
        var imports = gate.Shell.SavedOriginals.LocalImports;
        foreach (string text in new[] { LocalOriginalsView.OpenButtonText, LocalOriginalsView.ExportButtonText, SavedOriginalsView.SaveCopyButtonText })
        {
            var button = gate.Button("originals", text);
            Assert.False(button.IsEnabled, text);
            Assert.Equal(unavailable.Reason, button.ToolTipText);
        }
        string eml = Path.Combine(outside, "typed.eml");
        byte[] local = Plain("Typed on a phone");
        File.WriteAllBytes(eml, local);
        gate.Edit("originals", "Local .eml file path").Text = eml;
        gate.Click("originals", "Open typed .eml path");
        gate.Until(() => !imports.IsBusy && imports.PreviewText.Length > 0);
        Assert.Contains("Subject: Typed on a phone", imports.PreviewText);
        await imports.ImportOpenedAsync(); gate.Render();
        await imports.ListAsync(); gate.Render();
        gate.Combo("originals", "Local imported originals").SelectedIndex = 0;
        string exported = Path.Combine(outside, "typed-export.eml");
        gate.Edit("originals", "New local .eml export path").Text = exported;
        gate.Click("originals", "Export to typed path");
        gate.Until(() => !imports.IsBusy && File.Exists(exported));
        Assert.Equal(local, File.ReadAllBytes(exported));

        // Backups: the typed folders stay and a backup goes where it is typed.
        Assert.True(gate.Shell.ShowView("settings"));
        gate.Render();
        var settings = gate.Shell.GetContent("settings");
        var typed = Descendants(settings).OfType<FormField>().Where(field => field.Control is StandardEdit)
            .ToDictionary(field => field.Label.Text, field => (StandardEdit)field.Control);
        Assert.Contains("New backup folder", typed.Keys);
        Assert.Contains("Backup folder", typed.Keys);
        Assert.Contains("New profile folder", typed.Keys);
        foreach (string text in new[] { "Change backup location…", "Choose backup folder…", "Change restore location…" })
            Assert.False(gate.Button("settings", text).IsEnabled, text);
        string backup = Path.Combine(outside, "typed backup");
        typed["New backup folder"].Text = backup;
        gate.Click("settings", "Create verified backup");
        gate.Until(() => !gate.Storage.IsBusy && Directory.Exists(backup));
        Assert.False(gate.Storage.IsError, gate.Storage.Status);
        Assert.True(File.Exists(Path.Combine(backup, "backup.json")));
    }

    // ---- (b) Attachments visible in list and reader -------------------------------------------------------------

    [Fact]
    public async Task AttachmentsAreVisibleInListAndReaderAndSurviveReceive()
    {
        await using var server = AttachmentServer();
        var files = new FakeMailFilePicker();
        using var gate = await Gate.OpenAsync(server, files);
        var inbox = gate.Model.Inbox;
        await gate.ReceiveAsync();

        // Never opened, the row already shows the badge from the server's BODYSTRUCTURE.
        var row = inbox.Messages.Single(message => message.Key.Uid == 1);
        var plain = inbox.Messages.Single(message => message.Key.Uid == 2);
        var hinted = inbox.Messages.Single(message => message.Key.Uid == 3);
        Assert.False(row.IsBodyCached);
        Assert.False(hinted.IsBodyCached);
        Assert.Equal(1, server.BodyStructureFetchCount);
        Assert.DoesNotContain(server.MailCommands, command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        gate.AssertBadge(row, true);
        gate.AssertBadge(plain, false);
        gate.AssertBadge(hinted, true);

        // Opened, the reader says how many and the strip lists exactly those two (the inline cid image is not one).
        await gate.OpenAsync(row.Key);
        var body = inbox.Body!;
        Assert.Equal([ReportName, "notes.txt"], body.Attachments!.Select(part => part.FileName));
        Assert.Equal([Report.Length, Notes.Length], body.Attachments!.Select(part => part.DecodedBytes));
        var content = gate.Shell.GetContent("inbox");
        Assert.Contains(Descendants(content).OfType<StandardLabel>(), label => label.Text.Contains(" · 2 attachments", StringComparison.Ordinal));
        var strip = Descendants(content).OfType<MessageAttachmentStrip>().Single();
        var saves = Descendants(strip).OfType<StandardButton>().Where(button => button.Text == "Save as…").ToArray();
        Assert.Equal([ReportName, "notes.txt"], saves.Select(button => button.AccessibleName!["Save ".Length..^" as…".Length]));
        // Shown in the reader, not only in the tree: the strip and each Save as… are laid out in view and can be clicked.
        gate.AssertReachable("inbox", strip, "the attachment strip");
        foreach (var save in saves) gate.AssertReachable("inbox", save);
        gate.AssertBadge(inbox.Messages.Single(message => message.Key.Uid == 1), true);
        // Receiving and reading asked for no file and opened no dialog.
        Assert.Empty(files.Requests);
        gate.ExpectOnlyTheView("inbox");

        // Receiving again without the hint keeps the badge, and so does a new session over the same cache: both for the
        // opened message, whose decoded body also recorded it, and for message 3, which only the earlier hint marked.
        int peeks = server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        server.RejectBodyStructure = true;
        await gate.ReceiveAsync();
        Assert.Equal(2, server.BodyStructureFetchCount);
        gate.AssertBadge(inbox.Messages.Single(message => message.Key.Uid == 1), true);
        gate.AssertBadge(inbox.Messages.Single(message => message.Key.Uid == 2), false);
        var stillHinted = inbox.Messages.Single(message => message.Key.Uid == 3);
        Assert.False(stillHinted.IsBodyCached);
        gate.AssertBadge(stillHinted, true);
        using var next = new InboxViewModel(gate.Receiver, new ImmediateUiDispatcher(), gate.Cache);
        next.SetAccount(gate.Account);
        await next.CacheLoadTask;
        await next.ReceiveAsync();
        Assert.Equal(3, server.BodyStructureFetchCount);
        Assert.True(next.Messages.Single(message => message.Key.Uid == 1).HasAttachments);
        Assert.False(next.Messages.Single(message => message.Key.Uid == 2).HasAttachments);
        var nextHinted = next.Messages.Single(message => message.Key.Uid == 3);
        Assert.False(nextHinted.IsBodyCached);
        Assert.True(nextHinted.HasAttachments);
        // No body was read to get there.
        Assert.Equal(peeks, server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal)));
    }

    // ---- (c) Savable in one click -------------------------------------------------------------------------------

    [Fact]
    public async Task OneClickSaveAsWritesVerifiedBytesOutsideProfileQuotas()
    {
        await using var server = AttachmentServer();
        var files = new FakeMailFilePicker();
        using var gate = await Gate.OpenAsync(server, files);
        // Both profile export folders are at their 64-file quota: any export there would fail.
        foreach (string folder in new[] { gate.Exports.ExportDirectory, gate.Exports.AttachmentDirectory })
        {
            Directory.CreateDirectory(folder);
            for (int index = 0; index < FileMessageSourceExports.MaximumFiles; index++)
                File.WriteAllBytes(Path.Combine(folder, $"existing-{index:00}.bin"), [1, 2, 3]);
        }
        string[] Snapshot() => new[] { gate.Exports.ExportDirectory, gate.Exports.AttachmentDirectory }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(".lock", StringComparison.Ordinal))
            .Select(path => path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).Order(StringComparer.Ordinal).ToArray();
        var before = Snapshot();

        await gate.ReceiveAsync();
        await gate.OpenAsync(gate.Model.Inbox.Messages.Single(message => message.Key.Uid == 1).Key);
        var strip = Descendants(gate.Shell.GetContent("inbox")).OfType<MessageAttachmentStrip>().Single();
        string target = Path.Combine(gate.Outside, ReportName);
        files.WillSave(target);
        int peeks = server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));

        // One click.
        var save = Descendants(strip).OfType<StandardButton>().Single(button => button.AccessibleName == $"Save {ReportName} as…");
        gate.AssertReachable("inbox", strip, "the attachment strip");
        gate.AssertReachable("inbox", save);
        gate.Click(save);
        gate.Until(() => !strip.IsBusy && File.Exists(target));

        var (kind, request) = Assert.Single(files.Requests);
        Assert.Equal(("save", MailFilePurpose.SaveAttachment, ReportName), (kind, request.Purpose, request.SuggestedFileName));
        Assert.Equal(SHA256.HashData(Report), SHA256.HashData(File.ReadAllBytes(target)));
        Assert.Equal($"Saved {ReportName} (3.0 MiB). The file was not opened.", strip.Status);
        Assert.Equal(peeks + 1, server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal)));
        Assert.DoesNotContain(server.Commands, command => command is "STORE" or "EXPUNGE");
        // Nothing went into, or out of, the profile's export folders, and no saved-original receipt was made.
        Assert.Equal(before, Snapshot());
        Assert.Empty(await gate.Exports.ListAccountExportsAsync(gate.Account.Id));
        Assert.Empty(Directory.GetFiles(gate.Outside, "*.part"));
        gate.ExpectOnlyTheView("inbox");
    }

    // ---- (d) 12 MiB .eml round trip ----------------------------------------------------------------------------

    /// <summary>Exactly 12 MiB, the inclusive limit, streams from IMAP into the file and opens again.</summary>
    [Fact]
    public Task TwelveMiBEmlRoundTripsThroughSaveAndOpenDecoded() => RoundTripAsync(IStreamingMessageSourceExports.MaximumBytes);

    /// <summary>The roadmap's UX-03.5 verification: a 5 MiB server original exported and opened decoded.</summary>
    [Fact]
    public Task FiveMiBServerOriginalRoundTrip() => RoundTripAsync(5 * MiB);

    /// <summary>
    /// One byte over 12 MiB is refused on every leg: the streamed IMAP save that Save as .eml… and Ctrl+Shift+S run over
    /// this receiver writes no file and leaves no partial one, the reader says the message exceeds the limit without
    /// timing out, and a local .eml of that size is not opened.
    /// </summary>
    [Fact]
    public async Task TwelveMiBPlusOneIsRefused()
    {
        int size = IStreamingMessageSourceExports.MaximumBytes + 1;
        string subject = $"Round trip of {size} bytes";
        string raw = Large(size, subject);
        Assert.Equal(size, raw.Length);
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, raw));
        var files = new FakeMailFilePicker();
        using var gate = await Gate.OpenAsync(server, files);
        await gate.ReceiveAsync();
        var key = Assert.Single(gate.Model.Inbox.Messages).Key;
        // The streamed save itself, as Save as .eml… runs it over this receiver: the file is chosen, then the original
        // is refused by its reported size before anything is written.
        string target = Path.Combine(gate.Outside, "too large.eml");
        files.WillSave(target);
        using (var actions = new MessageSourceExportActions(gate.Account, key, gate.Receiver, gate.Exports, () => true, () => true, files, "too large.eml"))
        {
            Assert.True(actions.CanSaveAs);
            Assert.False(await actions.SaveAsAsync());
            Assert.False(actions.IsBusy);
            Assert.Equal("Message not saved. The original is empty or exceeds the available export limit.", actions.Status);
            Assert.Null(actions.LastSaved);
        }
        Assert.Equal(MailFilePurpose.SaveMessage, Assert.Single(files.Requests).Request.Purpose);
        Assert.False(File.Exists(target));
        Assert.Empty(System.IO.Directory.GetFiles(gate.Outside));
        Assert.Empty(await gate.Exports.ListAccountExportsAsync(gate.Account.Id));
        gate.ExpectOnlyTheView("inbox");

        // Opening it in the reader (selecting loads it, opening loads it again) is refused by its reported size each
        // time: promptly, with the reading limit rather than the receiver's 60-second timeout, and before any body is
        // requested, so the server never starts a literal the reader would abandon.
        int peeks = server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        var opening = System.Diagnostics.Stopwatch.StartNew();
        await gate.Model.Inbox.SelectAsync(key);
        gate.Until(() => !gate.Model.Inbox.IsBusy);
        Assert.Contains("This message exceeds the 12 MiB limit (including attachments).", gate.Model.Inbox.Status);
        gate.Shell.Inbox.OpenSelected();
        gate.Until(() => !gate.Model.Inbox.IsBusy);
        opening.Stop();
        Assert.Contains("This message exceeds the 12 MiB limit (including attachments).", gate.Model.Inbox.Status);
        Assert.DoesNotContain("timed out", gate.Model.Inbox.Status);
        Assert.Null(gate.Model.Inbox.Body);
        Assert.True(opening.Elapsed < TimeSpan.FromSeconds(20), $"The refusal took {opening.Elapsed}.");
        Assert.Equal(peeks, server.MailCommands.Count(command => command.Contains("BODY.PEEK[]", StringComparison.Ordinal)));
        gate.ExpectOnlyTheView("inbox");

        // The same octets as a local .eml file are not opened either.
        string larger = Path.Combine(gate.Outside, "one byte more.eml");
        File.WriteAllBytes(larger, Encoding.ASCII.GetBytes(raw));
        Assert.Equal(IStreamingMessageSourceExports.MaximumBytes + 1, new FileInfo(larger).Length);
        files.WillOpen(larger);
        Assert.True(gate.Keyboard.Handle(InlineMailFixture.Key('O', KeyboardModifierState.Control)));
        var imports = gate.Shell.SavedOriginals.LocalImports;
        gate.Until(() => !imports.IsBusy && imports.Status.StartsWith("The file could not be opened.", StringComparison.Ordinal));
        Assert.Contains("Local .eml files must contain 1 byte to 12 MiB.", imports.Status);
        Assert.DoesNotContain("Subject: " + subject, imports.PreviewText);
    }

    private static async Task RoundTripAsync(int size)
    {
        string subject = $"Round trip of {size} bytes";
        string raw = Large(size, subject);
        Assert.Equal(size, raw.Length);
        byte[] octets = Encoding.ASCII.GetBytes(raw);
        await using var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, raw));
        var files = new FakeMailFilePicker();
        using var gate = await Gate.OpenAsync(server, files);
        await gate.ReceiveAsync();
        var key = Assert.Single(gate.Model.Inbox.Messages).Key;
        await gate.OpenAsync(key);

        // Ctrl+Shift+S saves the open message through the picker.
        string target = Path.Combine(gate.Outside, "round trip.eml");
        files.WillSave(target);
        Assert.True(gate.Shell.Inbox.CanSaveMessageAsEml);
        Assert.True(gate.Keyboard.Handle(InlineMailFixture.Key('S', KeyboardModifierState.Control | KeyboardModifierState.Shift)));
        gate.Until(() => File.Exists(target) && gate.Shell.Inbox.CanSaveMessageAsEml);
        Assert.Equal(MailFilePurpose.SaveMessage, Assert.Single(files.Requests).Request.Purpose);
        Assert.Equal(SHA256.HashData(octets), SHA256.HashData(File.ReadAllBytes(target)));
        Assert.Empty(await gate.Exports.ListAccountExportsAsync(gate.Account.Id));
        gate.ExpectOnlyTheView("inbox");

        // Ctrl+O opens it again: decoded by default, the raw toggle shows the source.
        files.WillOpen(target);
        Assert.True(gate.Keyboard.Handle(InlineMailFixture.Key('O', KeyboardModifierState.Control)));
        var imports = gate.Shell.SavedOriginals.LocalImports;
        gate.Until(() => !imports.IsBusy && imports.PreviewText.Length > 0);
        Assert.Equal("originals", gate.Shell.ActiveViewId);
        Assert.Equal(MailFilePurpose.OpenOriginal, files.Requests[1].Request.Purpose);
        Assert.True(imports.ShowsDecoded, imports.Status);
        Assert.Contains("Subject: " + subject, imports.PreviewText);
        Assert.Contains("The round trip body.", imports.PreviewText);
        Assert.DoesNotContain("Content-Transfer-Encoding: base64", imports.PreviewText);
        if (size > 2 * MiB) Assert.Contains(LocalOriginalsView.NotKeptText, imports.Status);
        gate.Click("originals", LocalOriginalsView.ShowRawText);
        Assert.False(imports.ShowsDecoded);
        Assert.Contains("MIME-Version: 1.0", imports.PreviewText);
        Assert.Contains("Content-Disposition: attachment; filename=\"bulk.bin\"", imports.PreviewText);
        // The file on disk is still exactly the server's octets.
        Assert.Equal(size, new FileInfo(target).Length);
        Assert.Equal(SHA256.HashData(octets), SHA256.HashData(File.ReadAllBytes(target)));
    }

    // ---- Decision 4: only file dialogs the person asks for ---------------------------------------------------------

    /// <summary>
    /// With the shell's own file dialogs: receiving, reading a message with attachments and opening Compose, Saved
    /// originals and Settings open no file dialog and no other modal. Each exempted file command (every button, menu
    /// item and shortcut the roadmap lists) opens exactly one file dialog, shown over its own form (or the main window),
    /// which is then the only modal element; Escape closes only it, writes nothing and leaves nothing busy.
    /// </summary>
    [Fact]
    public async Task OnlyUserInitiatedFileDialogsOpen()
    {
        await using var server = AttachmentServer();
        var sender = new UnusedSender();
        using var gate = await Gate.OpenAsync(server, files: null, sender: sender);
        Assert.IsType<MailFileDialogs>(gate.Shell.Files);
        await gate.ReceiveAsync();
        await gate.OpenAsync(gate.Model.Inbox.Messages.Single(message => message.Key.Uid == 1).Key);
        gate.ExpectOnlyTheView("inbox");

        // The reader: the strip's Save as… and Save all…, the source section's Save as .eml…, the same command in More
        // message actions and in the File menu, and Ctrl+Shift+S; Ctrl+O and File → Open .eml file… from the inbox.
        var inbox = gate.Shell.GetContent("inbox");
        var strip = Descendants(inbox).OfType<MessageAttachmentStrip>().Single();
        var source = Descendants(inbox).OfType<MessageSourceExportActions>().Single();
        bool ReaderIdle() => !strip.IsBusy && !source.IsBusy && !gate.Model.Inbox.IsBusy;
        gate.ExpectOneFileDialog(Descendants(strip).OfType<StandardButton>().First(button => button.Text == "Save as…"), null, ReaderIdle);
        Assert.Equal("", strip.Status);
        gate.ExpectOneFileDialog(Descendants(strip).OfType<StandardButton>().Single(button => button.Text == "Save all…"), null, ReaderIdle);
        Assert.Equal("", strip.Status);
        gate.ExpectOneFileDialog(Descendants(source).OfType<StandardButton>().Single(button => button.Text == MessageSourceExportActions.SaveAsText), null, ReaderIdle);
        Assert.Equal("No file chosen. Nothing was saved.", source.Status);
        gate.ExpectOneFileDialog("More message actions → " + MessageSourceExportActions.SaveAsText,
            () => ReaderMenus.Invoke(gate.Session, inbox, "More message actions", MessageSourceExportActions.SaveAsText), null, ReaderIdle);
        gate.ExpectOneFileDialog("File → Save message as .eml…", () => gate.InvokeFileMenu("save-eml"), null, ReaderIdle);
        gate.ExpectOneFileDialog("Ctrl+Shift+S",
            () => Assert.True(gate.Keyboard.Handle(InlineMailFixture.Key('S', KeyboardModifierState.Control | KeyboardModifierState.Shift))), null, ReaderIdle);
        gate.ExpectOneFileDialog("Ctrl+O", () => Assert.True(gate.Keyboard.Handle(InlineMailFixture.Key('O', KeyboardModifierState.Control))), null, ReaderIdle);
        gate.ExpectOneFileDialog("File → Open .eml file…", () => gate.InvokeFileMenu("open-eml"), null, ReaderIdle);

        // Saved originals: Open .eml file…, Export selected import as .eml… and Save copy as .eml….
        Assert.True(gate.Shell.ShowView("originals")); gate.Render();
        gate.ExpectOnlyTheView("originals");
        var originals = gate.Shell.ActiveDialog;
        var imports = gate.Shell.SavedOriginals.LocalImports;
        var library = gate.Shell.SavedOriginals;
        bool OriginalsIdle() => !imports.IsBusy && !library.IsBusy;
        gate.ExpectOneFileDialog(gate.Button("originals", LocalOriginalsView.OpenButtonText), originals, OriginalsIdle);
        string eml = Path.Combine(gate.Outside, "imported.eml");
        File.WriteAllBytes(eml, Plain("Imported locally"));
        await imports.OpenFileAsync(eml); gate.Render();
        await imports.ImportOpenedAsync(); gate.Render();
        await imports.ListAsync(); gate.Render();
        gate.Combo("originals", "Local imported originals").SelectedIndex = 0;
        gate.Render();
        gate.ExpectOneFileDialog(gate.Button("originals", LocalOriginalsView.ExportButtonText), originals, OriginalsIdle);
        await gate.Exports.SaveAsync(new MailMessageSource(new MailMessageKey(gate.Account.Id, "INBOX", 7, 1), Plain("Saved original")));
        await library.ListAsync(); gate.Render();
        gate.Combo("originals", "Saved originals for current account").SelectedIndex = 0;
        await library.VerifyAsync(library.Exports[0]); gate.Render();
        gate.ExpectOneFileDialog(gate.Button("originals", SavedOriginalsView.SaveCopyButtonText), originals, OriginalsIdle);

        // Settings → Backups: the backup location, the backup to restore and where it is restored.
        Assert.True(gate.Shell.ShowView("settings")); gate.Render();
        gate.ExpectOnlyTheView("settings");
        var storage = gate.Storage;
        string backupLocation = storage.BackupLocation, restoreSource = storage.RestoreSource, restoreLocation = storage.RestoreLocation;
        foreach (string text in new[] { "Change backup location…", "Choose backup folder…", "Change restore location…" })
            gate.ExpectOneFileDialog(gate.Button("settings", text), gate.Shell.ActiveDialog, () => !storage.IsBusy);
        Assert.Equal((backupLocation, restoreSource, restoreLocation), (storage.BackupLocation, storage.RestoreSource, storage.RestoreLocation));

        // Compose: Attach file…, and the attachment reminder's Attach file… before sending.
        Assert.True(gate.Model.Composer.StartNew());
        Assert.True(gate.Shell.ShowView("compose")); gate.Render();
        gate.ExpectOnlyTheView("compose");
        var compose = gate.Shell.ActiveDialog;
        bool ComposeIdle() => !gate.Model.Composer.IsAddingAttachment && gate.Model.Composer.Attachments.Count == 0;
        gate.ExpectOneFileDialog(gate.Button("compose", "Attach file…"), compose, ComposeIdle);
        gate.WriteDraft("Quarterly report", "Here is the report, see attached.");
        gate.Click("compose", "Send");
        Assert.True(gate.Model.Composer.ShowsSendConfirmation);
        gate.ExpectOnlyTheView("compose");
        var confirmAttach = Descendants(gate.Shell.GetContent("compose")).OfType<StandardButton>().Single(button => button.AccessibleName == "Attach file before sending");
        gate.ExpectOneFileDialog(confirmAttach, compose, ComposeIdle);
        Assert.True(gate.Model.Composer.ShowsSendConfirmation);
        Assert.Equal(0, sender.Calls);
    }

    /// <summary>Lets Compose send (so the attachment reminder can show) and records that nothing was sent.</summary>
    private sealed class UnusedSender : IMailSender
    {
        public int Calls { get; private set; }
        public Task<SendResult> SendAsync(AccountProfile account, MailDraft draft, CancellationToken token = default)
        {
            Calls++;
            throw new InvalidOperationException("The exemption walk never sends mail.");
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private static readonly Regex PathWords = new(@"\b(path|paths|file|files|folder|folders|directory)\b|\.eml\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>No edit asks for a path: none is sized for one, and none is named, labelled or hinted as one.</summary>
    private static void NoTypedPathFields(UiElement root, string where)
    {
        var all = Descendants(root).ToArray();
        foreach (var edit in all.OfType<StandardEdit>())
        {
            Assert.True(edit.MaxLength != 32767, $"{where}: {edit.AccessibleName} is sized for a path.");
            var names = new List<string?> { edit.AccessibleName, edit.PlaceholderText, edit.ToolTipText };
            names.AddRange(all.OfType<StandardLabel>().Where(label => ReferenceEquals(label.Target, edit)).Select(label => label.Text));
            names.AddRange(all.OfType<FormField>().Where(field => ReferenceEquals(field.Control, edit)).Select(field => field.Label.Text));
            foreach (string? name in names)
                Assert.False(name is not null && PathWords.IsMatch(name), $"{where}: the field \"{name}\" asks for a typed path.");
        }
    }

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Plain(string subject) => Encoding.ASCII.GetBytes(
        $"From: Sender <sender@example.test>\r\nTo: test@example.test\r\nSubject: {subject}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nExact bytes.\r\n");

    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks);

    /// <summary>
    /// Message 1: text and HTML with an inline cid: image, plus two attachments, one of them 3 MiB under an RFC 2231
    /// international name; message 2: plain text. The server describes both in BODYSTRUCTURE, as a real one does.
    /// </summary>
    private static LocalImapServer AttachmentServer()
    {
        string raw = "From: Sender <sender@example.test>\r\nTo: test@example.test\r\nSubject: Quarterly report\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"outer\"\r\n\r\n" +
            "--outer\r\nContent-Type: multipart/related; boundary=\"rel\"\r\n\r\n" +
            "--rel\r\nContent-Type: multipart/alternative; boundary=\"alt\"\r\n\r\n" +
            "--alt\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSee the report.\r\n" +
            "--alt\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>See the report.</p><img src=\"cid:logo@example.test\">\r\n--alt--\r\n" +
            "--rel\r\nContent-Type: image/png; name=\"logo.png\"\r\nContent-ID: <logo@example.test>\r\nContent-Disposition: inline; filename=\"logo.png\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\niVBORw0KGgo=\r\n--rel--\r\n" +
            "--outer\r\nContent-Type: application/pdf\r\nContent-Disposition: attachment; filename*=UTF-8''Bericht%20%E2%80%93%20M%C3%A4rz.pdf\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" + Base64(Report) + "\r\n" +
            "--outer\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Disposition: attachment; filename=\"notes.txt\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" + Base64(Notes) + "\r\n--outer--\r\n";
        static string Multipart(string subtype, params string[] parts) => "(" + string.Concat(parts) + $" \"{subtype}\" (\"BOUNDARY\" \"b\") NIL NIL NIL)";
        const string text = "(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"utf-8\") NIL NIL \"7BIT\" 15 1 NIL NIL NIL NIL)";
        const string html = "(\"TEXT\" \"HTML\" (\"CHARSET\" \"utf-8\") NIL NIL \"7BIT\" 58 1 NIL NIL NIL NIL)";
        const string logo = "(\"IMAGE\" \"PNG\" (\"NAME\" \"logo.png\") \"<logo@example.test>\" NIL \"BASE64\" 12 NIL (\"INLINE\" (\"FILENAME\" \"logo.png\")) NIL NIL)";
        string pdf = $"(\"APPLICATION\" \"PDF\" NIL NIL NIL \"BASE64\" {Base64(Report).Length} NIL (\"ATTACHMENT\" (\"FILENAME*\" \"UTF-8''Bericht%20%E2%80%93%20M%C3%A4rz.pdf\")) NIL NIL)";
        string notes = $"(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"utf-8\") NIL NIL \"BASE64\" {Base64(Notes).Length} 1 NIL (\"ATTACHMENT\" (\"FILENAME\" \"notes.txt\")) NIL NIL)";
        var server = new LocalImapServer(TransportSecurity.Tls);
        server.Messages.Add(new(1, raw, BodyStructure: Multipart("MIXED", Multipart("RELATED", Multipart("ALTERNATIVE", text, html), logo), pdf, notes)));
        server.Messages.Add(new(2, Encoding.ASCII.GetString(Plain("Nothing attached"))));
        // Message 3 is never opened by any test: only its BODYSTRUCTURE hint ever marks it.
        string hinted = "From: Sender <sender@example.test>\r\nTo: test@example.test\r\nSubject: Hinted only\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"hint\"\r\n\r\n" +
            "--hint\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSee the file.\r\n" +
            "--hint\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Disposition: attachment; filename=\"later.txt\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" + Base64(Notes) + "\r\n--hint--\r\n";
        string later = $"(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"utf-8\") NIL NIL \"BASE64\" {Base64(Notes).Length} 1 NIL (\"ATTACHMENT\" (\"FILENAME\" \"later.txt\")) NIL NIL)";
        server.Messages.Add(new(3, hinted, BodyStructure: Multipart("MIXED", text, later)));
        return server;
    }

    /// <summary>A message of exactly <paramref name="total"/> octets: a short text part and one base64 attachment.</summary>
    private static string Large(int total, string subject)
    {
        string head = $"From: Sender <sender@example.test>\r\nTo: test@example.test\r\nSubject: {subject}\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"outer\"\r\n\r\n--outer\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nThe round trip body.\r\n";
        const string part = "--outer\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"bulk.bin\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n";
        const string tail = "\r\n--outer--\r\n";
        // 57 bytes make one full 76-character base64 line; k lines take 78k - 2 characters.
        int lines = (total - head.Length - part.Length - tail.Length - 200) / 78;
        string encoded = Base64(Random(57 * lines, total));
        Assert.Equal(78 * lines - 2, encoded.Length);
        int filler = total - head.Length - part.Length - tail.Length - encoded.Length;
        Assert.InRange(filler, 200, 300);
        return head + new string('x', filler - 2) + "\r\n" + part + encoded + tail;
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>
    /// The shell over a real profile (cache, saved originals, storage service) and loopback IMAP, with the attachment
    /// reader the application wires: saved originals first, then one streamed, read-only download.
    /// </summary>
    private sealed class Gate : IDisposable
    {
        private Gate() { }

        public TestDirectory Directory { get; } = new();
        public TestQueueDispatcher Dispatcher { get; } = new();
        public MailProfile Profile { get; private set; } = null!;
        public SqliteMailCache Cache { get; private set; } = null!;
        public FileMessageSourceExports Exports { get; private set; } = null!;
        public ProfileStorageViewModel Storage { get; private set; } = null!;
        public AccountProfile Account { get; private set; } = null!;
        public ImapMailReceiver Receiver { get; private set; } = null!;
        public MailShellViewModel Model { get; private set; } = null!;
        public MailShellView Shell { get; private set; } = null!;
        public UiSession Session { get; private set; } = null!;
        public MailKeyboardNavigation Keyboard { get; private set; } = null!;
        /// <summary>A folder outside the profile, as the person's documents are.</summary>
        public string Outside => System.IO.Directory.CreateDirectory(Directory.File("Documents")).FullName;

        /// <param name="sender">With a sender the account also gets an outgoing server, so Compose can send.</param>
        public static async Task<Gate> OpenAsync(LocalImapServer server, IMailFilePicker? files, int width = 1100, int height = 720, IMailSender? sender = null)
        {
            var gate = new Gate();
            gate.Directory.Create();
            gate.Profile = MailProfile.Open(gate.Directory.File("profile"));
            gate.Cache = new SqliteMailCache(gate.Profile.CachePath);
            var service = new ProfileStorageService(gate.Profile, gate.Cache);
            gate.Exports = new FileMessageSourceExports(gate.Profile.DirectoryPath, service);
            gate.Account = TestDirectory.Profile() with { IncomingServer = new() { Host = "127.0.0.1", Port = server.Port, UserName = "test", Security = TransportSecurity.Tls } };
            if (sender is not null)
                gate.Account = gate.Account with { OutgoingServer = new() { Host = "smtp.example.test", Port = 587, UserName = "test", Security = TransportSecurity.StartTls } };
            var credentials = new TestCredentialStore();
            await credentials.WriteAsync(CredentialKey.For(gate.Account, MailProtocol.Imap), LocalImapServer.Password);
            gate.Receiver = new ImapMailReceiver(credentials, (_, cert, _, _) => cert?.GetCertHashString() == server.Certificate.GetCertHashString(), TimeSpan.FromSeconds(60));
            gate.Storage = new ProfileStorageViewModel(service, gate.Dispatcher);
            var inbox = new InboxViewModel(gate.Receiver, gate.Dispatcher, gate.Cache, sourceExports: gate.Exports)
            {
                AttachmentReader = new MessageAttachmentReader(gate.Receiver, gate.Receiver, gate.Exports, gate.Exports),
            };
            gate.Model = new MailShellViewModel(
                new(new JsonAccountStore(gate.Directory.File("accounts.json")), credentials, gate.Receiver, gate.Dispatcher, gate.Account, null),
                new(new JsonSettingsStore(gate.Directory.File("settings.json")), gate.Dispatcher, new(), null, gate.Storage),
                inbox,
                new ComposerViewModel(dispatcher: gate.Dispatcher, sender: sender));
            gate.Shell = new MailShellView(gate.Model, files: files);
            gate.Session = new StandardUiSessionBuilder().WithDispatcher(gate.Dispatcher).Build(new Host(width, height));
            gate.Shell.Attach(gate.Session);
            gate.Shell.ShowView("inbox");
            await inbox.CacheLoadTask;
            gate.Dispatcher.DrainUntil(() => !inbox.IsBusy);
            gate.Keyboard = gate.Shell.CreateKeyboardNavigation(gate.Session);
            gate.Render();
            return gate;
        }

        public void Render()
        {
            Session.RenderFrame();
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        /// <summary>Drains posted work until <paramref name="settled"/> holds; large originals take longer than the default wait.</summary>
        public void Until(Func<bool> settled, int seconds = 90)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                Dispatcher.Drain();
                if (settled()) break;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Background work did not settle.");
                Thread.Sleep(5);
            }
            Render();
        }

        public async Task ReceiveAsync()
        {
            await Model.Inbox.ReceiveAsync();
            Until(() => !Model.Inbox.IsBusy);
        }

        public async Task OpenAsync(MailMessageKey key)
        {
            await Model.Inbox.SelectAsync(key);
            Until(() => !Model.Inbox.IsBusy);
            Shell.Inbox.OpenSelected();
            Until(() => !Model.Inbox.IsBusy && Model.Inbox.Body?.Key == key);
        }

        public StandardButton Button(string view, string text) =>
            Descendants(Shell.GetContent(view)).OfType<StandardButton>().Single(button => button.Text == text);

        public StandardEdit Edit(string view, string name) =>
            Descendants(Shell.GetContent(view)).OfType<StandardEdit>().Single(edit => edit.AccessibleName == name);

        public StandardComboBox Combo(string view, string name) =>
            Descendants(Shell.GetContent(view)).OfType<StandardComboBox>().Single(combo => combo.AccessibleName == name);

        public void Click(string view, string text) => Click(Button(view, text));

        public void Click(StandardButton button)
        {
            Assert.True(button.IsEnabled, $"{button.Text} is disabled.");
            if (button.CanFocus) Session.SetFocus(button);
            button.Click();
            Render();
        }

        public bool Press(UiInputEvent input) => Keyboard.Handle(input) || Session.DispatchInput(input);

        /// <summary>The view is shown on its own: no file dialog and no other modal element.</summary>
        public void ExpectOnlyTheView(string view)
        {
            Assert.Equal(view, Shell.ActiveViewId);
            Assert.Null(Shell.ActiveFileDialog);
            Assert.Null(Shell.ActiveAboutDialog);
            if (view == "inbox")
            {
                Assert.Null(Shell.ActiveDialog);
                Assert.Null(Session.ModalElement);
            }
            else
            {
                Assert.NotNull(Shell.ActiveDialog);
                Assert.Same(Shell.ActiveDialog, Session.ModalElement);
            }
        }

        /// <summary>
        /// Pressing <paramref name="trigger"/> opens one file dialog, the only modal element, over <paramref name="form"/>
        /// (or the main window); Escape closes it and only it, and focus goes back to the button.
        /// </summary>
        public void ExpectOneFileDialog(StandardButton trigger, UiDialog? form, Func<bool> idle)
        {
            ExpectOneFileDialog(trigger.Text, () => Click(trigger), form, idle);
            if (trigger.CanFocus) Assert.True(ReferenceEquals(trigger, Session.FocusedElement), $"{trigger.Text}: focus is on {Session.FocusedElement} after Escape.");
        }

        /// <summary>
        /// Running <paramref name="trigger"/> opens one file dialog, the only modal element, shown over
        /// <paramref name="form"/> (or the main window). Escape closes it and only it: no other dialog follows, no file
        /// is written outside or inside the profile, and <paramref name="idle"/> holds again.
        /// </summary>
        public void ExpectOneFileDialog(string what, Action trigger, UiDialog? form, Func<bool> idle)
        {
            string view = Shell.ActiveViewId;
            var files = Assert.IsType<MailFileDialogs>(Shell.Files);
            var before = WrittenFiles();
            trigger();
            Render();
            var dialog = Shell.ActiveFileDialog;
            Assert.True(dialog is not null, $"{what} opened no file dialog.");
            Assert.Same(dialog, Session.ModalElement);
            Assert.Same(form, Shell.ActiveDialog);
            Assert.True(ReferenceEquals((UiWindow?)form ?? Shell.Window, files.ActiveOwner), $"{what}: the file dialog is over {files.ActiveOwner}.");
            if (!dialog.IsBrokenOut) Assert.Same((UiWindow?)form ?? Shell.Window, dialog.Owner);
            Assert.Null(Shell.ActiveAboutDialog);
            Assert.Equal(view, Shell.ActiveViewId);
            Press(InlineMailFixture.Key(0x1B));
            Until(() => Shell.ActiveFileDialog is null && idle());
            Assert.True(dialog.IsResultCompleted || dialog.IsDisposed);
            // Exactly one: nothing else opened once it closed.
            Render();
            ExpectOnlyTheView(view);
            Assert.True(idle(), $"{what}: still busy after Escape.");
            Assert.Equal(before, WrittenFiles());
        }

        /// <summary>Every file in the person's documents and the profile's export folders, with its size.</summary>
        private string[] WrittenFiles() => new[] { Outside, Exports.ExportDirectory, Exports.AttachmentDirectory }
            .Where(System.IO.Directory.Exists)
            .SelectMany(folder => System.IO.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(".lock", StringComparison.Ordinal))
            .Select(path => path + ":" + new FileInfo(path).Length).Order(StringComparer.Ordinal).ToArray();

        /// <summary>Chooses an item of the shell's File menu as a keyboard user does, then runs what it posted.</summary>
        public void InvokeFileMenu(string id)
        {
            var file = Shell.Menu.Items[0];
            int index = file.Children.ToList().FindIndex(item => item.Id == id);
            Assert.True(index >= 0, $"File has no {id}.");
            Assert.True(file.Children[index].IsEnabled, $"File → {file.Children[index].Text} is disabled.");
            Keyboard.Execute(MailCommand.OpenMenu);
            Assert.True(Shell.Menu.SetSelectedPath([0, index]));
            Assert.True(Session.DispatchInput(ReaderMenus.Key(0x0D, "Enter")));
            Dispatcher.Drain();
        }

        /// <summary>Types a recipient, subject and text into the open Compose form.</summary>
        public void WriteDraft(string subject, string text)
        {
            var content = Shell.GetContent("compose");
            StandardEdit Field(string label) => (StandardEdit)Descendants(content).OfType<StandardLabel>().Single(item => item.Text == label).Target!;
            Field("To").Text = "to@example.test";
            Field("Subject").Text = subject;
            ((StandardRichEdit)Shell.Composer.Body!).SetPlainText(text);
            Render();
            Assert.Equal((subject, text), (Model.Composer.Subject, Model.Composer.PlainText));
        }

        /// <summary>
        /// The element and every container above it are shown in <paramref name="view"/>, and a pointer user can reach
        /// it there (see <see cref="Reachability.Assert"/>).
        /// </summary>
        public void AssertReachable(string view, UiElement element, string? where = null)
        {
            string name = where ?? element.AccessibleName ?? element.GetType().Name;
            for (UiElement? at = element; at is not null; at = at.Parent)
                Assert.True(at.Visibility == UiVisibility.Visible, $"{name} is hidden by {at.AccessibleName ?? at.GetType().Name}.");
            Assert.True(element.IsDescendantOf(Shell.GetContent(view)), $"{name} is not in {view}.");
            Reachability.Assert(Session, Render, element, name);
        }

        /// <summary>The list draws the row as the inbox's own presenter does: badge and accessible name, or neither.</summary>
        public void AssertBadge(MailMessageSummary row, bool expected)
        {
            Assert.Equal(expected, row.HasAttachments);
            var list = Descendants(Shell.GetContent("inbox")).OfType<StandardListView>().Single();
            var presenter = Assert.IsType<MailMessageItemPresenter>(list.ItemPresenter);
            var item = new UiListItem(row.Key.ToString(), row.Sender, row.Subject, null, row.IsRead, row);
            var state = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: row.IsRead, Index: 0);
            string name = presenter.CreateSemanticNode(new UiListItemSemanticContext { Item = item, Index = 0, Bounds = new BRect(0, 0, 600, 52), State = state }).Name;
            Assert.Equal(expected, name.Contains("Has attachments, ", StringComparison.Ordinal));
            var drawn = new BRenderList();
            presenter.Render(new UiListItemRenderContext
            {
                RenderList = drawn, Bounds = new BRect(0, 0, 600, 120), Item = item, State = state, Font = BFontStyle.Default,
                Foreground = BColor.Black, SecondaryForeground = BColor.Black, Background = BColor.White, SelectedBackground = BColor.White,
                FocusRing = BColor.Black, Accent = BColor.Black,
            });
            // The paperclip is two stroked rounded rectangles, drawn after the row's texts.
            Assert.Equal(expected ? 2 : 0, drawn.Commands.OfType<BRenderCommand.StrokeRoundedRect>().Count());
        }

        public void Dispose()
        {
            Session?.Dispose();
            Shell?.Dispose();
            Storage?.Dispose();
            Profile?.Dispose();
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Dispose(); return; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 100) { Thread.Sleep(20); }
            }
        }
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
