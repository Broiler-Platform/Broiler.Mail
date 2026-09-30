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

using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.Standard;

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
        ConfigurationForm.AddText(panel, "Plain-text composer — drafts are saved automatically in normal mode. Configure SMTP, its password, and Sent-copy handling in Account before sending.");
        var source = ConfigurationForm.AddText(panel, "");
        var actions = new StandardPanel { StackOrientation = UiStackOrientation.Horizontal, Spacing = 8 };
        var create = new StandardButton { Text = "New message" };
        var reply = new StandardButton { Text = "Reply" };
        var replyAll = new StandardButton { Text = "Reply all" };
        var forward = new StandardButton { Text = "Forward" };
        foreach (var button in new[] { create, reply, replyAll, forward }) actions.AddChild(button);
        panel.AddChild(actions);
        var sender = ConfigurationForm.AddText(panel, "");
        ConfigurationForm.AddText(panel, "Recipients: separate email addresses with commas. Only plain text is retained from the body editor.");
        var to = ConfigurationForm.AddField(panel, "To", "");
        var cc = ConfigurationForm.AddField(panel, "Cc", "");
        var bcc = ConfigurationForm.AddField(panel, "Bcc", "");
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
        var send = new StandardButton { Text = "Send" };
        panel.AddChild(check);
        panel.AddChild(save);
        panel.AddChild(send);
        panel.AddChild(discard);
        var submission = ConfigurationForm.AddText(panel, "");
        var sentCopy = ConfigurationForm.AddText(panel, "");
        var storage = ConfigurationForm.AddText(panel, "");
        var status = ConfigurationForm.AddText(panel, "");
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
            submission.Text = model.SubmissionText;
            sentCopy.Text = model.SentCopyText.Replace("&", "&&", StringComparison.Ordinal);
            storage.Text = model.StorageStatus.Replace("&", "&&", StringComparison.Ordinal);
            sender.Text = ("From: " + model.FromAddress).Replace("&", "&&", StringComparison.Ordinal);
            source.Text = (inbox.Body is { } loaded
                ? loaded.CompositionUnavailableReason ?? $"Selected message: {loaded.Composition?.Subject ?? "Read the message again to load reply headers."}"
                : "Select and read a message in Inbox before replying or forwarding.").Replace("&", "&&", StringComparison.Ordinal);
            status.Text = model.Status.Replace("&", "&&", StringComparison.Ordinal);
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
        return ConfigurationForm.Wrap(panel);
    }
}
