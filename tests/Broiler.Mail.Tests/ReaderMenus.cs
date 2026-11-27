using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Menu;

namespace Broiler.Mail.Tests;

/// <summary>Opens one of the reader's popup menus and chooses an item as a keyboard user does.</summary>
internal static class ReaderMenus
{
    public static MailMenuButton Button(UiElement root, string trigger) =>
        Descendants(root).OfType<MailMenuButton>().Single(button => button.Trigger.Text == trigger);

    /// <summary>The items the menu offers when it opens now; it is closed again afterwards.</summary>
    public static IReadOnlyList<UiMenuItem> Items(UiElement root, string trigger)
    {
        var button = Button(root, trigger);
        Assert.True(button.Expand(), $"{trigger} does not open.");
        var items = button.Menu.Items.ToArray();
        Assert.True(button.Collapse());
        return items;
    }

    /// <summary>Opens the menu, selects <paramref name="item"/> and presses Enter, then runs what the menu posted.</summary>
    public static void Invoke(UiSession session, UiElement root, string trigger, string item, bool drain = true)
    {
        var button = Button(root, trigger);
        Assert.True(button.Expand(), $"{trigger} does not open.");
        Assert.Same(button.Menu, session.CapturedElement);
        int index = button.Menu.Items.ToList().FindIndex(entry => entry.Text == item);
        Assert.True(index >= 0, $"{trigger} has no {item}: {string.Join(", ", button.Menu.Items.Select(entry => entry.Text))}");
        Assert.True(button.Menu.Items[index].IsEnabled, $"{item} is disabled.");
        button.Menu.SetSelectedPath([index]);
        Assert.Equal(index, button.Menu.SelectedPath[0]);
        Assert.True(session.DispatchInput(Key(0x0D, "Enter")));
        Assert.Null(session.CapturedElement);
        Assert.False(button.IsExpanded);
        if (drain && session.Dispatcher is TestQueueDispatcher queue) queue.Drain();
    }

    public static UiInputEvent Key(int code, string name, KeyboardModifierState modifiers = KeyboardModifierState.None) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new Broiler.Input.InputEventHeader(Broiler.Input.InputDeviceId.FromOpaqueValue("reader-menu-test"),
                new Broiler.Input.InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName(name), KeyboardKeyTransition.Down, modifiers, code, 0, 1, false, false,
            Source: Broiler.Input.InputEventSource.Synthetic));

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
