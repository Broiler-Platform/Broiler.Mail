using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class SavedRecipientsViewTests
{
    [Fact]
    public async Task ExplicitBccSelectionUpdatesVisibleFieldAndRemovalLeavesDraftUntouched()
    {
        using var directory = new TestDirectory(); var account = TestDirectory.Profile(); var book = new JsonRecipientBook(directory.File("recipients.json"));
        await book.SaveAsync(new(account.Id, "saved@example.test", "Saved person"));
        using var composer = new ComposerViewModel(recipientBook: book); composer.SetAccount(account); await composer.RecipientBook!.LoadTask; composer.StartNew();
        using var inbox = new InboxViewModel(new TestMailReceiver(), new ImmediateUiDispatcher());
        using var root = new ComposerView(composer, inbox).CreateContent();
        var section = All(root).OfType<FormSection>().Single(item => item.Toggle?.Text.Contains("Saved recipients", StringComparison.Ordinal) == true); section.IsExpanded = true;
        var picker = All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Saved recipient matches (at most 10)"); picker.SelectedIndex = 0;
        var target = All(root).OfType<StandardComboBox>().Single(item => item.AccessibleName == "Recipient destination field"); target.SelectedIndex = 2;
        All(root).OfType<StandardButton>().Single(item => item.Text == "Insert selected recipient").Click();
        Assert.Equal("saved@example.test", composer.Bcc); Assert.Empty(composer.To); Assert.Empty(composer.Cc);
        var field = All(root).OfType<FormField>().Single(item => item.Label.Text == "Bcc"); Assert.Equal(composer.Bcc, ((StandardEdit)field.Control).Text);
        Assert.True(All(root).OfType<FormSection>().Single(item => item.Toggle?.Text.Contains("Cc and Bcc", StringComparison.Ordinal) == true).IsExpanded);
        await composer.RecipientBook.RemoveAsync("saved@example.test");
        Assert.Empty(picker.Items); Assert.Equal("saved@example.test", composer.Bcc); Assert.Empty(await book.LoadAsync(account.Id));
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
}
