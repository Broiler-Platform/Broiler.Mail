using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Views;

internal static class ConfigurationForm
{
    public static StandardEdit AddField(StandardPanel panel, string label, string value)
    {
        var field = new StandardEdit { Text = value, MaxLength = 320 };
        AddLabeledControl(panel, label, field);
        return field;
    }

    public static void AddLabeledControl(StandardPanel panel, string label, UiElement control)
    {
        panel.AddChild(new StandardLabel { Text = label, Target = control, Foreground = StandardControlPaint.Text });
        panel.AddChild(control);
    }

    public static StandardLabel AddText(StandardPanel panel, string text)
    {
        var label = new StandardLabel { Text = text, Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
        panel.AddChild(label);
        return label;
    }

    public static ViewportScrollView Wrap(StandardPanel panel) => new(panel);
}
