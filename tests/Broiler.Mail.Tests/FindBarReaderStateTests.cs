using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// The find bar follows the reader that holds it, over a cache that can search: leaving the compact reader for
/// search closes the bar, and Ctrl+F on a message whose body failed to load keeps that message's reader.
/// </summary>
[Collection("UI theme")]
public sealed class FindBarReaderStateTests
{
    /// <summary>
    /// Opening search from the compact reader goes back to the list and closes the reader's find bar there: Escape
    /// and Back are then the search header's, and focus stays in its query, out of the hidden reader.
    /// </summary>
    [Fact]
    public async Task OpeningSearchFromTheCompactReaderClosesItsFindBar()
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        using var app = await InlineMailFixture.OpenAsync(640, 480);
        await app.OpenMessageAsync(0);
        Assert.True(app.Layout.ShowsReaderOnly);
        Assert.True(app.Shell.Inbox.ShowFindInMessage());
        app.Render();
        Assert.True(app.Shown(app.Find));

        Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('K', KeyboardModifierState.Control)));
        app.Render();
        Assert.False(app.Layout.ShowsReaderOnly);
        Assert.Equal(UiVisibility.Collapsed, app.Find.Visibility);
        Assert.Same(app.Query, app.Session.FocusedElement);
        Assert.False(app.Shell.Inbox.CloseFindInMessage());

        // Escape is the search header's: it closes the empty header, and focus never goes into the hidden reader.
        app.Press(InlineMailFixture.Key(0x1B));
        app.Drain();
        Assert.False(app.Layout.ShowsReaderOnly);
        var focused = app.Session.FocusedElement;
        Assert.False(focused is not null && (focused == app.Text.Editor || focused.IsDescendantOf(app.Text)), "Escape moved focus into the hidden reader.");
    }

    /// <summary>
    /// Ctrl+F on a message whose body failed to load keeps the compact reader with its problem and Retry; it does not
    /// go back to the list to open mailbox search. Back in the list, Ctrl+F opens search as it does with no message.
    /// </summary>
    [Fact]
    public async Task CtrlFOnAMessageThatFailedToLoadKeepsTheReader()
    {
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        using var app = await InlineMailFixture.OpenAsync(640, 480);
        app.Receiver.FailBodies = true;
        try { await app.Inbox.SelectAsync(app.Inbox.Messages[0].Key); } catch (Exception) { }
        app.Drain();
        Assert.True(app.Shell.Inbox.OpenSelected());
        app.Drain();
        Assert.Null(app.Inbox.Body);
        Assert.True(app.Layout.ShowsReaderOnly);
        Assert.True(app.Shell.Inbox.ShowsMessage);
        Assert.True(app.Shown(app.Button("Retry loading")), "The reader does not offer Retry.");

        Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('F', KeyboardModifierState.Control)));
        app.Render();
        Assert.True(app.Layout.ShowsReaderOnly);
        Assert.Null(app.Shell.ActiveDialog);
        Assert.True(app.Shown(app.Button("Retry loading")), "Ctrl+F hid the message's Retry.");
        Assert.False(app.Shown(app.Query), "Ctrl+F opened mailbox search.");

        Assert.True(app.Shell.Inbox.GoBackToList());
        app.Render();
        Assert.False(app.Shell.Inbox.ShowsMessage);
        Assert.True(app.Keyboard.Handle(InlineMailFixture.Key('F', KeyboardModifierState.Control)));
        app.Render();
        Assert.True(app.Shown(app.Query), "Ctrl+F in the list did not open mailbox search.");
        Assert.Same(app.Query, app.Session.FocusedElement);
    }
}
