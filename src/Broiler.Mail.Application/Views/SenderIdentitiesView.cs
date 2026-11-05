using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>Edits non-secret per-address defaults in the account's ordinary Save transaction.</summary>
internal static class SenderIdentitiesView
{
    internal static UiElement Create(AccountProfileViewModel model)
    {
        var panel = new StandardPanel { Spacing = 8, AccessibleName = "Per-address sender defaults" };
        ConfigurationForm.AddText(panel, "Per-address names and signatures affect new drafts only. Inherit follows account defaults; an empty override omits the name or signature. These settings do not authorize a new sender address. Save the account to apply edits.");
        var address = new StandardComboBox { AccessibleName = "Sender address to customize" }; panel.AddChild(address);
        StandardComboBox Policy(string label)
        {
            var combo = new StandardComboBox { AccessibleName = label };
            combo.SetItems([new UiComboBoxItem("Inherit", "Inherit account policy"), new UiComboBoxItem("Override", "Use address override (blank omits)")]);
            ConfigurationForm.AddLabeledControl(panel, label, combo); return combo;
        }
        var nameMode = Policy("Sender display-name policy");
        var name = ConfigurationForm.AddField(panel, "Sender display-name override", ""); name.MaxLength = 200;
        var signatureMode = Policy("Sender new-message and forward signature policy");
        var signature = new StandardRichEdit { AcceptsReturn = true, AccessibleName = "Sender signature override", PreferredSize = new BSize(520, 90) }; panel.AddChild(signature);
        var replyMode = Policy("Sender reply signature policy");
        var reply = new StandardRichEdit { AcceptsReturn = true, AccessibleName = "Sender reply signature override", PreferredSize = new BSize(520, 90) }; panel.AddChild(reply);
        var reset = new StandardButton { Text = "Reset selected sender overrides to inherited settings" }; panel.AddChild(reset);
        string[] shown = []; string? selected = null; bool updating = false;
        string? Selected() => address.SelectedIndex >= 0 && address.SelectedIndex < shown.Length ? shown[address.SelectedIndex] : null;
        void LoadFields()
        {
            var row = model.SenderIdentities?.SingleOrDefault(item => item.Address == Selected());
            nameMode.SelectedIndex = row?.DisplayName is null ? 0 : 1; name.Text = row?.DisplayName ?? "";
            signatureMode.SelectedIndex = row?.Signature is null ? 0 : 1; signature.SetPlainText(row?.Signature ?? "");
            replyMode.SelectedIndex = row?.ReplySignature is null ? 0 : 1; reply.SetPlainText(row?.ReplySignature ?? "");
        }
        void Refresh(bool load = false)
        {
            if (panel.IsDisposed) return;
            updating = true;
            var rows = new[] { model.EmailAddress.Trim() }.Concat((model.SenderIdentities ?? []).Select(item => item.Address))
                .Concat(model.SenderAliases.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(16).Select(value => value.Trim()))
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Take(34).ToArray();
            if (!shown.SequenceEqual(rows))
            {
                shown = rows; address.SetItems(rows.Select(value => new UiComboBoxItem(value, value)));
                address.SelectedIndex = Array.IndexOf(rows, selected ?? model.EmailAddress.Trim());
            }
            if (load || selected != Selected()) { selected = Selected(); LoadFields(); }
            bool editable = model.CanSave && selected is not null;
            address.IsEnabled = nameMode.IsEnabled = signatureMode.IsEnabled = replyMode.IsEnabled = reset.IsEnabled = editable;
            name.IsEnabled = editable && nameMode.SelectedIndex == 1;
            signature.IsReadOnly = !editable || signatureMode.SelectedIndex != 1; reply.IsReadOnly = !editable || replyMode.SelectedIndex != 1;
            updating = false;
        }
        void Capture()
        {
            if (updating || !model.CanSave || Selected() is not { } value) return;
            var row = new SenderIdentitySettings { Address = value, DisplayName = nameMode.SelectedIndex == 1 ? name.Text : null,
                Signature = signatureMode.SelectedIndex == 1 ? signature.GetPlainText() : null, ReplySignature = replyMode.SelectedIndex == 1 ? reply.GetPlainText() : null };
            var rows = (model.SenderIdentities ?? []).Where(item => item.Address != value).ToList();
            if (row.DisplayName is not null || row.Signature is not null || row.ReplySignature is not null) rows.Add(row);
            model.SenderIdentities = rows.Count == 0 ? null : rows.ToArray(); model.NotifyEdited();
        }
        address.SelectionChanged += (_, _) => { if (!updating) Refresh(load: true); };
        name.TextChanged += (_, _) => Capture(); signature.DocumentChanged += (_, _) => Capture(); reply.DocumentChanged += (_, _) => Capture();
        nameMode.SelectionChanged += (_, _) => Capture(); signatureMode.SelectionChanged += (_, _) => Capture(); replyMode.SelectionChanged += (_, _) => Capture();
        reset.Clicked += (_, _) =>
        {
            if (!model.CanSave || Selected() is not { } value) return;
            var rows = (model.SenderIdentities ?? []).Where(item => item.Address != value).ToArray(); model.SenderIdentities = rows.Length == 0 ? null : rows;
            Refresh(load: true); model.NotifyEdited();
        };
        model.Changed += (_, _) => Refresh(); model.ProfileSelected += (_, _) => { selected = null; shown = []; Refresh(load: true); };
        Refresh(load: true); return panel;
    }
}
