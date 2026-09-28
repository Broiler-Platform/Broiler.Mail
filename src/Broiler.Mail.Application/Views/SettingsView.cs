using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Panel.Standard;

namespace Broiler.Mail.Application.Views;

public sealed class SettingsView(SettingsViewModel model)
{
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
