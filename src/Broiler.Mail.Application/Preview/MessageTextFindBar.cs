using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.Graphics.Geometry;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Toolbar;

namespace Broiler.Mail.Application.Preview;

/// <summary>Owned by one reader body; only the displayed decoded text is searched and explicitly copied.</summary>
public sealed class MessageTextFindBar : UiElement
{
    private readonly StandardPanel _layout = new() { Spacing = 4 };
    private readonly ScrollableMessageText _text;
    private readonly string _body;
    private readonly int _offset;
    private readonly Action _showText;
    private readonly Func<bool> _current;
    private readonly Action _returnFocus;
    private readonly StandardEdit _query = new() { MaxLength = 256, AccessibleName = "Find in message text", PlaceholderText = "Literal text (not mailbox search)" };
    private readonly StandardButton _next = new() { Text = "Next match" }, _previous = new() { Text = "Previous match" }, _copy = new() { Text = "Copy match" };
    private readonly InlineFeedback _feedback = new();
    private MessageTextMatches _result = new([], false);
    private int _index = -1;
    public string Status => _feedback.Message;
    public MessageTextFindBar(ScrollableMessageText text, string body, int offset, Action showText, Func<bool> current, Action returnFocus)
    {
        _text = text; _body = body; _offset = offset; _showText = showText; _current = current; _returnFocus = returnFocus;
        AddChild(_layout); Visibility = UiVisibility.Collapsed; AccessibleName = "Find selected message text";
        ConfigurationForm.AddText(_layout, "Find searches this decoded plain-text preview only, not HTML markup, attachments or server-only text. At most 1,000 non-overlapping matches; no network access. HTML uses its selectable text alternative.");
        _layout.AddChild(_query);
        var actions = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Spacing = 6, Padding = 4 };
        var find = new StandardButton { Text = "Find text now" }; var close = new StandardButton { Text = "Close message find" };
        foreach (var button in new[] { find, _previous, _next, _copy, close }) actions.AddChild(button);
        _layout.AddChild(actions); _layout.AddChild(_feedback);
        find.Clicked += (_, _) => Search(); _query.Submitted += (_, _) => Search();
        _next.Clicked += (_, _) => Move(1); _previous.Clicked += (_, _) => Move(-1); _copy.Clicked += (_, _) => Copy(); close.Clicked += (_, _) => CloseFind();
        _query.TextChanged += (_, _) => { _result = new([], false); _index = -1; Feedback("Press Find to search the edited text."); };
        Feedback("Enter text, then Find. Ctrl+F focuses this field; F3/Shift+F3 navigate matches; Escape closes find.");
    }
    protected override BSize MeasureCore(BSize size) => _layout.Measure(size);
    protected override void ArrangeCore(BRect rect) => _layout.Arrange(rect);
    private bool Current => !IsDisposed && _current();
    public void ShowFind()
    {
        if (!Current) return; _showText(); Visibility = UiVisibility.Visible;
        if (Session is { } session) FocusNavigation.FocusAndReveal(session, _query);
    }
    public void CloseFind()
    {
        if (!Current) return; Visibility = UiVisibility.Collapsed; _returnFocus();
    }
    public void Search()
    {
        if (!Current) return; _showText();
        try { _result = MessageTextFinder.Find(_body, _query.Text); _index = -1; if (_result.Matches.Count > 0) Move(1); else Feedback("No matches in this saved text preview."); }
        catch (ArgumentException error) { _result = new([], false); _index = -1; Feedback(error.Message, true); }
    }
    private void Move(int direction)
    {
        if (!Current || _result.Matches.Count == 0) return; _showText();
        bool wrapped = direction > 0 ? _index == _result.Matches.Count - 1 : _index <= 0;
        _index = (_index + direction + _result.Matches.Count) % _result.Matches.Count;
        var match = _result.Matches[_index]; _text.SelectFindMatch(_offset + match.Start, match.Length);
        Feedback($"Match {_index + 1} of {_result.Matches.Count}{(_result.HasMore ? "+ (first 1,000 only)" : "")}.{(wrapped ? " Wrapped within available matches." : "")}");
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
    public bool HandleFindInput(UiInputEvent input)
    {
        if (!Current || input.Kind != UiInputEventKind.KeyboardKey || input.KeyTransition != KeyboardKeyTransition.Down) return false;
        var modifiers = input.KeyModifiers;
        bool control = (modifiers & (KeyboardModifierState.Control | KeyboardModifierState.LeftControl | KeyboardModifierState.RightControl)) != 0;
        bool shift = (modifiers & (KeyboardModifierState.Shift | KeyboardModifierState.LeftShift | KeyboardModifierState.RightShift)) != 0;
        if ((modifiers & (KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt | KeyboardModifierState.RightAlt | KeyboardModifierState.LeftWindows | KeyboardModifierState.RightWindows)) != 0) return false;
        if (control && !shift && input.NativeKeyCode == 0x46) { ShowFind(); return true; }
        if (!control && input.NativeKeyCode == 0x72 && Visibility == UiVisibility.Visible) { if (_result.Matches.Count == 0) Search(); else Move(shift ? -1 : 1); return true; }
        if (!control && !shift && input.NativeKeyCode == 0x1B && Visibility == UiVisibility.Visible) { CloseFind(); return true; }
        return false;
    }
}
