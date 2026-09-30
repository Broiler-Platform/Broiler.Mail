using System.Text.Json.Nodes;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class SmtpConfigurationTests
{
    [Fact]
    public async Task SmtpPasswordFormStoresAndForgetsOnlyItsOwnCredentialAndClearsInput()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile() with { OutgoingServer = SmtpServer() };
        await store.SaveAsync(profile);
        string original = await File.ReadAllTextAsync(directory.File("accounts.json"));
        var credentials = new TestCredentialStore();
        await credentials.WriteAsync(CredentialKey.For(profile, MailProtocol.Imap), "imap-secret");
        var model = new AccountProfileViewModel(store, credentials, new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);
        using var form = new AccountProfileView(model).CreateContent();
        var label = Descendants(form).OfType<StandardLabel>().Single(label => label.Text == "SMTP password / app password");
        var password = Assert.IsType<StandardEdit>(label.Target);
        Assert.True(password.IsPassword);
        Assert.True(password.IsEnabled);
        Assert.Empty(password.Text);
        password.Text = "smtp-secret";
        Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Save SMTP password").Click();
        Assert.Empty(password.Text);
        Assert.Equal("smtp-secret", await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Smtp)));
        Assert.Equal(original, await File.ReadAllTextAsync(directory.File("accounts.json")));
        password.Text = "must-clear";
        Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Forget SMTP password").Click();
        Assert.Empty(password.Text);
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Smtp)));
        Assert.Equal("imap-secret", await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Imap)));
    }

    [Fact]
    public async Task UnsavedSmtpEditsBlockCredentialChangesAndSavedConnectionChangesInvalidateBinding()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile() with { OutgoingServer = SmtpServer() };
        await store.SaveAsync(profile);
        var credentials = new TestCredentialStore();
        var model = new AccountProfileViewModel(store, credentials, new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);
        await model.SavePasswordAsync("original", MailProtocol.Smtp);
        model.SmtpHost = "changed.example.test";
        await model.SavePasswordAsync("replacement", MailProtocol.Smtp);
        Assert.Contains("Save your account changes", model.Status);
        await model.ForgetPasswordAsync(MailProtocol.Smtp);
        Assert.Equal("original", await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Smtp)));
        await model.SaveAsync();
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(model.Profile!, MailProtocol.Smtp)));
        await model.SavePasswordAsync("replacement", MailProtocol.Smtp);
        Assert.Equal("replacement", await credentials.ReadAsync(CredentialKey.For(model.Profile!, MailProtocol.Smtp)));
    }

    [Fact]
    public async Task SmtpCredentialFailureCanBeRetriedAndMissingSetupCannotStorePassword()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var credentials = new TestCredentialStore { FailWrites = true };
        var profile = TestDirectory.Profile() with { OutgoingServer = SmtpServer() };
        var model = new AccountProfileViewModel(store, credentials, new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);
        await model.SavePasswordAsync("synthetic", MailProtocol.Smtp);
        Assert.StartsWith("Password not saved", model.Status);
        credentials.FailWrites = false;
        await model.SavePasswordAsync("synthetic", MailProtocol.Smtp);
        Assert.Contains("Password saved", model.Status);
        var unconfigured = new AccountProfileViewModel(store, credentials, new TestMailReceiver(), new ImmediateUiDispatcher(), profile with { OutgoingServer = null }, null);
        Assert.False(unconfigured.CanManageSmtpPassword);
        await unconfigured.SavePasswordAsync("synthetic", MailProtocol.Smtp);
        Assert.Contains("Configure and save SMTP", unconfigured.Status);
    }

    [Theory]
    [InlineData(TransportSecurity.Tls, "465")]
    [InlineData(TransportSecurity.StartTls, "587")]
    public async Task SmtpSettingsSurviveRestartWithoutChangingImapOrItsCredential(TransportSecurity security, string port)
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile();
        await store.SaveAsync(profile);
        var credentials = new TestCredentialStore();
        var receiver = new TestMailReceiver();
        var imapKey = CredentialKey.For(profile, MailProtocol.Imap);
        await credentials.WriteAsync(imapKey, "synthetic-imap-secret");
        var model = new AccountProfileViewModel(store, credentials, receiver, new ImmediateUiDispatcher(), profile, null);
        Configure(model);
        model.SmtpSecurity = security;
        model.SmtpPort = port;
        await model.SaveAsync();

        var saved = Assert.Single(await new JsonAccountStore(directory.File("accounts.json")).LoadAsync());
        Assert.Equal(profile.Id, saved.Id);
        Assert.Equal(profile.IncomingServer, saved.IncomingServer);
        Assert.Equal(new MailServerSettings
        {
            Host = "smtp.example.test", Port = int.Parse(port), UserName = "outgoing@example.test",
            Security = security, Authentication = AuthenticationMethod.Password,
        }, saved.OutgoingServer);
        var restarted = new AccountProfileViewModel(store, credentials, receiver, new ImmediateUiDispatcher(), saved, null);
        Assert.True(restarted.ConfigureSmtp);
        Assert.Equal("smtp.example.test", restarted.SmtpHost);
        Assert.Equal(port, restarted.SmtpPort);
        Assert.Equal(security, restarted.SmtpSecurity);
        Assert.Equal("outgoing@example.test", restarted.SmtpUserName);
        Assert.Equal(AuthenticationMethod.Password, restarted.SmtpAuthentication);
        Assert.Equal("synthetic-imap-secret", await credentials.ReadAsync(CredentialKey.For(saved, MailProtocol.Imap)));
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(saved, MailProtocol.Smtp)));
        Assert.DoesNotContain("synthetic-imap-secret", await File.ReadAllTextAsync(directory.File("accounts.json")));
        Assert.Equal(0, receiver.Calls);
    }

    [Fact]
    public async Task LegacyProfileWithoutOutgoingPropertyLoadsAndCanStillBeEdited()
    {
        using var directory = new TestDirectory();
        string path = directory.File("accounts.json");
        var store = new JsonAccountStore(path);
        var profile = TestDirectory.Profile();
        await store.SaveAsync(profile);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        document["data"]![0]!.AsObject().Remove("outgoingServer");
        await File.WriteAllTextAsync(path, document.ToJsonString());
        var loaded = Assert.Single(await store.LoadAsync());
        var model = Model(store, loaded);
        Assert.False(model.ConfigureSmtp);
        Assert.Equal("587", model.SmtpPort);
        Assert.Equal(TransportSecurity.StartTls, model.SmtpSecurity);
        model.DisplayName = "Renamed";
        model.SmtpPort = "ignored while not configured";
        await model.SaveAsync();
        var saved = Assert.Single(await store.LoadAsync());
        Assert.Null(saved.OutgoingServer);
        Assert.Equal("Renamed", saved.DisplayName);
        Assert.Equal(profile.Id, saved.Id);
    }

    [Theory]
    [InlineData("host", "")]
    [InlineData("host", "https://smtp.example.test")]
    [InlineData("host", "smtp.example.test:587")]
    [InlineData("port", "abc")]
    [InlineData("port", "0")]
    [InlineData("port", "65536")]
    [InlineData("user", " ")]
    [InlineData("security", "")]
    [InlineData("authentication", "")]
    public async Task InvalidSmtpSettingsDoNotOverwriteSavedProfile(string field, string value)
    {
        using var directory = new TestDirectory();
        string path = directory.File("accounts.json");
        var store = new JsonAccountStore(path);
        var profile = TestDirectory.Profile() with { OutgoingServer = SmtpServer() };
        await store.SaveAsync(profile);
        string original = await File.ReadAllTextAsync(path);
        var model = Model(store, profile);
        switch (field)
        {
            case "host": model.SmtpHost = value; break;
            case "port": model.SmtpPort = value; break;
            case "user": model.SmtpUserName = value; break;
            case "security": model.SmtpSecurity = (TransportSecurity)99; break;
            case "authentication": model.SmtpAuthentication = (AuthenticationMethod)99; break;
        }
        await model.SaveAsync();
        Assert.StartsWith("Not saved:", model.Status);
        Assert.Equal(profile, model.Profile);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.True(model.CanSave);
    }

    [Fact]
    public async Task TurningOffSmtpRemovesOnlyOutgoingConfiguration()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile() with { OutgoingServer = SmtpServer() };
        await store.SaveAsync(profile);
        var model = Model(store, profile);
        model.ConfigureSmtp = false;
        await model.SaveAsync();
        Assert.Equal(profile with { OutgoingServer = null }, Assert.Single(await store.LoadAsync()));
    }

    [Fact]
    public async Task FormEnablesOptionalFieldsAndSavesTheirValues()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile();
        await store.SaveAsync(profile);
        var model = Model(store, profile);
        using var form = new AccountProfileView(model).CreateContent();
        var controls = Descendants(form).OfType<StandardLabel>().Where(label => label.Target is not null)
            .ToDictionary(label => label.Text, label => label.Target!);
        var smtpHost = Assert.IsType<StandardEdit>(controls["SMTP server (hostname only)"]);
        var smtpPort = Assert.IsType<StandardEdit>(controls["SMTP port"]);
        var smtpUser = Assert.IsType<StandardEdit>(controls["SMTP username"]);
        var setup = Assert.IsType<StandardComboBox>(controls["Outgoing mail setup"]);
        Assert.False(smtpHost.IsEnabled);
        setup.SelectedIndex = 1;
        Assert.True(smtpHost.IsEnabled);
        smtpHost.Text = "smtp.example.test";
        smtpPort.Text = "465";
        smtpUser.Text = "outgoing@example.test";
        Assert.IsType<StandardComboBox>(controls["SMTP connection security"]).SelectedIndex = 0;
        await SaveFormAsync(form, model);
        Assert.Equal(SmtpServer() with { Port = 465, Security = TransportSecurity.Tls }, model.Profile!.OutgoingServer);
        Assert.True(smtpHost.IsEnabled);
        setup.SelectedIndex = 0;
        Assert.False(smtpHost.IsEnabled);
        Assert.False(smtpPort.IsEnabled);
        Assert.False(smtpUser.IsEnabled);
    }

    [Fact]
    public async Task EditingOtherFormFieldsPreservesAnExistingUnsupportedAuthenticationMode()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile() with { OutgoingServer = SmtpServer() with { Authentication = AuthenticationMethod.OAuth2 } };
        await store.SaveAsync(profile);
        var model = Model(store, profile);
        using var form = new AccountProfileView(model).CreateContent();
        var label = Descendants(form).OfType<StandardLabel>().Single(item => item.Text == "Display name");
        Assert.IsType<StandardEdit>(label.Target).Text = "Renamed";
        await SaveFormAsync(form, model);
        var saved = Assert.Single(await store.LoadAsync());
        Assert.Equal("Renamed", saved.DisplayName);
        Assert.Equal(profile.OutgoingServer, saved.OutgoingServer);
    }

    [Fact]
    public async Task UnsavedSmtpChangesBlockImapPasswordOperationsUntilSaved()
    {
        using var directory = new TestDirectory();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        var profile = TestDirectory.Profile();
        await store.SaveAsync(profile);
        var credentials = new TestCredentialStore();
        var receiver = new TestMailReceiver();
        var model = new AccountProfileViewModel(store, credentials, receiver, new ImmediateUiDispatcher(), profile, null);
        Configure(model);
        await model.SavePasswordAsync("synthetic-secret");
        await model.TestConnectionAsync();
        Assert.Contains("Save your account changes", model.Status);
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Imap)));
        Assert.Equal(0, receiver.Calls);
        await model.SaveAsync();
        await model.SavePasswordAsync("synthetic-secret");
        await model.TestConnectionAsync();
        Assert.Equal(1, receiver.Calls);
        Assert.Equal("synthetic-secret", await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Imap)));
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(model.Profile!, MailProtocol.Smtp)));
    }

    private static AccountProfileViewModel Model(JsonAccountStore store, AccountProfile profile) =>
        new(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);

    private static async Task SaveFormAsync(UiElement form, AccountProfileViewModel model)
    {
        // Capture through the user's button and wait for asynchronous persistence/UI completion.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs args) { if (!model.IsBusy) completion.TrySetResult(); }
        model.Changed += Changed;
        try
        {
            Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Save account").Click();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { model.Changed -= Changed; }
    }

    private static MailServerSettings SmtpServer() => new()
    {
        Host = "smtp.example.test", Port = 587, UserName = "outgoing@example.test",
        Security = TransportSecurity.StartTls, Authentication = AuthenticationMethod.Password,
    };

    private static void Configure(AccountProfileViewModel model)
    {
        model.ConfigureSmtp = true;
        model.SmtpHost = " smtp.example.test ";
        model.SmtpPort = "587";
        model.SmtpUserName = " outgoing@example.test ";
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var element in Descendants(child)) yield return element;
    }
}
