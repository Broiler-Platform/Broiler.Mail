// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           1
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Application.ViewModels;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.Mail.Core.Services;
using Broiler.UI.Button;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;

namespace Broiler.Mail.Application.Views;

public sealed class ProfileStorageView(ProfileStorageViewModel model, IMailFilePicker? files = null)
{
    /// <summary>Chooses files for this view; an unavailable picker when none was given, so views keep their typed paths.</summary>
    internal IMailFilePicker Files { get; } = files ?? UnavailableMailFilePicker.NotOnThisDeviceYet;

    public UiElement CreateContent()
    {
        var panel = new StandardPanel { Spacing = 12 };
        ConfigurationForm.AddText(panel, "Profile folder: " + model.ProfileDirectory);
        ConfigurationForm.AddText(panel, "Downloaded mail and backups are not encrypted. OS credentials are stored separately. Received headers are saved automatically.");
        var usage = ConfigurationForm.AddText(panel, model.Usage);
        var refresh = new StandardButton { Text = "Refresh storage usage" }; panel.AddChild(refresh);
        var policy = ConfigurationForm.AddSection(panel, "Offline download policy", "Existing saved bodies are always kept. Download loaded messages in the inbox explicitly saves up to 500 loaded bodies, including under headers-only mode. No automatic eviction or background folder download occurs.");
        var downloads = new StandardComboBox();
        downloads.SetItems([new UiComboBoxItem("OnDemand", "Save bodies when opened"), new UiComboBoxItem("HeadersOnly", "Headers only unless explicitly downloaded")]);
        downloads.SelectedIndex = (int)model.SelectedDownloadPolicy;
        ConfigurationForm.AddLabeledControl(policy, "New body downloads", downloads);
        var quota = ConfigurationForm.AddField(policy, "Cache limit in MiB (1–16384)", model.QuotaMiB);
        var apply = new StandardButton { Text = "Apply storage policy" }; policy.AddChild(apply);
        var repair = new StandardButton { Text = "Repair confirmed local mail changes" };
        if (model.SupportsRepair) policy.AddChild(repair);
        repair.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RepairAsync());
        var uncertain = new FormSection("Uncertain server actions", "These actions are never automatically repeated. Check the message and any destination with your provider before dismissing its saved record. Dismissal creates a verified backup and keeps cached mail.", collapsible: true, expanded: false)
            { ShowText = "Show uncertain server actions", HideText = "Hide uncertain server actions" };
        uncertain.Content.AccessibleName = "Review uncertain server mutation records";
        var review = new StandardButton { Text = "Refresh uncertain actions" }; uncertain.Content.AddChild(review);
        var actionChoice = new StandardComboBox { AccessibleName = "Uncertain server action", PreferredSize = new Broiler.Graphics.Geometry.BSize(520, 32) }; uncertain.Content.AddChild(actionChoice);
        var dismiss = new StandardButton { Text = "Dismiss checked record…" }; uncertain.Content.AddChild(dismiss);
        var dismissal = new StandardPanel { Spacing = 8 }; uncertain.Content.AddChild(dismissal);
        var dismissalText = ConfigurationForm.AddText(dismissal, "");
        var keepAction = new StandardButton { Text = "Keep unresolved record" }; dismissal.AddChild(keepAction);
        var confirmDismissal = new StandardButton { Text = "I checked the server — back up and dismiss record" }; dismissal.AddChild(confirmDismissal);
        if (model.SupportsRepair) panel.AddChild(uncertain);
        IReadOnlyList<MailUncertainAction>? shownActions = null; bool updatingActions = false;
        var indexSection = new StandardPanel { Spacing = 8 };
        ConfigurationForm.AddText(indexSection, "Indexed phrase search covers saved headers, previews and complete retained decoded reader text. Complete-body coverage excludes older preview-only, incomplete and over-limit bodies; rebuilding cannot recover discarded text. The disposable index has a separate 256 MiB limit and is rebuilt after profile restore.");
        var indexStatus = ConfigurationForm.AddText(indexSection, model.SearchIndexStatus);
        var inspectIndex = new StandardButton { Text = "Inspect search index" }; indexSection.AddChild(inspectIndex);
        var rebuildIndex = new StandardButton { Text = "Rebuild saved-text search index" }; indexSection.AddChild(rebuildIndex);
        var disableIndex = new StandardButton { Text = "Disable search index (keep mail)" }; indexSection.AddChild(disableIndex);
        if (model.SupportsSearchIndex) panel.AddChild(indexSection);
        // A device whose picker cannot choose folders yet keeps the typed path fields, so it loses no function.
        bool picking = Files.Capabilities.HasFlag(MailFilePickerCapabilities.PickDirectory);
        string unavailable = Files is UnavailableMailFilePicker none ? none.Reason : "Choosing folders is not available on this device yet.";
        var backup = ConfigurationForm.AddSection(panel, "Backup and recovery", picking
            ? "Each backup goes into a new folder in the backup location. Cleanup also creates a verified recovery backup there first. Existing folders are never overwritten."
            : "Choose a new folder for each backup. Cleanup also creates a verified recovery backup here first. Existing folders are never overwritten.");
        StandardLabel PathText(string name) => new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, AccessibleName = name };
        // A label's own accessible name replaces its text for assistive technology, so the name carries the path too.
        static void ShowPath(StandardLabel label, string name, string path) { label.Text = path; label.AccessibleName = name + ": " + path; }
        StandardButton PickButton(StandardPanel section, string text)
        {
            var button = new StandardButton { Text = text, IsEnabled = picking };
            if (!picking) button.ToolTipText = unavailable;
            section.AddChild(button); return button;
        }
        var location = PathText("Backup location");
        StandardEdit? destination = null;
        if (picking) ConfigurationForm.AddLabeledControl(backup, "Backup location", location);
        else { destination = ConfigurationForm.AddField(backup, "New backup folder", model.BackupDestination); destination.MaxLength = 32767; }
        var changeLocation = PickButton(backup, "Change backup location…");
        // Typed mode keeps its generator: one click writes a new unique folder in the profile's backups folder.
        var defaultLocation = new StandardButton { Text = picking ? "Use default location" : "Use a new default backup folder" }; backup.AddChild(defaultLocation);
        var save = new StandardButton { Text = "Create verified backup" }; backup.AddChild(save);
        var clear = new StandardButton { Text = "Clear downloaded mail…" }; backup.AddChild(clear);
        var confirmation = new StandardPanel { Spacing = 8 };
        ConfigurationForm.AddText(confirmation, "Clear all downloaded headers and bodies from this profile? A recovery backup is created first. Drafts, accounts, credentials and server mail are kept. Backups still use disk space; move them elsewhere to reclaim total space.");
        var confirm = new StandardButton { Text = "Back up and clear downloaded mail" };
        var keep = new StandardButton { Text = "Keep downloaded mail" };
        confirmation.AddChild(confirm); confirmation.AddChild(keep); backup.AddChild(confirmation);
        var restore = new FormSection("Restore as a separate profile", "The open profile is never replaced.", collapsible: true, expanded: false);
        panel.AddChild(restore);
        var sourceText = PathText("Backup to restore"); var restoredText = PathText("Restored profile folder");
        StandardEdit? source = null, restored = null;
        if (picking) ConfigurationForm.AddLabeledControl(restore.Content, "Backup to restore", sourceText);
        else { source = ConfigurationForm.AddField(restore.Content, "Backup folder", model.RestoreSource); source.MaxLength = 32767; }
        var chooseSource = PickButton(restore.Content, "Choose backup folder…");
        if (picking) ConfigurationForm.AddLabeledControl(restore.Content, "Restored profile folder", restoredText);
        else { restored = ConfigurationForm.AddField(restore.Content, "New profile folder", model.RestoreDestination); restored.MaxLength = 32767; }
        var changeRestoreLocation = PickButton(restore.Content, "Change restore location…");
        string shownDestination = model.BackupDestination, shownRestored = model.RestoreDestination;
        var restoreButton = new StandardButton { Text = "Restore backup" }; restore.Content.AddChild(restoreButton);
        var open = new StandardButton { Text = "Open restored profile" }; restore.Content.AddChild(open);
        var cancel = new StandardButton { Text = "Cancel storage operation" }; panel.AddChild(cancel);
        var feedback = new InlineFeedback(); panel.AddChild(feedback);
        void FocusVisible(StandardButton button)
        {
            if (panel.Session is not { } session || !button.CanFocus) return;
            // Visibility changes invalidate measurement. Reveal the newly laid-out button,
            // following FormSurface.Reveal's existing layout-before-focus pattern.
            session.RenderFrame(); session.SetFocus(button); button.BringIntoView();
        }
        void Refresh()
        {
            if (panel.IsDisposed) return;
            var oldFocus = panel.Session?.FocusedElement;
            usage.Text = model.Usage;
            refresh.IsEnabled = save.IsEnabled = clear.IsEnabled = defaultLocation.IsEnabled = !model.IsBusy;
            changeLocation.IsEnabled = chooseSource.IsEnabled = changeRestoreLocation.IsEnabled = picking && !model.IsBusy;
            defaultLocation.Visibility = picking && model.IsDefaultBackupLocation ? UiVisibility.Collapsed : UiVisibility.Visible;
            ShowPath(location, "Backup location", model.BackupLocation);
            ShowPath(sourceText, "Backup to restore", model.RestoreSource.Length > 0 ? model.RestoreSource : "No backup chosen yet. Create a backup, or choose a backup folder.");
            ShowPath(restoredText, "Restored profile folder", model.RestoreDestination);
            restoreButton.IsEnabled = !model.IsBusy && (picking ? model.RestoreSource.Length > 0 : true);
            foreach (var field in new[] { destination, source, restored }) if (field is not null) field.IsEnabled = !model.IsBusy;
            // A completed backup or restore generates the next new folder; show it unless the field was edited since.
            if (!model.IsBusy && destination is not null && model.BackupDestination != shownDestination)
            { if (destination.Text == shownDestination) destination.Text = model.BackupDestination; shownDestination = model.BackupDestination; }
            if (!model.IsBusy && restored is not null && model.RestoreDestination != shownRestored)
            { if (restored.Text == shownRestored) restored.Text = model.RestoreDestination; shownRestored = model.RestoreDestination; }
            updatingActions = true;
            if (!ReferenceEquals(shownActions, model.UncertainActions))
            { shownActions = model.UncertainActions; actionChoice.SetItems(shownActions.Select(action => new UiComboBoxItem(action.Id.ToString(), action.Description))); }
            actionChoice.SelectedIndex = model.UncertainActions.ToList().FindIndex(action => action.Id == model.SelectedUncertainActionId);
            actionChoice.IsEnabled = review.IsEnabled = !model.IsBusy && model.PendingUncertainDismissal is null;
            dismiss.IsEnabled = !model.IsBusy && model.SelectedUncertainActionId is not null && model.PendingUncertainDismissal is null;
            dismissal.Visibility = model.PendingUncertainDismissal is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            dismissalText.Text = model.PendingUncertainDismissal is { } pending ? "Dismiss only this saved uncertainty after checking the provider? " + pending.Description + " A verified backup is created first. Cached mail and server mail are not changed." : "";
            keepAction.IsEnabled = confirmDismissal.IsEnabled = !model.IsBusy;
            updatingActions = false;
            repair.IsEnabled = downloads.IsEnabled = quota.IsEnabled = apply.IsEnabled = !model.IsBusy;
            confirmation.Visibility = model.ConfirmClear ? UiVisibility.Visible : UiVisibility.Collapsed;
            confirm.IsEnabled = keep.IsEnabled = !model.IsBusy;
            cancel.Visibility = model.IsBusy ? UiVisibility.Visible : UiVisibility.Collapsed;
            indexStatus.Text = model.SearchIndexStatus;
            inspectIndex.IsEnabled = rebuildIndex.IsEnabled = disableIndex.IsEnabled = !model.IsBusy;
            open.IsEnabled = model.CanOpenRestored;
            if (model.IsBusy && (oldFocus == confirmDismissal || oldFocus == review)) FocusVisible(cancel);
            else if (!model.IsBusy && oldFocus == cancel) FocusVisible(review);
            // A completed backup becomes the restore source, without rewriting a field while typing.
            if (!model.IsBusy && source is not null && source.Text.Length == 0 && model.RestoreSource.Length > 0) source.Text = model.RestoreSource;
            feedback.Set(model.Status, model.IsError ? FeedbackKind.Error : model.IsWarning ? FeedbackKind.Warning : model.IsBusy ? FeedbackKind.Progress : FeedbackKind.Information);
            if (panel.Session?.FocusedElement is { } focused && focused.IsDescendantOf(panel))
            {
                if (model.IsBusy && !focused.CanFocus) FocusVisible(cancel);
                else if (!model.ConfirmClear && (focused == confirm || focused == keep)) FocusVisible(clear);
                else if (!model.IsBusy && focused == cancel) FocusVisible(refresh);
            }
        }
        model.Changed += (_, _) => Refresh();
        inspectIndex.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.InspectSearchIndexAsync());
        rebuildIndex.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RebuildSearchIndexAsync());
        disableIndex.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.DisableSearchIndexAsync());
        review.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.ReviewUncertainActionsAsync());
        actionChoice.SelectionChanged += (_, _) => { if (!updatingActions) model.SelectUncertainAction(actionChoice.SelectedIndex >= 0 && actionChoice.SelectedIndex < model.UncertainActions.Count ? model.UncertainActions[actionChoice.SelectedIndex].Id : null); };
        dismiss.Clicked += (_, _) => { model.RequestUncertainDismissal(); if (model.PendingUncertainDismissal is not null) FocusVisible(keepAction); };
        keepAction.Clicked += (_, _) => { model.CancelUncertainDismissal(); FocusVisible(dismiss); };
        confirmDismissal.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { await model.DismissUncertainActionAsync(); if (!panel.IsDisposed) FocusVisible(review); });
        refresh.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.RefreshAsync());
        apply.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { model.SelectedDownloadPolicy = (OfflineDownloadPolicy)downloads.SelectedIndex; model.QuotaMiB = quota.Text; await model.SavePolicyAsync(); });
        void TakeTypedBackupDestination() { if (destination is not null) shownDestination = model.BackupDestination = destination.Text; }
        // A user-initiated folder dialog is the documented exemption to the no-modal-while-reading rule.
        async Task PickFolderAsync(StandardButton trigger, MailFilePurpose purpose, string title, string accept, string initial, Func<string, bool> use)
        {
            if (model.IsBusy || panel.IsDisposed) return;
            var pick = await Files.PickDirectoryAsync(new MailFileRequest(purpose, title) { AcceptText = accept, InitialDirectory = initial });
            if (panel.IsDisposed) return;
            switch (pick.Status)
            {
                case MailFilePickStatus.Picked when pick.Item?.LocalPath is { Length: > 0 } path: use(path); break;
                case MailFilePickStatus.Picked: model.Warn("That folder cannot be used here because it has no path on this device. Nothing was changed."); break;
                case MailFilePickStatus.Rejected or MailFilePickStatus.Unavailable:
                    model.Warn((pick.Message ?? "That folder cannot be used.") + " Nothing was changed."); break;
            }
            if (panel.Session?.FocusedElement is not { } focused || !focused.IsDescendantOf(panel) || !focused.CanFocus) FocusVisible(trigger);
        }
        static string Existing(string path)
        {
            for (string? folder = path; !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(folder)) return folder;
            return path;
        }
        changeLocation.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await PickFolderAsync(changeLocation,
            MailFilePurpose.BackupLocation, "Choose backup location", "Use this folder", Existing(model.BackupLocation), model.SetBackupLocation));
        defaultLocation.Clicked += (_, _) =>
        {
            model.UseDefaultBackupLocation();
            // The user asked for a new folder, so it replaces whatever the field holds, edited or not.
            if (destination is not null) { if (!model.IsBusy) destination.Text = shownDestination = model.BackupDestination; }
            else FocusVisible(changeLocation);
        };
        chooseSource.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await PickFolderAsync(chooseSource,
            MailFilePurpose.RestoreBackup, "Choose backup to restore", "Use this backup",
            Existing(model.RestoreSource.Length > 0 ? model.RestoreSource : model.BackupLocation), model.SetRestoreSource));
        changeRestoreLocation.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await PickFolderAsync(changeRestoreLocation,
            MailFilePurpose.RestoreLocation, "Choose where to restore", "Use this folder", Existing(model.RestoreLocation), model.SetRestoreLocation));
        save.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { TakeTypedBackupDestination(); await model.BackupAsync(); });
        clear.Clicked += (_, _) => model.RequestClear();
        keep.Clicked += (_, _) => model.CancelClear();
        confirm.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => { TakeTypedBackupDestination(); await model.ClearAsync(); });
        restoreButton.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            if (source is not null) model.RestoreSource = source.Text;
            if (restored is not null) shownRestored = model.RestoreDestination = restored.Text;
            await model.RestoreAsync();
        });
        open.Clicked += (_, _) => model.OpenRestored(); cancel.Clicked += (_, _) => model.Cancel();
        Refresh(); return panel;
    }
}
