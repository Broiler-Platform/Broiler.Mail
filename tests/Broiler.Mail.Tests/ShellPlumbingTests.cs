// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Infrastructure.Persistence;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.2: one picker reaches every view that chooses files; without one, views fall back to the unavailable picker.</summary>
[Collection("UI theme")]
public sealed class ShellPlumbingTests
{
    [Fact]
    public void ShellPassesOnePickerToAllViews()
    {
        var fake = new FakeMailFilePicker();
        using (var fixture = MailFileDialogsTests.ShellFixture.Open(files: fake))
        {
            var shell = fixture.Shell;
            Assert.Same(fake, shell.Files);
            Assert.Same(fake, shell.Inbox.Shell!.Files);
            Assert.Same(fake, shell.Composer.Files);
            Assert.Same(fake, shell.SavedOriginals.Files);
            Assert.Same(fake, shell.SavedOriginals.LocalImports.Files);
            // The shell owns no dialogs of its own when given a picker.
            Assert.Null(shell.ActiveFileDialog);
        }

        using (var fixture = MailFileDialogsTests.ShellFixture.Open())
        {
            var files = Assert.IsType<MailFileDialogs>(fixture.Shell.Files);
            Assert.Same(files, fixture.Shell.Composer.Files);
            Assert.Same(files, fixture.Shell.SavedOriginals.LocalImports.Files);
            Assert.Equal(MailFilePickerCapabilities.OpenFile | MailFilePickerCapabilities.SaveFile |
                MailFilePickerCapabilities.PickDirectory | MailFilePickerCapabilities.LocalPaths, files.Capabilities);
        }
    }

    [Fact]
    public void SettingsPassesThePickerToProfileStorage()
    {
        using var directory = new TestDirectory();
        var dispatcher = new TestQueueDispatcher();
        var settings = new SettingsViewModel(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null);
        var fake = new FakeMailFilePicker();

        Assert.Same(fake, new SettingsView(settings, fake).Files);
        Assert.Same(UnavailableMailFilePicker.NotOnThisDeviceYet, new SettingsView(settings).Files);

        using var profile = MailProfile.Open(directory.File("profile"));
        var storage = new ProfileStorageService(profile, new SqliteMailCache(profile.CachePath));
        using var model = new ProfileStorageViewModel(storage, new Broiler.UI.Standard.ImmediateUiDispatcher());
        Assert.Same(fake, new ProfileStorageView(model, fake).Files);
        Assert.Same(UnavailableMailFilePicker.NotOnThisDeviceYet, new ProfileStorageView(model).Files);
    }

    [Fact]
    public async Task StandaloneViewsFallBackToTheUnavailablePicker()
    {
        var unavailable = UnavailableMailFilePicker.NotOnThisDeviceYet;
        Assert.Same(unavailable, new LocalOriginalsView(null).Files);
        Assert.Same(unavailable, new SavedOriginalsView(null).Files);
        Assert.Same(unavailable, new SavedOriginalsView(null).LocalImports.Files);

        Assert.Equal(MailFilePickerCapabilities.None, unavailable.Capabilities);
        var request = new MailFileRequest(MailFilePurpose.Attachment, "Attach file");
        var open = await unavailable.PickOpenFileAsync(request);
        var save = await unavailable.PickSaveFileAsync(request);
        var folder = await unavailable.PickDirectoryAsync(request);
        Assert.All(new[] { (open.Status, open.Message), (save.Status, save.Message), (folder.Status, folder.Message) }, result =>
        {
            Assert.Equal(MailFilePickStatus.Unavailable, result.Status);
            Assert.Equal("Choosing files is not available on this device yet.", result.Message);
        });
        Assert.Null(open.Item);
    }
}
