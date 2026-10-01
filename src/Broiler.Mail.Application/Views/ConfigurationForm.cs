// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   5
// Annotated:        5/5
// Exempt:           0
// Human-reviewed:   0/5
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  1/10 max
// Unverified:       5
//
// GENERATED - DO NOT EDIT MANUALLY

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

// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=CF90B6
// Broiler-Falsified-If: a saved value longer than 320 characters, such as a Sent folder path the validator accepts up to 512, is shortened when its field is created
// Broiler-Human:        PENDING
internal static class ConfigurationForm
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=7CD629
    // Broiler-Falsified-If: a saved value longer than 320 characters, such as a Sent folder path the validator accepts up to 512, is shortened when its field is created
    // Broiler-Human:        PENDING
    public static StandardEdit AddField(StandardPanel panel, string label, string value)
    {
        var field = new StandardEdit { Text = value, MaxLength = 320 };
        AddLabeledControl(panel, label, field);
        return field;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=1B2906
    // Broiler-Human:        PENDING
    public static void AddLabeledControl(StandardPanel panel, string label, UiElement control)
    {
        panel.AddChild(new FormField(label, control));
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=957C75
    // Broiler-Human:        PENDING
    public static StandardLabel AddText(StandardPanel panel, string text)
    {
        var label = new StandardLabel { Text = text, Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
        panel.AddChild(label);
        return label;
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=64528A
    // Broiler-Human:        PENDING
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
