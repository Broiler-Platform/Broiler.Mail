using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class SentCopyConfigurationTests
{
    [Theory]
    [InlineData(SentCopyMode.NotConfigured)]
    [InlineData(SentCopyMode.ProviderManaged)]
    [InlineData(SentCopyMode.AppendToFolder)]
    public async Task FormPersistsExplicitPolicyWithoutChangingCredentialBindings(SentCopyMode mode)
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 465, UserName = "test" } };
        await store.SaveAsync(profile);
        var model = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);
        using var form = new AccountProfileView(model).CreateContent();
        var fields = Descendants(form).OfType<StandardLabel>().Where(label => label.Target is not null).ToDictionary(label => label.Text, label => label.Target!);
        var selection = Assert.IsType<StandardComboBox>(fields["Sent-copy handling"]);
        var folder = Assert.IsType<StandardEdit>(fields["Sent folder path (exact IMAP path)"]);
        Assert.False(folder.IsEnabled);
        selection.SelectedIndex = (int)mode;
        Assert.Equal(mode == SentCopyMode.AppendToFolder, folder.IsEnabled);
        folder.Text = " Sent & Archive/2026 ";
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Other notifications (such as the saved-password check) also raise Changed; wait for this save to end.
        bool saving = false;
        model.Changed += (_, _) => { if (model.IsBusy) saving = true; else if (saving) finished.TrySetResult(); };
        Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Save account").Click();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var saved = Assert.Single(await store.LoadAsync());
        Assert.Equal(mode, saved.SentCopyMode);
        Assert.Equal(mode == SentCopyMode.AppendToFolder ? "Sent & Archive/2026" : null, saved.SentFolder);
        Assert.Equal(CredentialKey.For(profile, MailProtocol.Imap), CredentialKey.For(saved, MailProtocol.Imap));
        Assert.Equal(CredentialKey.For(profile, MailProtocol.Smtp), CredentialKey.For(saved, MailProtocol.Smtp));
        var restarted = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), saved, null);
        Assert.Equal(mode, restarted.SentCopyMode);
        Assert.Equal(saved.SentFolder ?? "", restarted.SentFolder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\r\npath")]
    public async Task InvalidFolderCannotOverwriteSavedAccount(string path)
    {
        using var directory = new TestDirectory();
        string file = directory.File("accounts.json");
        var store = new JsonAccountStore(file);
        var profile = TestDirectory.Profile() with { OutgoingServer = new() { Host = "smtp.example.test", Port = 465, UserName = "test" } };
        await store.SaveAsync(profile);
        string original = await File.ReadAllTextAsync(file);
        var model = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null)
        { SentCopyMode = SentCopyMode.AppendToFolder, SentFolder = path };
        await model.SaveAsync();
        Assert.StartsWith("Not saved", model.Status);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        model.ConfigureSmtp = false;
        await model.SaveAsync();
        var disabled = Assert.Single(await store.LoadAsync());
        Assert.Null(disabled.OutgoingServer);
        Assert.Equal(SentCopyMode.NotConfigured, disabled.SentCopyMode);
        Assert.Null(disabled.SentFolder);
    }

    [Fact]
    public async Task LegacyAccountAndAcceptedDraftLoadWithoutAssumingASentCopyExists()
    {
        using var directory = new TestDirectory();
        string accountPath = directory.File("accounts.json"), draftPath = directory.File("drafts.json");
        var accounts = new JsonAccountStore(accountPath);
        var profile = TestDirectory.Profile();
        await accounts.SaveAsync(profile);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(accountPath))!;
        json["data"]![0]!.AsObject().Remove("sentCopyMode");
        json["data"]![0]!.AsObject().Remove("sentFolder");
        await File.WriteAllTextAsync(accountPath, json.ToJsonString());
        Assert.Equal(SentCopyMode.NotConfigured, Assert.Single(await accounts.LoadAsync()).SentCopyMode);
        var drafts = new JsonDraftStore(draftPath);
        await drafts.SaveAsync(0, new() { Draft = MailComposition.Create(profile), ToText = "unfinished <", CcText = "", BccText = "", State = DraftSubmissionState.Accepted });
        json = JsonNode.Parse(await File.ReadAllTextAsync(draftPath))!;
        json["data"]!["draft"]!.AsObject().Remove("sentCopy");
        json["data"]!["draft"]!.AsObject().Remove("sentCopyFolder");
        json["data"]!["draft"]!["draft"]!.AsObject().Remove("submissionDate");
        await File.WriteAllTextAsync(draftPath, json.ToJsonString());
        using var recovered = new ComposerViewModel(drafts, await drafts.LoadAsync());
        recovered.SetAccount(profile);
        Assert.Equal(DraftSubmissionState.Accepted, recovered.SubmissionState);
        Assert.Equal(SentCopyState.NotRequested, recovered.SentCopy);
        Assert.False(recovered.CanSend);
        Assert.Contains("no app copy", recovered.SentCopyText);
        Assert.Equal("unfinished <", recovered.To);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
