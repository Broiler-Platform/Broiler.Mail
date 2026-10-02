using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.ListView.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-02: a successful refresh keeps a still-valid reading context and explains any change to it.</summary>
[Collection("UI theme")]
public sealed class RefreshContinuityTests
{
    [Fact]
    public async Task RefreshKeepsTheOpenMessageAndBodyAndUpdatesItsMetadata()
    {
        var account = TestDirectory.Profile();
        var open = Message(account, 5);
        var receiver = Receiver([open, Message(account, 4)], null);
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        await model.SelectAsync(open.Key);
        var body = model.Body;

        receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 6), open with { IsRead = true }, Message(account, 4)], null));
        await model.ReceiveAsync();

        Assert.Equal(3, model.Messages.Count);
        Assert.Equal(open.Key, model.SelectedMessage?.Key);
        Assert.True(model.SelectedMessage!.IsRead);
        Assert.Same(body, model.Body);
        Assert.DoesNotContain("Select one", model.Status);
    }

    [Fact]
    public async Task MessageOlderThanTheRefreshedPageStaysOpenAndCanBeReadAgain()
    {
        var account = TestDirectory.Profile();
        var open = Message(account, 2);
        var receiver = Receiver([Message(account, 3)], new(account.Id, 7, 4, 3, 1));
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        receiver.Inbox = (cursor, _) => Task.FromResult(cursor is null
            ? new MailInboxPage([Message(account, 3)], new(account.Id, 7, 4, 3, 1))
            : new MailInboxPage([open, Message(account, 1)], null));
        await model.LoadOlderAsync();
        await model.SelectAsync(open.Key);
        var body = model.Body;

        await model.ReceiveAsync();

        Assert.DoesNotContain(model.Messages, message => message.Key == open.Key);
        Assert.Equal(open.Key, model.SelectedMessage?.Key);
        Assert.Same(body, model.Body);
        Assert.Contains("older than the newest page", model.Status);

        await model.SelectAsync(open.Key);
        Assert.Equal(open.Key, model.Body?.Key);
        Assert.NotSame(body, model.Body);
    }

    [Theory]
    [InlineData("within-page")]
    [InlineData("whole-mailbox")]
    [InlineData("empty")]
    public async Task MessageMissingFromARangeTheRefreshCoversIsClosed(string variant)
    {
        var account = TestDirectory.Profile();
        var open = Message(account, 5);
        var receiver = Receiver([Message(account, 6), open], new(account.Id, 7, 7, 6, 3));
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        await model.SelectAsync(open.Key);

        receiver.Inbox = (_, _) => Task.FromResult(variant switch
        {
            // UIDs 4 and 6 were fetched contiguously, so 5 is no longer in the mailbox.
            "within-page" => new MailInboxPage([Message(account, 6), Message(account, 4)], new(account.Id, 7, 7, 5, 2)),
            "whole-mailbox" => new MailInboxPage([Message(account, 6)], null),
            _ => new MailInboxPage([], null),
        });
        await model.ReceiveAsync();

        Assert.Null(model.SelectedMessage);
        Assert.Null(model.Body);
        Assert.Contains(variant == "empty" ? "The inbox is empty." : "no longer in the inbox", model.Status);
    }

    [Fact]
    public async Task ChangedUidValidityClosesTheOpenMessage()
    {
        var account = TestDirectory.Profile();
        var open = Message(account, 5);
        var receiver = Receiver([open], null);
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        await model.SelectAsync(open.Key);

        // The same UID under a new UIDVALIDITY is a different message.
        receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage([open with { Key = open.Key with { UidValidity = 8 } }], null));
        await model.ReceiveAsync();

        Assert.Null(model.SelectedMessage);
        Assert.Null(model.Body);
        Assert.Contains("renumbered", model.Status);
    }

    [Fact]
    public async Task LoadingOlderPagesNeverChangesTheOpenMessage()
    {
        var account = TestDirectory.Profile();
        var open = Message(account, 3);
        var receiver = Receiver([open], new(account.Id, 7, 4, 3, 1));
        using var model = Model(receiver, account);
        await model.ReceiveAsync();
        await model.SelectAsync(open.Key);
        var body = model.Body;
        receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 2), Message(account, 1)], null));

        await model.LoadOlderAsync();

        Assert.Equal(3, model.Messages.Count);
        Assert.Same(body, model.Body);
        Assert.Equal(open.Key, model.SelectedMessage?.Key);
    }

    [Fact]
    public async Task ViewKeepsReaderScrollSelectionListAnchorAndPreviewAcrossRefresh()
    {
        var account = TestDirectory.Profile();
        var messages = Enumerable.Range(1, 40).Select(uid => Message(account, (uint)uid)).Reverse().ToArray();
        var open = messages[25];
        var receiver = Receiver(messages, null);
        receiver.Body = (key, _) => Task.FromResult(new MailMessageBody(key, string.Join('\n', Enumerable.Repeat("A line of message text.", 200)), "<p>HTML</p>"));
        var dispatcher = new ImmediateUiDispatcher();
        var preview = new CountingPreviewHost();
        using var model = new InboxViewModel(receiver, dispatcher);
        model.SetAccount(account);
        using var content = new InboxView(model, preview).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(900, 500));
        session.AddRoot(content);
        await model.ReceiveAsync();
        await model.SelectAsync(open.Key);
        _ = session.RenderFrame();

        var list = Descendants(content).OfType<StandardListView>().Single();
        list.ScrollIntoView(30);
        _ = session.RenderFrame();
        int firstVisible = list.FirstVisibleIndex;
        string firstVisibleId = list.Items[firstVisible].Id;
        Assert.True(firstVisible > 0);

        var reader = Descendants(content).OfType<ScrollableMessageText>().Single();
        var scroll = Descendants(reader).OfType<StandardScrollView>().Single();
        Assert.True(scroll.ScrollBy(0, 400));
        double readerOffset = scroll.VerticalOffset;
        session.SetFocus(reader.Editor);
        new StandardInputRoute(session).Dispatch(SelectAll());
        var selection = reader.Editor.Selection;
        Assert.False(selection.IsEmpty);
        int closes = preview.Closes;

        // Two new messages arrive above the anchored rows.
        receiver.Inbox = (_, _) => Task.FromResult(new MailInboxPage([Message(account, 42), Message(account, 41), .. messages], null));
        await model.ReceiveAsync();
        _ = session.RenderFrame();

        Assert.Equal(42, list.Items.Count);
        Assert.Equal(firstVisibleId, list.Items[list.FirstVisibleIndex].Id);
        Assert.Equal($"7:{open.Key.Uid}", list.SelectedItemId);
        Assert.Equal(readerOffset, scroll.VerticalOffset);
        Assert.Equal(selection, reader.Editor.Selection);
        Assert.Equal(closes, preview.Closes);
    }

    private static TestMailReceiver Receiver(IReadOnlyList<MailMessageSummary> newest, MailInboxCursor? older) => new()
    {
        Inbox = (_, _) => Task.FromResult(new MailInboxPage(newest, older)),
        Body = (key, _) => Task.FromResult(new MailMessageBody(key, $"Body {key.Uid}")),
    };

    private static InboxViewModel Model(TestMailReceiver receiver, AccountProfile account)
    {
        var model = new InboxViewModel(receiver, new ImmediateUiDispatcher());
        model.SetAccount(account);
        return model;
    }

    private static MailMessageSummary Message(AccountProfile account, uint uid) => new()
    {
        Key = new(account.Id, "INBOX", 7, uid), Sender = "sender@example.test", Subject = $"Subject {uid}",
        ReceivedAt = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero).AddMinutes(uid),
    };

    private static KeyboardKeyEvent SelectAll() =>
        new(new InputEventHeader(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName("A"), KeyboardKeyTransition.Down, KeyboardModifierState.Control, BVirtualKey.A, 0, 0, false, false,
            Source: InputEventSource.Synthetic);

    private static IEnumerable<UiElement> Descendants(UiElement element) => element.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private sealed class CountingPreviewHost : IHtmlPreviewHost
    {
        public int Closes { get; private set; }
        public Task<string> ShowAsync(MailMessageBody message) => Task.FromResult("Shown.");
        public void Close() => Closes++;
        public void Dispose() { }
    }

    private sealed class Host(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
