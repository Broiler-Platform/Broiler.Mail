// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   23
// Annotated:        0/23
// Exempt:           20
// Human-reviewed:   0/23
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       23
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// The composer's attachments, always shown while a draft exists: Attach file…, the quota line, one row per file with
/// its own Remove, and the progress of a file being read with Cancel. Choosing a file opens the shell's file dialog;
/// that dialog is the documented exemption to the no-modal rule, as it opens only when the person asks for it.
/// A device whose picker cannot open files yet keeps the typed path field instead, so it loses no function.
/// </summary>
internal sealed class ComposerAttachmentsBar : UiElement
{
    /// <summary>The quota line of a draft without attachments.</summary>
    public const string EmptyQuotaText = "Up to 4 files, 8 MiB per draft";
    /// <summary>Shown above the typed path field on a device whose picker cannot open files yet.</summary>
    public const string TypedPathHint = "No file picker on this device yet: type the full path.";
    /// <summary>The accessible name of the typed path field.</summary>
    public const string PathLabel = "Absolute attachment file path";
    /// <summary>The typed path field's placeholder.</summary>
    public const string PathPlaceholder = "Full path of a file to attach";

    private readonly ComposerViewModel _model;
    private readonly IMailFilePicker _files;
    private readonly StandardPanel _layout = new() { Spacing = 4, AccessibleName = "Attachments" };
    private readonly StandardButton _attach = new() { Text = "Attach file…", AccessibleName = "Attach file" };
    private readonly StandardLabel _quota = new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, Role = StandardLabelRole.Muted, AccessibleName = "Attachment quota" };
    private readonly StandardLabel _limit = new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, Role = StandardLabelRole.Muted, Visibility = UiVisibility.Collapsed };
    private readonly StandardEdit? _path;
    private readonly StandardButton? _add;
    private readonly StandardPanel _list = new() { Spacing = 2, AccessibleName = "Draft attachments", Visibility = UiVisibility.Collapsed };
    private readonly StandardToolbar _progress = new() { Overflow = UiToolbarOverflow.Wrap, Padding = 2, Spacing = 8, Visibility = UiVisibility.Collapsed };
    private readonly InlineFeedback _progressText = new();
    private readonly StandardButton _cancel = new() { Text = "Cancel", AccessibleName = "Cancel adding attachment" };
    private readonly InlineFeedback _feedback = new();
    private readonly List<(DraftAttachment Part, StandardButton Remove)> _rows = [];
    private readonly StandardPanel _forward = new() { Spacing = 2, AccessibleName = "Original attachments", Visibility = UiVisibility.Collapsed };
    private readonly StandardLabel _forwardText = new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap };
    private readonly StandardLabel _forwardReason = new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, Role = StandardLabelRole.Muted, Visibility = UiVisibility.Collapsed };
    private readonly StandardButton _include = new() { Text = "Include original attachments" };
    private readonly StandardButton _hideForward = MailToolbarButton.WithSymbol("Hide original attachments notice", "×");
    private IReadOnlyList<DraftAttachment>? _shown;

    public ComposerAttachmentsBar(ComposerViewModel model, IMailFilePicker files)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        AccessibleName = "Attachments";
        // The forward offer comes first: it names what the original carries before anything else is attached.
        var forwardActions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 2, Spacing = 8, AccessibleName = "Original attachment actions" };
        forwardActions.AddChild(_include);
        forwardActions.AddChild(_hideForward);
        _forward.AddChild(_forwardText);
        _forward.AddChild(_forwardReason);
        _forward.AddChild(forwardActions);
        _layout.AddChild(_forward);
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 2, Spacing = 8, AccessibleName = "Attachment actions" };
        if (CanPick)
        {
            actions.AddChild(_attach);
            actions.AddChild(_quota);
            _layout.AddChild(actions);
        }
        else
        {
            // One row, as with a picker, so the message keeps its height: the field, Add attachment, the quota and why.
            _path = new StandardEdit
            {
                MaxLength = 32767, AccessibleName = PathLabel, PlaceholderText = PathPlaceholder, ToolTipText = TypedPathHint,
                PreferredSize = new BSize(320, 32),
            };
            _add = new StandardButton { Text = "Add attachment" };
            actions.AddChild(_path);
            actions.AddChild(_add);
            actions.AddChild(_quota);
            actions.AddChild(new StandardLabel { Text = TypedPathHint, UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, Role = StandardLabelRole.Muted });
            _layout.AddChild(actions);
            _path.TextChanged += (_, _) => UpdateAdd();
            _add.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
            {
                string typed = _path.Text.Trim();
                if (typed.Length == 0) return;
                if (await AddAsync(() => _model.AddAttachmentAsync(typed)) && !_path.IsDisposed) _path.Text = "";
            });
        }
        _layout.AddChild(_limit);
        _layout.AddChild(_list);
        _progress.AddChild(_progressText);
        _progress.AddChild(_cancel);
        _layout.AddChild(_progress);
        _layout.AddChild(_feedback);
        AddChild(_layout);
        _attach.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await AttachAsync());
        _cancel.Clicked += (_, _) => _model.CancelAttachment();
        _include.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await IncludeForwardedAsync());
        _hideForward.Clicked += (_, _) =>
        {
            _model.DismissForwardAttachments();
            Refresh();
            Focus(Start);
        };
        Refresh();
    }

    /// <summary>Whether this device's picker can open files; otherwise the typed path field is shown.</summary>
    public bool CanPick => (_files.Capabilities & MailFilePickerCapabilities.OpenFile) != 0;

    /// <summary>The forward offer's Include original attachments button.</summary>
    internal StandardButton IncludeButton => _include;

    /// <summary>The forward offer's × button.</summary>
    internal StandardButton HideForwardButton => _hideForward;

    /// <summary>The Remove buttons, in the draft's order.</summary>
    internal IReadOnlyList<StandardButton> RemoveButtons => _rows.Select(row => row.Remove).ToArray();

    protected override BSize MeasureCore(BSize availableSize) => _layout.Measure(availableSize);
    protected override void ArrangeCore(BRect finalRect) => _layout.Arrange(finalRect);

    /// <summary>Brings the bar up to date with the draft; rows are rebuilt only when the attachment list changes.</summary>
    public void Refresh()
    {
        if (IsDisposed) return;
        Visibility = _model.HasDraft ? UiVisibility.Visible : UiVisibility.Collapsed;
        var parts = _model.Attachments;
        if (!ReferenceEquals(_shown, parts)) Rebuild(parts);
        string? limit = _model.AttachmentLimitReason;
        _attach.IsEnabled = CanPick && _model.CanAddAttachment;
        _attach.ToolTipText = limit ?? "Copy a file into this draft: up to 4 files, 8 MiB per draft";
        _quota.Text = parts.Count == 0 && _model.AttachmentRoomBytes >= DraftAttachment.MaximumBytes ? EmptyQuotaText : _model.AttachmentQuotaText;
        // The name carries the live quota too: a name of its own replaces the label's text for assistive technology.
        _quota.AccessibleName = "Attachment quota: " + _quota.Text;
        _limit.Text = limit ?? "";
        _limit.Visibility = limit is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        if (_path is not null) _path.IsEnabled = _model.CanAddAttachment;
        UpdateAdd();
        foreach (var (_, remove) in _rows) remove.IsEnabled = _model.CanEdit;
        bool reading = _model.IsAddingAttachment;
        _progress.Visibility = reading ? UiVisibility.Visible : UiVisibility.Collapsed;
        _progressText.Set(reading ? _model.AttachmentReadProgress : "", FeedbackKind.Progress);
        _cancel.IsEnabled = reading;
        RefreshForward();
    }

    private void RefreshForward()
    {
        var offer = _model.ForwardAttachments;
        _forward.Visibility = offer is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        if (offer is null) return;
        string? reason = _model.ForwardAttachmentsUnavailableReason;
        _forwardText.Text = offer.Text;
        _forwardReason.Text = reason ?? "";
        _forwardReason.Visibility = reason is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        // Without a reader nothing can be included, so the button is not offered at all.
        _include.Visibility = _model.AttachmentReader is null ? UiVisibility.Collapsed : UiVisibility.Visible;
        _include.IsEnabled = _model.CanIncludeForwardAttachments;
        _include.ToolTipText = reason ?? "Copy the original message's attachments into this draft";
    }

    /// <summary>
    /// Includes the forwarded message's files; focus goes to Cancel while they download, then to the first included
    /// file's Remove, or back to Include original attachments when nothing was included.
    /// </summary>
    internal async Task<bool> IncludeForwardedAsync()
    {
        if (IsDisposed || !_model.CanIncludeForwardAttachments) return false;
        var before = _model.Attachments;
        _feedback.Set("", FeedbackKind.Information);
        var running = _model.IncludeForwardAttachmentsAsync();
        if (_model.IsAddingAttachment) Focus(_cancel);
        await running;
        if (IsDisposed) return false;
        Refresh();
        var after = _model.Attachments;
        bool added = !ReferenceEquals(before, after) && after.Count > before.Count;
        int first = added ? _rows.FindIndex(row => ReferenceEquals(row.Part, after[before.Count])) : -1;
        if (first >= 0) Focus(_rows[first].Remove);
        else Focus(_include);
        return added;
    }

    private void UpdateAdd()
    {
        if (_add is not null && _path is not null) _add.IsEnabled = _model.CanAddAttachment && !string.IsNullOrWhiteSpace(_path.Text);
    }

    private void Rebuild(IReadOnlyList<DraftAttachment> parts)
    {
        _shown = parts;
        foreach (var child in _list.Children.ToArray()) { _list.RemoveChild(child); child.Dispose(); }
        _rows.Clear();
        foreach (var part in parts)
        {
            var row = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 2, Spacing = 8, AccessibleName = part.FileName };
            row.AddChild(new StandardLabel { Text = $"{part.FileName} · {MailByteSize.Format(part.Content.Length)}", UseMnemonic = false, Wrapping = UiTextWrapping.Wrap });
            var remove = new StandardButton { Text = "Remove", AccessibleName = $"Remove {part.FileName}", ToolTipText = $"Remove {part.FileName} from this draft" };
            remove.Clicked += (_, _) => Remove(part);
            row.AddChild(remove);
            _list.AddChild(row);
            _rows.Add((part, remove));
        }
        _list.Visibility = parts.Count > 0 ? UiVisibility.Visible : UiVisibility.Collapsed;
    }

    /// <summary>
    /// Picks a file and adds it: Attach file…, or the pre-send reminder's own button as <paramref name="origin"/>. A canceled,
    /// rejected or failed pick returns focus to where it started; with an origin, a successful add leaves focus to the caller.
    /// Returns whether a file was added.
    /// </summary>
    internal async Task<bool> AttachAsync(UiElement? origin = null)
    {
        if (IsDisposed || !CanPick || !_model.CanAddAttachment) return false;
        var back = origin ?? _attach;
        Guid? draft = _model.DraftId;
        _feedback.Set("", FeedbackKind.Information);
        var pick = await _files.PickOpenFileAsync(new MailFileRequest(MailFilePurpose.Attachment, "Attach file") { AcceptText = "Attach" });
        if (IsDisposed) return false;
        if (pick.Status == MailFilePickStatus.Canceled) { Focus(back); return false; }
        if (pick.Status != MailFilePickStatus.Picked || pick.Item is not { } file)
        {
            _feedback.Set(pick.Message ?? "No file was chosen.", FeedbackKind.Warning);
            Focus(back);
            return false;
        }
        if (_model.DraftId != draft)
        {
            _feedback.Set("The draft changed while the file was being chosen. Nothing was attached.", FeedbackKind.Warning);
            Focus(back);
            return false;
        }
        return await AddAsync(() => _model.AddAttachmentAsync(file), origin);
    }

    /// <summary>Moves focus to where adds start: Attach file…, or the typed path field without a picker.</summary>
    internal void FocusStart() => Focus(Start);

    /// <summary>
    /// Runs one add; focus goes to Cancel while it reads, then to the new row's Remove, or back to where adds start
    /// (to <paramref name="origin"/>, when one is given, after a failure; its caller places focus after a success).
    /// </summary>
    private async Task<bool> AddAsync(Func<Task> add, UiElement? origin = null)
    {
        var before = _model.Attachments;
        var running = add();
        if (_model.IsAddingAttachment) Focus(_cancel);
        await running;
        if (IsDisposed) return false;
        var after = _model.Attachments;
        bool added = !ReferenceEquals(before, after) && after.Count > before.Count;
        if (origin is not null) { if (!added) Focus(origin); }
        else if (added && _rows.Count > 0 && ReferenceEquals(_rows[^1].Part, after[^1])) Focus(_rows[^1].Remove);
        else Focus(Start);
        return added;
    }

    private UiElement Start => CanPick ? _attach : _path ?? (UiElement)_attach;

    private void Remove(DraftAttachment part)
    {
        int index = _rows.FindIndex(row => ReferenceEquals(row.Part, part));
        if (index < 0) return;
        // Chosen before the rows are rebuilt: the next file, else the previous one, else where adds start.
        DraftAttachment? next = index + 1 < _rows.Count ? _rows[index + 1].Part : index > 0 ? _rows[index - 1].Part : null;
        _model.RemoveAttachment(part);
        if (IsDisposed || _model.Attachments.Any(item => ReferenceEquals(item, part))) return;
        Refresh();
        int target = next is null ? -1 : _rows.FindIndex(row => ReferenceEquals(row.Part, next));
        Focus(target >= 0 ? _rows[target].Remove : Start);
    }

    private void Focus(UiElement target)
    {
        if (IsDisposed || target.IsDisposed || Session is not { } session) return;
        // Visibility changes invalidate the layout; lay out first so the reveal scrolls to where the target ends up.
        session.RenderFrame();
        if (target.CanFocus) FocusNavigation.FocusAndReveal(session, target);
        else if (Start is { CanFocus: true } start) FocusNavigation.FocusAndReveal(session, start);
    }
}
