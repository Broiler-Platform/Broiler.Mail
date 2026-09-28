using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class CredentialWorkflowTests
{
    [Fact]
    public async Task PasswordOperationsNeverPersistSecretInProfileJson()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore();
        var profile = TestDirectory.Profile();
        var store = new JsonAccountStore(directory.File("accounts.json"));
        await store.SaveAsync(profile);
        string json = await File.ReadAllTextAsync(directory.File("accounts.json"));
        var model = new AccountProfileViewModel(store, credentials, new TestMailReceiver(), new ImmediateUiDispatcher(), profile, null);
        await model.SavePasswordAsync(LocalImapServer.Password);
        Assert.Contains("Password saved", model.Status);
        Assert.Equal(json, await File.ReadAllTextAsync(directory.File("accounts.json")));
        Assert.DoesNotContain(LocalImapServer.Password, json);
        Assert.Equal(LocalImapServer.Password, await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Imap)));
        await model.ForgetPasswordAsync();
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Imap)));
        Assert.Contains("removed", model.Status);
    }

    [Fact]
    public async Task UnsavedServerEditsBlockBothPasswordSaveAndConnectionTest()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore();
        var receiver = new TestMailReceiver();
        var profile = TestDirectory.Profile();
        var model = Model(directory, credentials, receiver, profile);
        model.Host = "different.example.test";
        await model.SavePasswordAsync(LocalImapServer.Password);
        Assert.Null(await credentials.ReadAsync(CredentialKey.For(profile, MailProtocol.Imap)));
        Assert.Contains("Save your account changes", model.Status);
        await model.TestConnectionAsync();
        Assert.Equal(0, receiver.Calls);
        Assert.Contains("Save your account changes", model.Status);
    }

    [Fact]
    public async Task CredentialStoreFailureIsVisibleAndRetryWorks()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore { FailWrites = true };
        var model = Model(directory, credentials, new TestMailReceiver(), TestDirectory.Profile());
        await model.SavePasswordAsync(LocalImapServer.Password);
        Assert.StartsWith("Password not saved", model.Status);
        Assert.True(model.CanManagePassword);
        credentials.FailWrites = false;
        await model.SavePasswordAsync(LocalImapServer.Password);
        Assert.Contains("Password saved", model.Status);
    }

    [Fact]
    public async Task TestCanBeCanceledAndRetriedWithoutParallelOperations()
    {
        using var directory = new TestDirectory();
        var receiver = new TestMailReceiver { Test = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        var model = Model(directory, new TestCredentialStore(), receiver, TestDirectory.Profile());
        var pending = model.TestConnectionAsync();
        Assert.True(model.CanCancelTest);
        Assert.False(model.CanSave);
        await model.TestConnectionAsync();
        Assert.Equal(1, receiver.Calls);
        model.CancelConnectionTest();
        await pending;
        Assert.Contains("canceled", model.Status);
        Assert.True(model.CanSave);
        Assert.False(model.CanCancelTest);
        receiver.Test = _ => Task.CompletedTask;
        await model.TestConnectionAsync();
        Assert.Equal(2, receiver.Calls);
        Assert.Contains("authenticated", model.Status);
    }

    [Fact]
    public void PasswordControlIsMaskedAndClearedAfterExplicitSave()
    {
        using var directory = new TestDirectory();
        var credentials = new TestCredentialStore();
        var model = Model(directory, credentials, new TestMailReceiver(), TestDirectory.Profile());
        using var form = new AccountProfileView(model).CreateContent();
        var password = Descendants(form).OfType<StandardEdit>().Single(edit => edit.IsPassword);
        var test = Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Test connection");
        password.Text = LocalImapServer.Password;
        Assert.False(test.IsEnabled); // Never silently test a different, previously saved password.
        Descendants(form).OfType<StandardButton>().Single(button => button.Text == "Save password").Click();
        Assert.Equal(string.Empty, password.Text);
        Assert.True(test.IsEnabled);
        Assert.Contains("Password saved", model.Status);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("username")]
    [InlineData("tls")]
    [InlineData("auth")]
    public void EveryConnectionIdentityChangeChangesTheCredentialBinding(string part)
    {
        var profile = TestDirectory.Profile();
        var server = profile.IncomingServer;
        var changed = part switch
        {
            "host" => server with { Host = "other.example.test" },
            "port" => server with { Port = 143 },
            "username" => server with { UserName = "other" },
            "tls" => server with { Security = TransportSecurity.StartTls },
            _ => server with { Authentication = AuthenticationMethod.OAuth2 },
        };
        Assert.NotEqual(CredentialKey.For(profile, MailProtocol.Imap).Binding,
            CredentialKey.For(profile with { IncomingServer = changed }, MailProtocol.Imap).Binding);
    }

    private static AccountProfileViewModel Model(TestDirectory directory, TestCredentialStore credentials, TestMailReceiver receiver, AccountProfile profile) =>
        new(new JsonAccountStore(directory.File("accounts.json")), credentials, receiver, new ImmediateUiDispatcher(), profile, null);

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var element in Descendants(child)) yield return element;
    }
}
