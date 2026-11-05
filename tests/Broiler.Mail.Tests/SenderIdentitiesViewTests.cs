using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class SenderIdentitiesViewTests
{
    [Fact]
    public async Task EditorPersistsOnlyChosenAddressAndResetRestoresInheritance()
    {
        using var directory = new TestDirectory(); var account = SenderIdentitySettingsTests.Profile(); var store = new JsonAccountStore(directory.File("accounts.json")); await store.SaveAsync(account);
        var model = new AccountProfileViewModel(store, new TestCredentialStore(), new TestMailReceiver(), new ImmediateUiDispatcher(), account, null);
        using var root = new AccountProfileView(model).CreateContent();
        var address = All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Sender address to customize"); address.SelectedIndex = 1;
        All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Sender display-name policy").SelectedIndex = 1;
        ((StandardEdit)All(root).OfType<FormField>().Single(item => item.Label.Text == "Sender display-name override").Control).Text = "Alias Name";
        All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Sender new-message and forward signature policy").SelectedIndex = 1;
        All(root).OfType<StandardRichEdit>().Single(item => item.AccessibleName == "Sender signature override").SetPlainText("Alias signature");
        All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Sender reply signature policy").SelectedIndex = 1;
        Assert.True(model.HasUnsavedChanges); await model.SaveAsync();
        var saved = (await store.LoadAsync()).Single(); var row = saved.SenderIdentities!.Single(); Assert.Equal("alias@example.test", row.Address); Assert.Equal("Alias Name", row.DisplayName); Assert.Equal("Alias signature", row.Signature); Assert.Equal("", row.ReplySignature);
        Assert.Equal("Account Name", saved.SenderDisplayName(saved.EmailAddress));
        All(root).OfType<StandardButton>().Single(item => item.Text == "Reset selected sender overrides to inherited settings").Click(); await model.SaveAsync(); Assert.Null((await store.LoadAsync()).Single().SenderIdentities);
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var row in All(child)) yield return row; }
}
