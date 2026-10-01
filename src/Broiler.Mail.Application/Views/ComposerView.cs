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
// Security risk:    High
// Criteria:         2/2
// Resource impact:  7/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.UI.Forms.Standard;
using Broiler.UI.Forms;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=3F7FB2
// Broiler-Falsified-If: text typed in the Bcc field reaches model.Edit as its to or cc argument, so Bcc recipients appear in the sent headers
// Broiler-Human:        PENDING
public sealed class ComposerView(ComposerViewModel model, InboxViewModel inbox)
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=31C5F3
    // Broiler-Falsified-If: text typed in the Bcc field reaches model.Edit as its to or cc argument, so Bcc recipients appear in the sent headers
    // Broiler-Human:        PENDING
    public UiElement CreateContent()
    {
        var panel = new StandardPanel { Spacing = 8 };
        ConfigurationForm.AddText(panel, "Write a plain-text message. Configure outgoing mail in Account before sending.");
        var source = ConfigurationForm.AddText(panel, "");
        var actions = new StandardToolbar
        {
            Overflow = UiToolbarOverflow.Wrap,
            Padding = 4,
            Spacing = 8,
            PreferredSize = new BSize(0, 36),
        };
        var create = new StandardButton { Text = "New message" };
        var reply = new StandardButton { Text = "Reply" };
        var replyAll = new StandardButton { Text = "Reply all" };
        var forward = new StandardButton { Text = "Forward" };
        foreach (var button in new[] { create, reply, replyAll, forward }) actions.AddChild(button);
        panel.AddChild(actions);
        var sender = ConfigurationForm.AddText(panel, "");
        ConfigurationForm.AddText(panel, "Recipients: separate email addresses with commas. Only plain text is retained from the body editor.");
        var to = ConfigurationForm.AddField(panel, "To", "");
        var copies = new FormSection("Cc and Bcc", "Additional recipients remain part of the draft when this group is collapsed.", collapsible: true, expanded: false);
        panel.AddChild(copies);
        var cc = ConfigurationForm.AddField(copies.Content, "Cc", "");
        var bcc = ConfigurationForm.AddField(copies.Content, "Bcc", "");
        // Validate rather than truncate: imported recipients and prefixed subjects must remain intact.
        foreach (var field in new[] { to, cc, bcc }) field.MaxLength = int.MaxValue;
        var subject = ConfigurationForm.AddField(panel, "Subject", "");
        subject.MaxLength = int.MaxValue;
        var body = new StandardRichEdit { AcceptsReturn = true, PreferredSize = new BSize(520, 300) };
        body.ApplyTheme(StandardControlPaint.Theme);
        ConfigurationForm.AddLabeledControl(panel, "Body (plain text)", body);
        var check = new StandardButton { Text = "Check draft" };
        var discard = new StandardButton { Text = "Discard draft" };
        var save = new StandardButton { Text = "Save draft" };
        var send = new StandardButton { Text = "Send", IsDefault = true };
        var submission = new InlineFeedback();
        var sentCopy = new InlineFeedback();
        var storage = new InlineFeedback();
        var status = new InlineFeedback();
        var feedback = new StandardPanel { Spacing = 4 };
        feedback.AddChild(submission); feedback.AddChild(sentCopy);
        feedback.AddChild(storage); feedback.AddChild(status);
        var surface = new FormSurface(panel, FormSurface.ActionBar(send, check, save, discard), feedback);
        Guid? shown = null;
        bool updating = false;
        void Refresh()
        {
            if (panel.IsDisposed) return;
            updating = true;
            if (shown != model.DraftId)
            {
                shown = model.DraftId;
                to.Text = model.To; cc.Text = model.Cc; bcc.Text = model.Bcc;
                subject.Text = model.Subject; body.SetPlainText(model.PlainText);
                copies.IsExpanded = model.Cc.Length > 0 || model.Bcc.Length > 0;
            }
            create.IsEnabled = model.CanStart;
            reply.IsEnabled = replyAll.IsEnabled = forward.IsEnabled = model.CanStart && !inbox.IsBusy && inbox.Body?.Composition is not null;
            foreach (var field in new[] { to, cc, bcc, subject }) { field.IsEnabled = model.HasDraft; field.IsReadOnly = !model.CanEdit; }
            body.IsEnabled = model.HasDraft;
            body.IsReadOnly = !model.CanEdit;
            check.IsEnabled = model.HasDraft && !model.IsBusy;
            discard.IsEnabled = model.CanDiscard;
            save.IsEnabled = model.HasDraft && !model.IsBusy;
            send.IsEnabled = model.CanSend;
            copies.Summary = string.Join(" · ", new[] { model.Cc.Length > 0 ? "Cc recipients included" : "", model.Bcc.Length > 0 ? "Bcc recipients included" : "" }.Where(text => text.Length > 0));
            submission.Set(model.HasDraft && model.SubmissionState != DraftSubmissionState.Editing ? model.SubmissionText : "",
                model.SubmissionState switch { DraftSubmissionState.Failed => FeedbackKind.Error, DraftSubmissionState.Unknown => FeedbackKind.Warning,
                    DraftSubmissionState.Sending => FeedbackKind.Progress, DraftSubmissionState.Accepted => FeedbackKind.Success, _ => FeedbackKind.Information });
            sentCopy.Set(model.SentCopy != SentCopyState.NotRequested || model.SubmissionState == DraftSubmissionState.Accepted ? model.SentCopyText : "",
                model.SentCopy switch { SentCopyState.Failed => FeedbackKind.Error, SentCopyState.Unknown => FeedbackKind.Warning,
                    SentCopyState.Pending => FeedbackKind.Progress, SentCopyState.Saved => FeedbackKind.Success, _ => FeedbackKind.Information });
            storage.Set(model.HasDraft || model.HasLoadError ? model.StorageStatus : "", model.StorageKind);
            sender.Text = ("From: " + model.FromAddress).Replace("&", "&&", StringComparison.Ordinal);
            source.Text = (inbox.Body is { } loaded
                ? loaded.CompositionUnavailableReason ?? $"Selected message: {loaded.Composition?.Subject ?? "Read the message again to load reply headers."}"
                : "Select and read a message in Inbox before replying or forwarding.").Replace("&", "&&", StringComparison.Ordinal);
            status.Set(model.IsBusy ? model.SubmissionState == DraftSubmissionState.Sending ? "Submitting message…"
                : model.SentCopy == SentCopyState.Pending ? "Saving Sent copy…" : "Updating draft…" : model.Status,
                model.IsBusy ? FeedbackKind.Progress : model.StatusKind);
            updating = false;
        }
        void Capture()
        {
            if (!updating) model.Edit(to.Text, cc.Text, bcc.Text, subject.Text, body.GetPlainText());
        }
        foreach (var field in new[] { to, cc, bcc, subject }) field.TextChanged += (_, _) => Capture();
        body.DocumentChanged += (_, _) => Capture();
        create.Clicked += (_, _) => model.StartNew();
        reply.Clicked += (_, _) => { if (inbox.Body is { } message) model.StartFromMessage(message, CompositionKind.Reply); };
        replyAll.Clicked += (_, _) => { if (inbox.Body is { } message) model.StartFromMessage(message, CompositionKind.ReplyAll); };
        forward.Clicked += (_, _) => { if (inbox.Body is { } message) model.StartFromMessage(message, CompositionKind.Forward); };
        check.Clicked += (_, _) => model.CheckDraft();
        discard.Clicked += async (_, _) => await model.DiscardAsync();
        save.Clicked += async (_, _) => await model.SaveAsync();
        send.Clicked += async (_, _) => await model.SendAsync();
        model.Changed += (_, _) => Refresh();
        inbox.Changed += (_, _) => Refresh();
        Refresh();
        return surface;
    }
}
