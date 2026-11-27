// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   16
// Annotated:        0/16
// Exempt:           15
// Human-reviewed:   0/16
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       16
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Toolbar;

namespace Broiler.Mail.Application.Preview;

/// <summary>
/// Owned by one reader body; only the displayed decoded text is searched and explicitly copied. A compact
/// strip above the message text: the query and its commands on one wrapping row, and a counter or problem
/// below them only while there is one to show.
/// </summary>
public sealed class MessageTextFindBar : UiElement
{
    private const string Scope = "Find searches the decoded plain text shown in the reader (the 32,000-character preview unless the full message text is shown), not HTML markup, attachments or server-only text. At most 1,000 non-overlapping matches; no network access. HTML uses its selectable text alternative.";
    private readonly StandardPanel _layout = new() { Spacing = 2 };
    private readonly ScrollableMessageText _text;
    private string _body;
    private readonly int _offset;
    private readonly Action _showText;
    private readonly Func<bool> _current;
    private readonly Action _returnFocus;
    private readonly StandardEdit _query = new() { MaxLength = 256, AccessibleName = "Find in message text", PlaceholderText = "Find in this message", ToolTipText = Scope, PreferredSize = new BSize(220, 32) };
    // Previous, Next and Close show a symbol, so the strip keeps to one row; their names stay their text.
    private readonly StandardButton _next = MailToolbarButton.WithSymbol("Next match", "↓", "F3, or Enter in the query"),
        _previous = MailToolbarButton.WithSymbol("Previous match", "↑", "Shift+F3"), _copy = new() { Text = "Copy match" };
    private readonly InlineFeedback _feedback = new();
    private MessageTextMatches _result = new([], false);
    private int _index = -1;
    public string Status => _feedback.Message;
    /// <summary>Added to "No matches" while the reader shows a preview that the complete text could extend.</summary>
    public string? MoreTextHint { get; set; }
    /// <summary>
    /// While this returns true, Escape is left to cancel the running work, as it is with focus anywhere else, and
    /// does not close the bar; the bar's close button still does.
    /// </summary>
    public Func<bool>? IsWorkRunning { get; init; }
    public MessageTextFindBar(ScrollableMessageText text, string body, int offset, Action showText, Func<bool> current, Action returnFocus)
    {
        _text = text; _body = body; _offset = offset; _showText = showText; _current = current; _returnFocus = returnFocus;
        AddChild(_layout); Visibility = UiVisibility.Collapsed; AccessibleName = "Find selected message text";
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Spacing = 6, Padding = 4, AccessibleName = "Find in message" };
        var find = new StandardButton { Text = "Find", AccessibleName = "Find text now", ToolTipText = "Find text now (Enter)" };
        var close = MailToolbarButton.WithSymbol("Close message find", "×", "Escape");
        foreach (var control in new UiElement[] { _query, find, _previous, _next, _copy, close }) actions.AddChild(control);
        _layout.AddChild(actions); _layout.AddChild(_feedback);
        find.Clicked += (_, _) => Search(); _query.Submitted += (_, _) => Search();
        _next.Clicked += (_, _) => Move(1); _previous.Clicked += (_, _) => Move(-1); _copy.Clicked += (_, _) => Copy(); close.Clicked += (_, _) => CloseFind();
        _query.TextChanged += (_, _) => { _result = new([], false); _index = -1; Feedback("Press Enter to search."); };
        Feedback("");
    }
    // While the bar is open its feedback keeps its room: one line before there is anything to say, and then the
    // most it has needed at about this width since the bar opened, so the text below does not move as the feedback
    // appears, changes and clears while the user types and searches. A width that changes by no more than a scroll
    // bar, as the banner slot's does while it starts or stops scrolling, keeps the room.
    private double _openHeight, _openWidth = double.NaN;
    protected override BSize MeasureCore(BSize size)
    {
        var measured = _layout.Measure(size);
        double height = measured.Height;
        if (_feedback.Visibility == UiVisibility.Collapsed)
            height += _layout.Spacing + BTextMeasurer.GetLineHeight(StandardThemeFonts.For(StandardControlPaint.GetTheme(this), StandardTextStyle.Body)) + 16;
        if (!(Math.Abs(size.Width - _openWidth) <= 32)) { _openWidth = size.Width; _openHeight = 0; }
        _openHeight = Math.Max(_openHeight, height);
        return new BSize(measured.Width, _openHeight);
    }
    protected override void ArrangeCore(BRect rect) => _layout.Arrange(new BRect(rect.X, rect.Y, rect.Width, Math.Min(rect.Height, _layout.DesiredSize.Height)));
    private bool Current => !IsDisposed && _current();
    public void ShowFind()
    {
        if (!Current) return; _showText();
        if (Visibility != UiVisibility.Visible) { _openHeight = 0; Visibility = UiVisibility.Visible; }
        if (Session is { } session) FocusNavigation.FocusAndReveal(session, _query);
    }
    public void CloseFind()
    {
        if (!Current) return; Visibility = UiVisibility.Collapsed; _openHeight = 0; _returnFocus();
    }
    public void Search()
    {
        if (!Current) return; _showText();
        try { _result = MessageTextFinder.Find(_body, _query.Text); _index = -1; if (_result.Matches.Count > 0) Move(1); else Feedback(MoreTextHint is { Length: > 0 } hint ? "No matches in the 32,000-character preview. " + hint : "No matches in this saved text."); }
        catch (ArgumentException error) { _result = new([], false); _index = -1; Feedback(error.Message, true); }
    }
    /// <summary>
    /// Searches <paramref name="body"/> from now on, as the reader now shows it: earlier matches are dropped, and
    /// a visible bar with a query searches the new text again at once.
    /// </summary>
    public void ReplaceText(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Current) return;
        _body = body; _result = new([], false); _index = -1;
        if (Visibility == UiVisibility.Visible && _query.Text.Length > 0) Search();
        else Feedback(Visibility == UiVisibility.Visible ? "Full message text loaded. Press Find to search it." : "");
    }
    private void Move(int direction)
    {
        if (!Current || _result.Matches.Count == 0) return; _showText();
        bool wrapped = direction > 0 ? _index == _result.Matches.Count - 1 : _index <= 0;
        _index = (_index + direction + _result.Matches.Count) % _result.Matches.Count;
        var match = _result.Matches[_index]; _text.SelectFindMatch(_offset + match.Start, match.Length);
        Feedback($"Match {_index + 1} of {_result.Matches.Count}{(_result.HasMore ? "+ (first 1,000 only)" : "")}.{(wrapped ? " Wrapped." : "")}");
    }
    private void Copy()
    {
        if (!Current || _index < 0) return;
        if (Session?.Host is not IUiClipboardHost clipboard) { Feedback("Clipboard is unavailable; select the match in the text alternative and copy there.", true); return; }
        var match = _result.Matches[_index];
        try { clipboard.SetText(_body.Substring(match.Start, match.Length)); Feedback("Current match copied as plain text. Other message content was not copied."); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or UnauthorizedAccessException or System.IO.IOException or System.Runtime.InteropServices.ExternalException)
        { Feedback("Clipboard copy failed; the message and selection were kept. Try copying again.", true); }
    }
    private void Feedback(string value, bool error = false)
    {
        _previous.IsEnabled = _next.IsEnabled = _result.Matches.Count > 0;
        _copy.IsEnabled = _index >= 0;
        _feedback.Set(value, error ? FeedbackKind.Error : FeedbackKind.Information);
    }
    protected override bool OnInput(UiInputEvent input) => HandleFindInput(input) || base.OnInput(input);

    public bool HandleFindInput(UiInputEvent input)
    {
        if (!Current || input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        var modifiers = input.KeyModifiers;
        bool control = (modifiers & (KeyboardModifierState.Control | KeyboardModifierState.LeftControl | KeyboardModifierState.RightControl)) != 0;
        bool shift = (modifiers & (KeyboardModifierState.Shift | KeyboardModifierState.LeftShift | KeyboardModifierState.RightShift)) != 0;
        if ((modifiers & (KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt | KeyboardModifierState.RightAlt | KeyboardModifierState.LeftWindows | KeyboardModifierState.RightWindows)) != 0) return false;
        if (control && !shift && input.NativeKeyCode == 0x46) { ShowFind(); return true; }
        if (!control && input.NativeKeyCode == 0x72 && Visibility == UiVisibility.Visible) { if (_result.Matches.Count == 0) Search(); else Move(shift ? -1 : 1); return true; }
        // Enter in the query searches it (Submitted); once that search found matches and the query is
        // unchanged, Enter and Shift+Enter move to the next and previous match as F3 and Shift+F3 do.
        if (!control && input.NativeKeyCode == 0x0D && Visibility == UiVisibility.Visible && Session?.FocusedElement == _query && _result.Matches.Count > 0)
        { Move(shift ? -1 : 1); return true; }
        if (!control && !shift && input.NativeKeyCode == 0x1B && Visibility == UiVisibility.Visible && IsWorkRunning?.Invoke() != true) { CloseFind(); return true; }
        return false;
    }
}
