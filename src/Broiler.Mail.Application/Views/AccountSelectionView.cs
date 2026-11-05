using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

public sealed class AccountSelectionView(AccountSelectionViewModel model, Action openSetup, AccountReceiveQueueViewModel? receives = null)
{
    public UiElement CreateContent()
    {
        var panel = new StandardPanel { Spacing = 4 };
        var row = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var accounts = new StandardComboBox { AccessibleName = "Mail account", PreferredSize = new BSize(280, 32) };
        var add = new StandardButton { Text = "Add account" };
        var cancel = new StandardButton { Text = "Cancel new account" };
        var manage = new StandardButton { Text = "Manage accounts" };
        row.AddChild(accounts); row.AddChild(add); row.AddChild(cancel); row.AddChild(manage); panel.AddChild(row);
        if (receives is not null) panel.AddChild(new AccountReceiveQueueView(receives, () => model.SelectedId, model).CreateContent());
        var management = new StandardPanel { Visibility = UiVisibility.Collapsed, Spacing = 4 };
        management.AddChild(new StandardLabel
        {
            Text = "Disabling stops receiving and sending for this account. Cached mail, credentials and the current draft are kept. Enabling does not automatically receive mail.",
            UseMnemonic = false, Wrapping = UiTextWrapping.Wrap,
        });
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var enabled = new StandardButton(); var earlier = new StandardButton { Text = "Move account earlier" }; var later = new StandardButton { Text = "Move account later" };
        var remove = new StandardButton { Text = "Remove account…" };
        actions.AddChild(enabled); actions.AddChild(earlier); actions.AddChild(later); actions.AddChild(remove); management.AddChild(actions); panel.AddChild(management);
        var removalExplanation = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; management.AddChild(removalExplanation);
        var confirmation = new StandardPanel { Visibility = UiVisibility.Collapsed, Spacing = 4 };
        var confirmationText = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; confirmation.AddChild(confirmationText);
        var confirmActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var keepPasswords = new StandardButton { Text = "Remove, keep passwords" };
        var forgetPasswords = new StandardButton { Text = "Remove, forget passwords" };
        var cancelRemoval = new StandardButton { Text = "Cancel removal" };
        confirmActions.AddChild(keepPasswords); confirmActions.AddChild(forgetPasswords); confirmActions.AddChild(cancelRemoval);
        confirmation.AddChild(confirmActions); management.AddChild(confirmation);
        var localMail = new StandardPanel { Spacing = 4 };
        var localNotice = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; localMail.AddChild(localNotice);
        var localActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var inspectMail = new StandardButton { Text = "Inspect downloaded mail" };
        var clearMail = new StandardButton { Text = "Clear this account's downloaded mail…" };
        localActions.AddChild(inspectMail); localActions.AddChild(clearMail); localMail.AddChild(localActions);
        var localConfirmation = new StandardPanel { Spacing = 4, Visibility = UiVisibility.Collapsed };
        var localConfirmationText = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; localConfirmation.AddChild(localConfirmationText);
        var localConfirmActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var keepMail = new StandardButton { Text = "Keep downloaded mail" };
        var confirmClearMail = new StandardButton { Text = "Back up and clear local mail" };
        localConfirmActions.AddChild(keepMail); localConfirmActions.AddChild(confirmClearMail); localConfirmation.AddChild(localConfirmActions); localMail.AddChild(localConfirmation);
        var localBackup = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; localMail.AddChild(localBackup);
        if (model.SupportsLocalMailCleanup) management.AddChild(localMail);
        var orphanSection = new FormSection("Mail for removed accounts", "Inspect and explicitly clear local mail whose account ID is no longer configured.", collapsible: true, expanded: false);
        orphanSection.Content.AccessibleName = "Removed-account mail controls";
        var orphanNotice = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; orphanSection.Content.AddChild(orphanNotice);
        var orphanReload = new StandardButton { Text = "List removed-account mail" };
        var orphanPicker = new StandardComboBox { AccessibleName = "Removed account ID and downloaded-mail counts", PreferredSize = new BSize(420,32) };
        var orphanClear = new StandardButton { Text = "Clear selected removed-account mail…" };
        var orphanActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        orphanActions.AddChild(orphanReload); orphanActions.AddChild(orphanPicker); orphanActions.AddChild(orphanClear); orphanSection.Content.AddChild(orphanActions);
        var orphanConfirmation = new StandardPanel { Spacing = 4, Visibility = UiVisibility.Collapsed };
        var orphanConfirmationText = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; orphanConfirmation.AddChild(orphanConfirmationText);
        var orphanConfirmActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        var orphanKeep = new StandardButton { Text = "Keep removed-account mail" };
        var orphanConfirm = new StandardButton { Text = "Back up and clear removed-account mail" };
        orphanConfirmActions.AddChild(orphanKeep); orphanConfirmActions.AddChild(orphanConfirm); orphanConfirmation.AddChild(orphanConfirmActions); orphanSection.Content.AddChild(orphanConfirmation);
        var orphanBackup = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; orphanSection.Content.AddChild(orphanBackup);
        if (model.SupportsOrphanMail) panel.AddChild(orphanSection);
        IReadOnlyList<Core.Services.AccountDownloadedMailInfo>? shownOrphans = null;
        var status = new StandardLabel { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap }; panel.AddChild(status);
        bool updating = false;
        IReadOnlyList<Core.Accounts.AccountProfile>? shown = null;
        void Refresh()
        {
            if (panel.IsDisposed) return;
            updating = true;
            if (!ReferenceEquals(shown, model.Profiles))
            {
                shown = model.Profiles;
                accounts.SetItems(shown.Select(profile => new UiComboBoxItem(profile.Id.ToString(),
                    $"{profile.DisplayName} <{profile.EmailAddress}>{(profile.IsEnabled ? "" : " — disabled")}")));
            }
            accounts.SelectedIndex = model.IsAdding ? -1 : model.Profiles.ToList().FindIndex(p => p.Id == model.SelectedId);
            accounts.IsEnabled = model.CanSwitch && model.Profiles.Count > 1;
            add.IsEnabled = model.CanAdd;
            cancel.Visibility = model.IsAdding ? UiVisibility.Visible : UiVisibility.Collapsed;
            cancel.IsEnabled = model.CanChange;
            manage.IsEnabled = model.Profiles.Count > 0 && !model.IsAdding && !model.IsBusy;
            if (model.Profiles.Count == 0) { management.Visibility = UiVisibility.Collapsed; manage.Text = "Manage accounts"; }
            enabled.Text = model.SelectedProfile?.IsEnabled == true ? "Disable account" : "Enable account";
            enabled.IsEnabled = model.CanManage;
            earlier.IsEnabled = model.CanMoveEarlier; later.IsEnabled = model.CanMoveLater;
            remove.IsEnabled = model.CanRemove;
            localNotice.Text = model.LocalMailNotice;
            inspectMail.IsEnabled = model.CanInspectLocalMail; clearMail.IsEnabled = model.CanRequestLocalMailCleanup;
            localConfirmation.Visibility = model.PendingLocalMailCleanup is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            localConfirmationText.Text = model.PendingLocalMailCleanup is { } localTarget
                ? $"Clear downloaded mail for {localTarget.DisplayName} <{localTarget.EmailAddress}> from this profile? A verified whole-profile recovery backup is created first. Only this account's saved headers, body previews and inline resources are cleared. Other accounts, all drafts, settings and passwords stay. No server mail is changed. Recovery backups continue to contain this mail; this is not secure erasure." : "";
            confirmClearMail.IsEnabled = model.CanConfirmLocalMailCleanup; keepMail.IsEnabled = true;
            keepMail.Text = model.IsBusy && model.PendingLocalMailCleanup is not null ? "Cancel local mail cleanup" : "Keep downloaded mail";
            localBackup.Text = model.LastLocalMailCleanupBackup is { } localPath ? "Recovery backup (last local mail cleanup): " + localPath : "";
            orphanSection.Toggle!.IsEnabled = !model.IsChanging;
            orphanNotice.Text = model.OrphanMailNotice;
            if (!ReferenceEquals(shownOrphans, model.OrphanMail))
            {
                shownOrphans = model.OrphanMail; orphanPicker.SetItems(shownOrphans.Select(item => new UiComboBoxItem(item.AccountId.ToString(),
                    $"{item.AccountId} · {item.HeaderCount} headers · {item.DownloadedBodyCount} body previews · {item.MailboxCount} mailbox heads")));
            }
            orphanPicker.SelectedIndex = model.OrphanMail.ToList().FindIndex(item => item == model.SelectedOrphanMail);
            orphanReload.IsEnabled = model.CanFindOrphanMail; orphanPicker.IsEnabled = model.CanFindOrphanMail && model.OrphanMail.Count > 0; orphanClear.IsEnabled = model.CanRequestOrphanCleanup;
            orphanConfirmation.Visibility = model.PendingOrphanCleanup is null ? UiVisibility.Collapsed : UiVisibility.Visible;
            orphanConfirmationText.Text = model.PendingOrphanCleanup is {} orphanTarget
                ? $"Clear local mail for unconfigured account ID {orphanTarget.AccountId}? Last inspected: {orphanTarget.HeaderCount} headers and {orphanTarget.DownloadedBodyCount} body previews. Counts can change; the account ID is rechecked before a verified whole-profile backup and cleanup. Configured accounts, all drafts, settings and passwords stay. No server mail is changed." : "";
            orphanConfirm.IsEnabled = model.CanConfirmOrphanCleanup; orphanKeep.IsEnabled = true;
            orphanKeep.Text = model.IsBusy && model.PendingOrphanCleanup is not null ? "Cancel removed-account cleanup" : "Keep removed-account mail";
            orphanBackup.Text = model.LastOrphanCleanupBackup is {} orphanPath ? "Recovery backup (removed-account mail): " + orphanPath : "";
            if (panel.Session is {} orphanSession && orphanSession.FocusedElement is {} orphanFocus && orphanFocus.IsDescendantOf(orphanConfirmation))
            {
                if (model.IsBusy && orphanFocus != orphanKeep) FocusNavigation.FocusAndReveal(orphanSession, orphanKeep);
                else if (model.PendingOrphanCleanup is null && !model.IsChanging) FocusNavigation.FocusAndReveal(orphanSession, orphanReload);
            }
            removalExplanation.Text = model.RemovalExplanation;
            confirmation.Visibility = model.PendingRemoval is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
            confirmationText.Text = model.PendingRemoval is { } pending
                ? $"Remove {pending.DisplayName} <{pending.EmailAddress}> from the account list? A verified recovery backup is created first. Downloaded mail stays on disk; no server mail is deleted. Passwords are not included in backups. Forgetting them cannot be rolled back if removal later fails or is canceled." : "";
            keepPasswords.IsEnabled = forgetPasswords.IsEnabled = model.CanConfirmRemoval;
            cancelRemoval.IsEnabled = true;
            status.Text = model.Guidance + (model.LastRemovalBackup is { } backup ? "\nRecovery backup (last removal): " + backup : "");
            if (panel.Session?.FocusedElement == cancel && !model.IsAdding) panel.Session.SetFocus(add);
            if (!model.IsChanging && panel.Session is { } session &&
                ((session.FocusedElement == earlier && !earlier.IsEnabled) || (session.FocusedElement == later && !later.IsEnabled)))
                FocusNavigation.FocusAndReveal(session, enabled.IsEnabled ? enabled : manage);
            if (panel.Session is { } removalSession && removalSession.FocusedElement is { } removalFocus && removalFocus.IsDescendantOf(confirmation))
            {
                if (model.IsBusy && removalFocus != cancelRemoval) FocusNavigation.FocusAndReveal(removalSession, cancelRemoval);
                else if (model.PendingRemoval is null && !model.IsChanging)
                    FocusNavigation.FocusAndReveal(removalSession, remove.IsEnabled ? remove : accounts.IsEnabled ? accounts : add);
            }
            if (panel.Session is { } localSession && localSession.FocusedElement is { } localFocus && localFocus.IsDescendantOf(localConfirmation))
            {
                if (model.IsBusy && localFocus != keepMail) FocusNavigation.FocusAndReveal(localSession, keepMail);
                else if (model.PendingLocalMailCleanup is null && !model.IsChanging) FocusNavigation.FocusAndReveal(localSession, clearMail.IsEnabled ? clearMail : manage);
            }
            if (!model.IsChanging && model.PendingRemoval is null && model.PendingLocalMailCleanup is null && model.PendingOrphanCleanup is null && panel.Session is { } focusSession)
                FocusNavigation.KeepFocusUsable(focusSession, panel, accounts.IsEnabled ? accounts : add);
            updating = false;
        }
        accounts.SelectionChanged += async (_, _) =>
        {
            if (updating) return;
            int index = accounts.SelectedIndex;
            if (index >= 0 && index < model.Profiles.Count) await model.SwitchAsync(model.Profiles[index].Id);
        };
        add.Clicked += (_, _) => { if (model.BeginAdd()) openSetup(); };
        cancel.Clicked += (_, _) => model.CancelAdd();
        enabled.Clicked += async (_, _) => await model.SetEnabledAsync(model.SelectedProfile?.IsEnabled != true);
        earlier.Clicked += async (_, _) => await model.MoveSelectedAsync(-1);
        later.Clicked += async (_, _) => await model.MoveSelectedAsync(1);
        remove.Clicked += (_, _) =>
        {
            if (model.RequestRemoval() && panel.Session is { } session) FocusNavigation.FocusAndReveal(session, cancelRemoval);
        };
        orphanSection.Toggle!.Clicked += (_, _) => { if (!orphanSection.IsExpanded && !model.IsBusy) model.CancelOrphanCleanup(); };
        orphanReload.Clicked += async (_, _) => await model.FindOrphanMailAsync();
        orphanPicker.SelectionChanged += (_, _) => { if (!updating) model.SelectOrphanMail(orphanPicker.SelectedIndex >= 0 && orphanPicker.SelectedIndex < model.OrphanMail.Count ? model.OrphanMail[orphanPicker.SelectedIndex].AccountId : null); };
        orphanClear.Clicked += (_, _) => { if (model.RequestOrphanCleanup() && panel.Session is {} session) FocusNavigation.FocusAndReveal(session, orphanKeep); };
        orphanKeep.Clicked += (_, _) => model.CancelOrphanCleanup();
        orphanConfirm.Clicked += async (_, _) => await model.ConfirmOrphanCleanupAsync();
        inspectMail.Clicked += async (_, _) => await model.InspectLocalMailAsync();
        clearMail.Clicked += (_, _) => { if (model.RequestLocalMailCleanup() && panel.Session is { } session) FocusNavigation.FocusAndReveal(session, keepMail); };
        keepMail.Clicked += (_, _) => model.CancelLocalMailCleanup();
        confirmClearMail.Clicked += async (_, _) => await model.ConfirmLocalMailCleanupAsync();
        keepPasswords.Clicked += async (_, _) => await model.ConfirmRemovalAsync(false);
        forgetPasswords.Clicked += async (_, _) => await model.ConfirmRemovalAsync(true);
        cancelRemoval.Clicked += (_, _) => model.CancelRemoval();
        manage.Clicked += (_, _) =>
        {
            bool show = management.Visibility != UiVisibility.Visible;
            if (!show && !model.IsBusy) { model.CancelRemoval(); model.CancelLocalMailCleanup(); }
            management.Visibility = show ? UiVisibility.Visible : UiVisibility.Collapsed;
            manage.Text = show ? "Hide account management" : "Manage accounts";
            if (!show && panel.Session is { } session && session.FocusedElement is { } focused && focused.IsDescendantOf(management))
                FocusNavigation.FocusAndReveal(session, manage);
        };
        model.Changed += (_, _) => Refresh(); Refresh();
        return new BoundedScrollArea(panel, 0.4, "Accounts");
    }
}
