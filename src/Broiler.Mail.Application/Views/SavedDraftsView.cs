using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Panel.Standard;

namespace Broiler.Mail.Application.Views;

internal static class SavedDraftsView
{
    internal static UiElement Create(ComposerViewModel model)
    {
        var section = new FormSection("Saved drafts", "Local drafts and submission records; nothing sends automatically.", collapsible: true, expanded: false);
        var park = new StandardButton { Text = "Save and close current draft" }; section.Content.AddChild(park);
        var picker = new StandardComboBox { AccessibleName = "Saved drafts for selected account" }; section.Content.AddChild(picker);
        var resume = new StandardButton { Text = "Resume selected draft (save current first)" }; section.Content.AddChild(resume);
        var remove = new StandardButton { Text = "Remove selected saved draft…" }; section.Content.AddChild(remove);
        var confirmation = new StandardPanel { Spacing = 8 };
        var notice = ConfigurationForm.AddText(confirmation, "");
        var keep = new StandardButton { Text = "Keep saved draft" }; confirmation.AddChild(keep);
        var confirm = new StandardButton { Text = "Confirm permanent local draft removal" }; confirmation.AddChild(confirm);
        section.Content.AddChild(confirmation);
        ConfigurationForm.AddText(section.Content, "Choose the sender account to browse its saved drafts. Resuming saves any open draft, including another account's draft. Removal deletes only the selected local record; it does not recall sent mail. Keep accepted/unknown records until you have checked their outcome.");
        DraftSnapshot[] shown = []; bool updating = false;
        DraftSnapshot? Selected() => picker.SelectedIndex >= 0 && picker.SelectedIndex < shown.Length ? shown[picker.SelectedIndex] : null;
        static string Title(DraftSnapshot row)
        {
            string subject = new(row.Draft.Subject.Select(c => char.IsControl(c) ? ' ' : c).Take(100).ToArray());
            string outcome = row.State == DraftSubmissionState.Sending ? "Unknown (interrupted sending)" : row.State.ToString();
            string copy = row.SentCopy == SentCopyState.Pending ? "Unknown (interrupted Sent copy)" : row.SentCopy.ToString();
            return (string.IsNullOrWhiteSpace(subject) ? "(No subject)" : subject) + $" · {outcome} · Sent copy: {copy}";
        }
        void Refresh()
        {
            if (section.IsDisposed) return;
            updating = true; var selected = Selected(); var rows = model.SavedDrafts.ToArray();
            if (!shown.SequenceEqual(rows))
            {
                shown = rows; picker.SetItems(rows.Select(row => new UiComboBoxItem(Title(row), row.Draft.Id.ToString())));
                picker.SelectedIndex = selected is null ? -1 : Array.FindIndex(rows, row => row.Draft.Id == selected.Draft.Id);
            }
            section.Summary = $"{shown.Length} saved for selected account; {model.SavedDraftLibraryCount}/20 across this profile.";
            park.IsEnabled = model.CanParkDraft; picker.IsEnabled = model.CanManageSavedDrafts;
            resume.IsEnabled = remove.IsEnabled = model.CanManageSavedDrafts && Selected() is not null;
            var pending = shown.SingleOrDefault(row => row.Draft.Id == model.SavedDraftRemoval);
            confirmation.Visibility = pending is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            notice.Text = pending is null ? "" : "Permanently remove this local draft/submission record: " + Title(pending) + "? This cannot be undone without a profile backup.";
            keep.IsEnabled = confirm.IsEnabled = pending is not null && model.CanManageSavedDrafts;
            updating = false;
        }
        picker.SelectionChanged += (_, _) => { if (!updating) { model.KeepSavedDraft(); Refresh(); } };
        park.Clicked += async (_, _) => await model.ParkDraftAsync();
        resume.Clicked += async (_, _) => { if (Selected() is { } row) await model.ResumeDraftAsync(row.Draft.Id); };
        remove.Clicked += (_, _) => { if (Selected() is { } row) model.RequestSavedDraftRemoval(row.Draft.Id); };
        keep.Clicked += (_, _) => model.KeepSavedDraft();
        confirm.Clicked += async (_, _) => await model.ConfirmSavedDraftRemovalAsync();
        model.Changed += (_, _) => Refresh(); Refresh(); return section;
    }
}
