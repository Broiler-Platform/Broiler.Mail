using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Forms;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

public sealed class AccountReceiveQueueView(AccountReceiveQueueViewModel model, Func<AccountId?> selectedAccount, AccountSelectionViewModel? availability = null)
{
    public UiElement CreateContent()
    {
        var section = new FormSection("Account receives", "Explicit background header jobs for saved accounts. Two receive slots; one pending/active job per account. Opening this section starts nothing and jobs are not replayed after restart.", collapsible: true, expanded: false) { ShowText = "Show account receives", HideText = "Hide account receives" };
        var picker = new StandardComboBox { AccessibleName = "Account receive job", PreferredSize = new BSize(400, 32) };
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 6 };
        var queueAll = new StandardButton { Text = "Queue enabled accounts" }; var queueOne = new StandardButton { Text = "Queue selected account receive" };
        var cancelOne = new StandardButton { Text = "Cancel selected account receive" }; var cancelAll = new StandardButton { Text = "Cancel all account receives" };
        foreach (var button in new[] { queueAll, queueOne, cancelOne, cancelAll }) actions.AddChild(button);
        var feedback = new InlineFeedback();
        var details = new StandardRichEdit { AccessibleName = "Account receive job details", IsReadOnly = true, AcceptsReturn = true, PreferredSize = new BSize(520, 112) };
        section.Content.AddChild(picker); section.Content.AddChild(actions); section.Content.AddChild(feedback); section.Content.AddChild(details);
        AccountId? selected = null; IReadOnlyList<AccountReceiveJobStatus> shown = []; bool updating = false; string? shownDetails = null;
        void Refresh()
        {
            if (section.IsDisposed) return; updating = true;
            var jobs = model.Jobs;
            if (!shown.SequenceEqual(jobs))
            {
                shown = jobs; if (!shown.Any(job => job.AccountId == selected)) selected = shown.Any(job => job.AccountId == selectedAccount()) ? selectedAccount() : shown.FirstOrDefault()?.AccountId;
                picker.SetItems(shown.Select(job => new UiComboBoxItem(job.AccountId.ToString(), $"{job.DisplayName} <{job.Address}> — {job.State}")));
                picker.SelectedIndex = selected is {} id ? shown.ToList().FindIndex(job => job.AccountId == id) : -1;
            }
            var current = shown.SingleOrDefault(job => job.AccountId == selected);
            string text = current is null ? "No configured account receive selected." : $"{current.DisplayName} <{current.Address}>\nAccount: {current.AccountId}\nState: {current.State}; confirmed saved headers this job: {current.SavedHeaders}; finished: {current.FinishedAt?.ToString("O") ?? "not finished"}\n{current.Detail}\nThese controls do not switch the reader or draft sender.";
            if (shownDetails != text) { shownDetails = text; details.SetPlainText(text); }
            queueAll.IsEnabled = model.CanQueueAll; queueOne.IsEnabled = selected is {} queueId && model.CanQueueAccount(queueId);
            cancelOne.IsEnabled = selected is {} cancelId && model.CanCancel(cancelId); cancelAll.IsEnabled = model.CanCancelAll;
            feedback.Set(model.Notice, current?.State == AccountReceiveState.Failed ? FeedbackKind.Error : FeedbackKind.Information);
            updating = false;
            if (section.Session is {} session && (session.FocusedElement == cancelOne && !cancelOne.IsEnabled || session.FocusedElement == cancelAll && !cancelAll.IsEnabled))
                FocusNavigation.FocusAndReveal(session, queueOne.IsEnabled ? queueOne : queueAll.IsEnabled ? queueAll : picker);
        }
        void FocusCancel() { if (section.Session is {} session) FocusNavigation.FocusAndReveal(session, cancelOne.IsEnabled ? cancelOne : cancelAll.IsEnabled ? cancelAll : picker); }
        picker.SelectionChanged += (_, _) => { if (updating) return; selected = picker.SelectedIndex >= 0 && picker.SelectedIndex < shown.Count ? shown[picker.SelectedIndex].AccountId : null; Refresh(); };
        queueAll.Clicked += (_, _) => { model.QueueAllEnabled(); Refresh(); FocusCancel(); };
        queueOne.Clicked += (_, _) => { if (selected is {} id) model.QueueAccount(id); Refresh(); FocusCancel(); };
        cancelOne.Clicked += (_, _) => { if (selected is {} id) model.Cancel(id); Refresh(); };
        cancelAll.Clicked += (_, _) => { model.CancelAll(); Refresh(); };
        model.Changed += (_, _) => Refresh(); if (availability is not null) availability.Changed += (_, _) => Refresh(); Refresh(); return section;
    }
}
