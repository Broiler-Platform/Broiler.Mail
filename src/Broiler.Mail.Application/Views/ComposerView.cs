// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   4
// Annotated:        2/4
// Exempt:           7
// Human-reviewed:   0/4
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  7/10 max
// Unverified:       4
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.UI.Forms.Standard;
using Broiler.UI.Forms;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

// Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=4B6B5C
// Hairs-Falsified-If: text typed in the Bcc field reaches model.Edit as its to or cc argument, so Bcc recipients appear in the sent headers
// Hairs-Human:        PENDING
public sealed class ComposerView(ComposerViewModel model, InboxViewModel inbox, CompositionCommands? commands = null, IMailFilePicker? files = null)
{
    private readonly CompositionCommands _commands = commands ?? new CompositionCommands(model, inbox);
    /// <summary>Chooses files for this view; an unavailable picker when none was given, so views keep their typed paths.</summary>
    internal IMailFilePicker Files { get; } = files ?? UnavailableMailFilePicker.NotOnThisDeviceYet;

    /// <summary>The To field, available after <see cref="CreateContent"/>; focused for new messages and forwards.</summary>
    public UiElement? Recipients { get; private set; }
    /// <summary>The body editor, available after <see cref="CreateContent"/>; focused for replies.</summary>
    public UiElement? Body { get; private set; }
    /// <summary>The attachment bar, available after <see cref="CreateContent"/>.</summary>
    internal ComposerAttachmentsBar? Attachments { get; private set; }
    /// <summary>The inline "mentions an attachment" confirmation above the draft's feedback, available after <see cref="CreateContent"/>.</summary>
    internal UiElement? SendConfirmation { get; private set; }

    /// <summary>The label of the confirmation shown when Send would leave out a file the draft mentions.</summary>
    internal const string SendConfirmationText = "This message mentions an attachment, but no file is attached.";

    private Func<bool>? _escape;

    /// <summary>
    /// Escape inside Compose, before the dialog closes: a shown attachment confirmation goes away (focus returns to Send),
    /// then a running attachment read is canceled. Returns false when neither applies, so Escape closes Compose.
    /// </summary>
    internal bool HandleEscape() => _escape?.Invoke() ?? false;

    // Hairs-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=A74C88
    // Hairs-Falsified-If: text typed in the Bcc field reaches model.Edit as its to or cc argument, so Bcc recipients appear in the sent headers
    // Hairs-Human:        PENDING
    public UiElement CreateContent()
    {
        // Writing comes first: a compact header, then a body that takes the rest of the window.
        var panel = new FillLastStack { Spacing = 8, MinimumFillHeight = 160 };
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
        panel.Add(actions);
        if (model.SupportsDraftLibrary) panel.Add(SavedDraftsView.Create(model));
        // Shown only without a draft: why Reply is unavailable, or how to enable it.
        var hint = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false, Role = StandardLabelRole.Muted };
        panel.Add(hint);
        // Sender identity and the routine autosave state share one quiet line; save failures use feedback below.
        var sender = new StandardLabel { Wrapping = UiTextWrapping.Wrap, UseMnemonic = false };
        panel.Add(sender);
        var senderChoice = new StandardComboBox { AccessibleName = "Sender address for new draft", PreferredSize = new BSize(320, 32) };
        panel.Add(senderChoice); string[] shownSenders = [];

        // Validate rather than truncate: imported recipients and prefixed subjects must remain intact.
        StandardEdit Field(string placeholder) => new() { MaxLength = int.MaxValue, PlaceholderText = placeholder };
        var to = Field("name@example.com, another@example.com");
        var toField = new FormField("To", to);
        panel.Add(toField);
        var copies = new FormSection("Cc and Bcc", "", collapsible: true, expanded: false)
        {
            // Abbreviations keep their capitals in the sentence-case toggle.
            ShowText = "Show Cc and Bcc", HideText = "Hide Cc and Bcc",
        };
        // The toggle names this content as the part it controls; a screen reader following it lands on a name. The
        // section around it is already a group called "Cc and Bcc", so the content says what it holds instead of
        // repeating that on the way into a field.
        copies.Content.AccessibleName = "Cc and Bcc fields";
        panel.Add(copies);
        var cc = Field("Visible to all recipients");
        var bcc = Field("Hidden from other recipients");
        var ccField = new FormField("Cc", cc);
        var bccField = new FormField("Bcc", bcc);
        copies.Content.AddChild(ccField);
        copies.Content.AddChild(bccField);
        var recipientFields = new[] { ("To", toField), ("Cc", ccField), ("Bcc", bccField) };
        if (model.RecipientBook is not null) panel.Add(SavedRecipientsView.Create(model, field =>
        {
            if (field != "To") copies.IsExpanded = true;
            var destination = field == "To" ? to : field == "Cc" ? cc : bcc;
            if (destination.Session is { } session) FocusNavigation.FocusAndReveal(session, destination);
        }));
        var subject = Field("");
        panel.Add(new FormField("Subject", subject));
        // Always shown while a draft exists: files are copied into the saved draft, and the quota stays in sight.
        var attachments = new ComposerAttachmentsBar(model, Files);
        Attachments = attachments;
        panel.Add(attachments);
        var body = new StandardRichEdit { AcceptsReturn = true, PreferredSize = new BSize(520, 120), PlaceholderText = "Write your message. Only plain text is kept." };
        body.ApplyTheme(StandardControlPaint.Theme);
        var bodyArea = new FillLastStack { Spacing = 4, MinimumFillHeight = 120 };
        bodyArea.Add(new StandardLabel { Text = "Message", Target = body });
        bodyArea.Add(body);
        panel.Add(bodyArea);
        Recipients = to;
        Body = body;
        var check = new StandardButton { Text = "Check draft" };
        var discard = new StandardButton { Text = "Discard draft" };
        var save = new StandardButton { Text = "Save draft" };
        var send = new StandardButton { Text = "Send", IsDefault = true };
        var cancelSend = new StandardButton { Text = "Cancel send", Visibility = UiVisibility.Collapsed };
        CancellationTokenSource? submissionCancellation = null;
        var submission = new InlineFeedback();
        var sendHint = new InlineFeedback();
        var attachmentHint = new InlineFeedback();
        var sentCopy = new InlineFeedback();
        var storage = new InlineFeedback();
        var status = new InlineFeedback();
        var feedback = new StandardPanel { Spacing = 4 };
        // The pre-send attachment reminder: shown only after Send, inline and above every other line, never as a dialog.
        // Its contents are made the first time it is needed.
        var sendConfirmation = new StandardPanel { Spacing = 4, AccessibleName = "Attachment reminder before sending", Visibility = UiVisibility.Collapsed };
        SendConfirmation = sendConfirmation;
        StandardButton? confirmAttach = null;
        StandardButton? sendAnyway = null;
        feedback.AddChild(sendConfirmation);
        feedback.AddChild(submission); feedback.AddChild(sendHint); feedback.AddChild(sentCopy);
        feedback.AddChild(attachmentHint); feedback.AddChild(storage); feedback.AddChild(status);
        var surface = ConfigurationForm.NameFeedback(new FormSurface(panel, FormSurface.ActionBar(send, cancelSend, check, save, discard), feedback));
        Guid? shown = null;
        string? markedField = null;
        bool updating = false;
        var problemsShown = new HashSet<(InlineFeedback, string)>();
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
            to.Text = model.To; cc.Text = model.Cc; bcc.Text = model.Bcc;
            var choices = model.SenderChoices.ToArray();
            if (!shownSenders.SequenceEqual(choices)) { shownSenders = choices; senderChoice.SetItems(choices.Select(value => new UiComboBoxItem(value, value))); }
            senderChoice.Visibility = !model.HasDraft && choices.Length > 1 ? UiVisibility.Visible : UiVisibility.Collapsed;
            senderChoice.SelectedIndex = Array.IndexOf(choices, model.FromAddress); senderChoice.IsEnabled = model.CanStart;
            create.IsEnabled = model.CanStart;
            // Starting another message is impossible while a draft exists, so the row gives way to writing.
            actions.Visibility = model.HasDraft ? UiVisibility.Collapsed : UiVisibility.Visible;
            reply.IsEnabled = replyAll.IsEnabled = forward.IsEnabled = model.CanStart && !inbox.IsBusy && inbox.Body?.Composition is not null;
            foreach (var field in new[] { to, cc, bcc, subject }) { field.IsEnabled = model.HasDraft; field.IsReadOnly = !model.CanEdit; }
            body.IsEnabled = model.HasDraft;
            body.IsReadOnly = !model.CanEdit;
            attachments.Refresh();
            // A submitted draft can no longer change, so there is nothing left to check.
            check.IsEnabled = model.CanEdit;
            discard.IsEnabled = model.CanDiscard;
            save.IsEnabled = model.HasDraft && !model.IsBusy;
            send.IsEnabled = model.CanSend;
            bool submitting = submissionCancellation is not null && model.SubmissionState == DraftSubmissionState.Sending;
            cancelSend.Visibility = submitting ? UiVisibility.Visible : UiVisibility.Collapsed;
            cancelSend.IsEnabled = submitting && submissionCancellation?.IsCancellationRequested == false;
            attachmentHint.Set(model.AttachmentReminderText ?? "", FeedbackKind.Warning);
            bool confirming = model.ShowsSendConfirmation;
            if (confirming) EnsureSendConfirmation();
            bool confirmationHides = !confirming && sendConfirmation.Visibility == UiVisibility.Visible;
            var focusedBefore = surface.Session?.FocusedElement;
            sendConfirmation.Visibility = confirming ? UiVisibility.Visible : UiVisibility.Collapsed;
            ShowCopiesSummary();
            submission.Set(model.HasDraft && model.SubmissionState != DraftSubmissionState.Editing ? model.SubmissionText : "",
                model.SubmissionState switch { DraftSubmissionState.Failed => FeedbackKind.Error, DraftSubmissionState.Unknown => FeedbackKind.Warning,
                    DraftSubmissionState.Sending => FeedbackKind.Progress, DraftSubmissionState.Accepted => FeedbackKind.Success, _ => FeedbackKind.Information });
            sentCopy.Set(model.SentCopy != SentCopyState.NotRequested || model.SubmissionState == DraftSubmissionState.Accepted ? model.SentCopyText : "",
                model.SentCopy switch { SentCopyState.Failed => FeedbackKind.Error, SentCopyState.Unknown => FeedbackKind.Warning,
                    SentCopyState.Pending => FeedbackKind.Progress, SentCopyState.Saved => FeedbackKind.Success, _ => FeedbackKind.Information });
            bool storageProblem = model.StorageKind == FeedbackKind.Error;
            storage.Set(storageProblem && (model.HasDraft || model.HasLoadError) ? model.StorageStatus : "", model.StorageKind);
            string senderLabel = string.IsNullOrWhiteSpace(model.FromDisplayName) ? model.FromAddress : $"{model.FromDisplayName} <{model.FromAddress}>";
            sender.Text = model.HasDraft && !storageProblem ? $"From: {senderLabel} · {model.StorageStatus}" : $"From: {senderLabel}";
            sendHint.Set(model.SendUnavailableReason ?? "", FeedbackKind.Information);
            hint.Text = model.HasDraft ? "" : inbox.Body is { } loaded
                ? loaded.CompositionUnavailableReason ?? (loaded.Composition is null ? "Read the message again to load its reply headers." : "")
                : "To reply or forward, open a message in Inbox.";
            hint.Visibility = hint.Text.Length > 0 ? UiVisibility.Visible : UiVisibility.Collapsed;
            // Routine information goes to the shell footer; results, warnings, and errors stay beside the draft.
            // Once a draft has been submitted, the outcome lines above report the work in progress:
            // repeating it here would be read twice.
            // A file being read reports its progress in the attachment bar, beside its Cancel.
            status.Set(model.IsBusy ? !model.IsAddingAttachment && model.SubmissionState == DraftSubmissionState.Editing ? "Updating draft…" : ""
                : model.StatusKind == FeedbackKind.Information ? "" : model.Status,
                model.IsBusy ? FeedbackKind.Progress : model.StatusKind);
            ShowProblemsFirst();
            // A refused recipient field carries the error itself, as the account form's fields do: it shows the error
            // and reports Invalid with the error in its name, and its edit, which takes focus, reports Invalid with a
            // description that starts with the error. As there, the field (Cc and Bcc shown first) takes focus and is
            // scrolled into view, but only while the user is still on the form.
            foreach (var (name, field) in recipientFields) field.SetError(model.InvalidField == name ? model.Status : null);
            if (model.InvalidField is { } refused && refused != markedField
                && surface.Session is { } revealSession && FocusNavigation.MayTakeFocus(revealSession, surface))
            {
                // Cc and Bcc are shown first, and their summary gives way to them, so the field is scrolled to where
                // it ends up.
                if (refused != "To") { copies.IsExpanded = true; ShowCopiesSummary(); }
                surface.Reveal(recipientFields.Single(pair => pair.Item1 == refused).Item2);
            }
            markedField = model.InvalidField;
            // A confirmation that goes away with focus inside it (an edit removed the mention, the draft changed) hands
            // focus to Send, or to what follows when Send is unavailable.
            if (confirmationHides && focusedBefore is { } inside && inside.IsDescendantOf(sendConfirmation) && surface.Session is { } handOver)
            {
                handOver.RenderFrame();
                if (send.CanFocus) FocusNavigation.FocusAndReveal(handOver, send);
            }
            // A command that stays disabled once it has finished hands focus on: Discard draft to New
            // message, an accepted Send to the next enabled action (Save draft). Focus stays while it
            // runs, and a start button hidden by its own request leaves focus to that request.
            if (!model.IsBusy && !_commands.IsStarting && surface.Session is { } session)
                FocusNavigation.KeepFocusUsable(session, surface, create);
            updating = false;
        }
        void ShowProblemsFirst()
        {
            // The area is capped and scrolls at a large text size, so it starts with errors, then
            // warnings, then the other lines in their usual order: a hint or a success above an error
            // would push the error out of view. Moving a line keeps its text, so it is not announced again.
            var lines = new[] { submission, sendHint, sentCopy, storage, attachmentHint, status }.OrderBy(Rank).ToArray();
            // The attachment confirmation stays first: it answers the Send that was just clicked.
            for (int index = 0; index < lines.Length; index++) feedback.MoveChild(lines[index], index + 1);
            // The area may have been scrolled down to a line below, so a new problem is brought into view
            // at its top, once and without moving focus; scrolling it afterwards is left to the user.
            var problems = lines.Where(line => Rank(line) < 2).Select(line => (line, line.Message)).ToHashSet();
            if (!problems.IsSubsetOf(problemsShown) && feedback.Parent is UiScrollView area) area.ScrollToStart();
            problemsShown = problems;
        }
        static int Rank(InlineFeedback line) => line.Message.Length == 0 ? 2
            : line.Kind switch { FeedbackKind.Error => 0, FeedbackKind.Warning => 1, _ => 2 };
        void ShowCopiesSummary()
        {
            // The summary stands in for collapsed fields; expanded, the fields show the same thing.
            copies.Summary = copies.IsExpanded ? "" : string.Join(" · ", new[] { model.Cc.Length > 0 ? "Cc recipients included" : "", model.Bcc.Length > 0 ? "Bcc recipients included" : "" }.Where(text => text.Length > 0));
        }
        if (copies.Toggle is { } toggle) toggle.Clicked += (_, _) =>
        {
            ShowCopiesSummary();
            // A short dialog can leave the disclosure above the viewport after its fields collapse.
            if (surface.Session is { } session && session.FocusedElement == toggle)
                FocusNavigation.FocusAndReveal(session, toggle);
        };
        void Capture()
        {
            if (!updating) model.Edit(to.Text, cc.Text, bcc.Text, subject.Text, body.GetPlainText());
        }
        foreach (var field in new[] { to, cc, bcc, subject }) field.TextChanged += (_, _) => Capture();
        senderChoice.SelectionChanged += (_, _) => { if (!updating && senderChoice.SelectedIndex >= 0 && senderChoice.SelectedIndex < shownSenders.Length) model.SelectSender(shownSenders[senderChoice.SelectedIndex]); };
        body.DocumentChanged += (_, _) => Capture();
        create.Clicked += (_, _) => _commands.StartNew();
        reply.Clicked += (_, _) => _commands.Respond(CompositionKind.Reply);
        replyAll.Clicked += (_, _) => _commands.Respond(CompositionKind.ReplyAll);
        forward.Clicked += (_, _) => _commands.Respond(CompositionKind.Forward);
        check.Clicked += (_, _) => model.CheckDraft();
        discard.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.DiscardAsync());
        save.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await model.SaveAsync());
        async Task RunSend(Func<CancellationToken, Task> sending)
        {
            if (submissionCancellation is not null || !model.CanSend) return;
            using var cancellation = new CancellationTokenSource();
            submissionCancellation = cancellation;
            try { await sending(cancellation.Token); }
            finally { submissionCancellation = null; if (!surface.IsDisposed) Refresh(); }
        }
        void FocusInForm(UiElement target)
        {
            if (surface.IsDisposed || target.IsDisposed || surface.Session is not { } session) return;
            // Visibility changes invalidate the layout; lay out first so the reveal scrolls to where the target ends up.
            session.RenderFrame();
            if (target.CanFocus) FocusNavigation.FocusAndReveal(session, target);
        }
        void EnsureSendConfirmation()
        {
            if (confirmAttach is not null) return;
            var text = new StandardLabel { Text = SendConfirmationText, UseMnemonic = false, Wrapping = UiTextWrapping.Wrap };
            // Without a file picker (Android until UX-M5) the button leads to the typed path field instead.
            confirmAttach = new StandardButton
            {
                Text = attachments.CanPick ? "Attach file…" : "Add a file first",
                // The accessible name contains the visible label (WCAG 2.5.3), so speech control finds it by what it shows.
                AccessibleName = attachments.CanPick ? "Attach file before sending" : "Add a file first, then send",
                ToolTipText = attachments.CanPick ? "Choose a file to attach; the message is not sent until you click Send again" : "Type the full path of the file to attach",
            };
            // Neither is a default button: Enter never sends a message that leaves out its file.
            sendAnyway = new StandardButton { Text = "Send anyway", ToolTipText = "Send this message without an attachment" };
            var buttons = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 2, Spacing = 8, AccessibleName = "Attachment reminder actions" };
            buttons.AddChild(confirmAttach);
            buttons.AddChild(sendAnyway);
            sendConfirmation.AddChild(text);
            sendConfirmation.AddChild(buttons);
            confirmAttach.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
            {
                if (!model.ShowsSendConfirmation) return;
                if (!attachments.CanPick)
                {
                    model.DismissSendConfirmation();
                    attachments.FocusStart();
                    return;
                }
                bool added = await attachments.AttachAsync(confirmAttach);
                // The file is only added: sending still takes another click on Send.
                if (added) FocusInForm(send);
            });
            sendAnyway.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
            {
                if (!model.ShowsSendConfirmation) return;
                // Send stays focused while the message goes out, as after an ordinary Send.
                FocusInForm(send);
                await RunSend(model.SendAnywayAsync);
            });
        }
        send.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            if (submissionCancellation is not null || !model.CanSend) return;
            if (model.RequestSendConfirmation())
            {
                // Nothing is sent: the reminder asks first, inline, with its first choice focused and in view.
                if (feedback.Parent is UiScrollView area) area.ScrollToStart();
                if (confirmAttach is not null) FocusInForm(confirmAttach);
                return;
            }
            await RunSend(model.SendAsync);
        });
        _escape = () =>
        {
            if (surface.IsDisposed) return false;
            if (model.ShowsSendConfirmation)
            {
                model.DismissSendConfirmation();
                FocusInForm(send);
                return true;
            }
            if (model.IsAddingAttachment) { model.CancelAttachment(); return true; }
            return false;
        };
        cancelSend.Clicked += (_, _) => { submissionCancellation?.Cancel(); cancelSend.IsEnabled = false; };
        model.Changed += (_, _) => Refresh();
        inbox.Changed += (_, _) => Refresh();
        Refresh();
        return surface;
    }
}
