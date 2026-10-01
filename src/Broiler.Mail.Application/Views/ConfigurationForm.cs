using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.Mail.Application.ViewModels;

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
        panel.AddChild(new FormField(label, control));
    }

    public static StandardLabel AddText(StandardPanel panel, string text)
    {
        var label = new StandardLabel { Text = text, Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
        panel.AddChild(label);
        return label;
    }

    public static ViewportScrollView Wrap(StandardPanel panel) => new(panel);

    public static StandardPanel AddSection(StandardPanel panel, string title, string description = "")
    {
        var section = new FormSection(title, description);
        panel.AddChild(section);
        return section.Content;
    }

    public static void BindFeedback(SaveViewModel model, FormSurface surface, InlineFeedback status,
        IReadOnlyDictionary<string, StandardEdit> fields)
    {
        string? lastField = null;
        void Refresh()
        {
            if (surface.IsDisposed) return;
            status.Set(model.Status, model.StatusKind);
            foreach (var pair in fields)
                FieldFor(pair.Value).SetError(pair.Key == model.ValidationField ? model.ValidationMessage : null);
            if (model.ValidationField is { } field && field != lastField && fields.TryGetValue(field, out var edit))
                surface.Reveal(FieldFor(edit));
            lastField = model.ValidationField;
        }
        model.Changed += (_, _) => Refresh();
        Refresh();
    }

    private static FormField FieldFor(UiElement control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            if (parent is FormField field) return field;
        throw new InvalidOperationException("The control is not in a form field.");
    }
}
