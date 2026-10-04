// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    Medium
// Criteria:         2/0
// Resource impact:  2/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.UI.Forms.Standard;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Panel.Standard;

namespace Broiler.Mail.Application.Views;

// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=C168E3
// Broiler-Falsified-If: choosing a theme in the list saves a different AppTheme, because the list order no longer matches the enum
// Broiler-Human:        PENDING
public sealed class SettingsView(SettingsViewModel model)
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=6A099B
    // Broiler-Falsified-If: choosing a theme in the list saves a different AppTheme, because the list order no longer matches the enum
    // Broiler-Human:        PENDING
    public UiElement CreateContent()
    {
        var panel = new StandardPanel { Spacing = 20 };
        var appearance = ConfigurationForm.AddSection(panel, "Appearance", "Saved appearance and inbox spacing apply immediately. The system high-contrast mode takes precedence.");
        var theme = new StandardComboBox();
        theme.SetItems([new UiComboBoxItem("System", "Use system theme"), new UiComboBoxItem("Light", "Light"), new UiComboBoxItem("Dark", "Dark")]);
        theme.SelectedIndex = (int)model.Theme;
        ConfigurationForm.AddLabeledControl(appearance, "Theme", theme);
        var density = new StandardComboBox();
        density.SetItems([new UiComboBoxItem("Comfortable", "Comfortable"), new UiComboBoxItem("Compact", "Compact")]);
        density.SelectedIndex = (int)model.InboxDensity;
        ConfigurationForm.AddLabeledControl(appearance, "Inbox row spacing", density);
        var window = ConfigurationForm.AddSection(panel, "Window size", "Broiler.Mail reopens at its last size and position. A size saved here is used at the next start instead.");
        var width = ConfigurationForm.AddField(window, "Initial window width (640–7680)", model.WindowWidth);
        var height = ConfigurationForm.AddField(window, "Initial window height (480–4320)", model.WindowHeight);
        // Listed from the same table the key handling uses; collapsed so the settings stay short.
        var shortcuts = new FormSection("Keyboard shortcuts", "", collapsible: true, expanded: false)
        {
            // In sentence case, as every other command; composed from the title it would read "Show Keyboard shortcuts".
            ShowText = "Show keyboard shortcuts", HideText = "Hide keyboard shortcuts",
        };
        // Named for the toggle that controls it, as the composer's Cc and Bcc are.
        shortcuts.Content.AccessibleName = "Keyboard shortcuts";
        panel.AddChild(shortcuts);
        foreach (var shortcut in MailShortcuts.All)
            shortcuts.Content.AddChild(new StandardLabel
            {
                Text = $"{shortcut.Gesture}: {shortcut.Description}", UseMnemonic = false,
                Wrapping = UiTextWrapping.Wrap,
            });
        var save = new StandardButton { Text = "Save settings", IsDefault = true };
        var status = new InlineFeedback();
        var surface = ConfigurationForm.NameFeedback(new FormSurface(panel, FormSurface.ActionBar(save), status));

        void RefreshState()
        {
            if (panel.IsDisposed)
                return;
            theme.IsEnabled = model.CanSave;
            density.IsEnabled = model.CanSave;
            width.IsEnabled = height.IsEnabled = save.IsEnabled = model.CanSave;
        }
        model.Changed += (_, _) => RefreshState();
        ConfigurationForm.BindFeedback(model, surface, status, new Dictionary<string, Broiler.UI.Edit.Standard.StandardEdit>
        { ["WindowWidth"] = width, ["WindowHeight"] = height });
        save.Clicked += async (_, _) =>
        {
            model.Theme = (AppTheme)theme.SelectedIndex;
            model.InboxDensity = (InboxDensity)density.SelectedIndex;
            model.WindowWidth = width.Text;
            model.WindowHeight = height.Text;
            await model.SaveAsync();
        };
        RefreshState();
        return surface;
    }
}
