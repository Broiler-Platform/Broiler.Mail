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

using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
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
        var panel = new StandardPanel { Spacing = 8 };
        ConfigurationForm.AddText(panel, "Settings");
        ConfigurationForm.AddText(panel, "Theme and initial window size apply on the next start.");
        var theme = new StandardComboBox();
        theme.SetItems([new UiComboBoxItem("System", "Use Windows theme"), new UiComboBoxItem("Light", "Light"), new UiComboBoxItem("Dark", "Dark")]);
        theme.SelectedIndex = (int)model.Theme;
        ConfigurationForm.AddLabeledControl(panel, "Theme", theme);
        var width = ConfigurationForm.AddField(panel, "Initial window width (640–7680)", model.WindowWidth);
        var height = ConfigurationForm.AddField(panel, "Initial window height (480–4320)", model.WindowHeight);
        var save = new StandardButton { Text = "Save settings" };
        panel.AddChild(save);
        var status = ConfigurationForm.AddText(panel, model.Status);

        void RefreshState()
        {
            if (panel.IsDisposed)
                return;
            theme.IsEnabled = model.CanSave;
            width.IsEnabled = height.IsEnabled = save.IsEnabled = model.CanSave;
            status.Text = model.Status;
        }
        model.Changed += (_, _) => RefreshState();
        save.Clicked += async (_, _) =>
        {
            model.Theme = (AppTheme)theme.SelectedIndex;
            model.WindowWidth = width.Text;
            model.WindowHeight = height.Text;
            await model.SaveAsync();
        };
        RefreshState();
        return ConfigurationForm.Wrap(panel);
    }
}
