using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.ComboBox;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Deliberate local suggestions, keyboard-operable and separate from send actions.</summary>
internal static class SavedRecipientsView
{
    internal static UiElement Create(ComposerViewModel composer, Action<string>? inserted = null)
    {
        var model = composer.RecipientBook!;
        var section = new FormSection("Saved recipients", "Local to this sender account", collapsible: true, expanded: false);
        ConfigurationForm.AddText(section.Content, "Save addresses explicitly. No recipients are collected from messages or Bcc. Suggestions stay on this device and belong to the draft's sender account.");
        var prefix = new StandardEdit { MaxLength = 320, PlaceholderText = "Start of name or address" };
        section.Content.AddChild(new FormField("Find saved recipient", prefix));
        var choices = new StandardComboBox { AccessibleName = "Saved recipient matches (at most 10)" };
        section.Content.AddChild(choices);
        var target = new StandardComboBox { AccessibleName = "Recipient destination field" };
        target.SetItems(new[] { "To", "Cc", "Bcc" }.Select(value => new UiComboBoxItem(value, value))); target.SelectedIndex = 0;
        section.Content.AddChild(target);
        var use = new StandardButton { Text = "Insert selected recipient" }; section.Content.AddChild(use);
        var address = new StandardEdit { MaxLength = 320, PlaceholderText = "person@example.com" };
        var name = new StandardEdit { MaxLength = 120, PlaceholderText = "Optional name for suggestions" };
        section.Content.AddChild(new FormField("Saved email address", address));
        section.Content.AddChild(new FormField("Saved recipient name", name));
        var save = new StandardButton { Text = "Save recipient name and address" }; section.Content.AddChild(save);
        var remove = new StandardButton { Text = "Remove selected saved recipient" }; section.Content.AddChild(remove);
        var reload = new StandardButton { Text = "Reload saved recipients" }; section.Content.AddChild(reload);
        var feedback = new InlineFeedback(); section.Content.AddChild(feedback);
        SavedRecipient[] shown = []; bool updating = false; Guid? shownAccount = null;
        SavedRecipient? Selected() => choices.SelectedIndex >= 0 && choices.SelectedIndex < shown.Length ? shown[choices.SelectedIndex] : null;
        void Refresh()
        {
            if (section.IsDisposed) return;
            updating = true;
            var account = composer.DraftAccountId?.Value;
            if (account != shownAccount) { shownAccount = account; prefix.Text = address.Text = name.Text = ""; }
            var selected = Selected(); var rows = model.Suggest(prefix.Text).ToArray();
            if (!shown.SequenceEqual(rows))
            {
                shown = rows; choices.SetItems(rows.Select(row => new UiComboBoxItem(row.Label, row.Address)));
                choices.SelectedIndex = selected is null ? -1 : Array.FindIndex(rows, row => row.Address.Equals(selected.Address, StringComparison.OrdinalIgnoreCase));
            }
            bool ready = composer.CanEdit && model.IsReady && !model.IsBusy;
            section.Visibility = composer.HasDraft ? UiVisibility.Visible : UiVisibility.Collapsed;
            prefix.IsEnabled = choices.IsEnabled = target.IsEnabled = ready;
            address.IsEnabled = name.IsEnabled = save.IsEnabled = ready;
            use.IsEnabled = remove.IsEnabled = ready && Selected() is not null;
            reload.IsEnabled = composer.CanEdit && !model.IsBusy;
            feedback.Set(model.Status, model.IsError ? FeedbackKind.Error : model.IsBusy ? FeedbackKind.Progress : FeedbackKind.Information);
            updating = false;
        }
        prefix.TextChanged += (_, _) => { if (!updating) Refresh(); };
        choices.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            if (Selected() is { } row) { address.Text = row.Address; name.Text = row.Name; }
            Refresh();
        };
        save.Clicked += async (_, _) => await model.SaveAsync(address.Text, name.Text);
        remove.Clicked += async (_, _) => { if (Selected() is { } row) await model.RemoveAsync(row.Address); };
        reload.Clicked += async (_, _) => await model.ReloadAsync();
        use.Clicked += (_, _) =>
        {
            if (Selected() is { } row && target.SelectedIndex is >= 0 and <= 2)
            {
                string field = new[] { "To", "Cc", "Bcc" }[target.SelectedIndex];
                if (composer.InsertRecipient(field, row)) inserted?.Invoke(field);
            }
        };
        composer.Changed += (_, _) => Refresh(); Refresh(); return section;
    }
}
