using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-08: inbox states are explained beside the affected pane, with the action that resolves them.</summary>
[Collection("UI theme")]
public sealed class InboxStateTests
{
    [Fact]
    public async Task ListExplainsWhyItIsEmptyBeforeAndAfterTheFirstReceive()
    {
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([], null)));
        Assert.False(fixture.Model.HasLoaded);
        Assert.Equal("Receive mail to load your inbox.", fixture.ListFeedback.Message);
        await fixture.ReceiveAsync();
        Assert.True(fixture.Model.HasLoaded);
        Assert.Equal("The inbox is empty.", fixture.ListFeedback.Message);
        Assert.Equal(FeedbackKind.Information, fixture.ListFeedback.Kind);
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
    }

    [Fact]
    public async Task FailedRefreshKeepsMessagesAndOffersRetryBesideTheList()
    {
        var account = TestDirectory.Profile();
        var messages = new[] { Message(account, 2), Message(account, 1) };
        bool fail = false;
        using var fixture = new Fixture(_ => fail ? throw new MailConnectionException("The server did not respond.") : Task.FromResult(new MailInboxPage(messages, null)), account);
        await fixture.ReceiveAsync();
        fail = true;
        await fixture.ReceiveAsync();

        Assert.Equal(2, fixture.Model.Messages.Count);
        Assert.Equal(InboxProblemScope.List, fixture.Model.ProblemScope);
        Assert.Equal(FeedbackKind.Error, fixture.ListFeedback.Kind);
        Assert.Contains("The server did not respond.", fixture.ListFeedback.Message);
        Assert.Contains("from the last successful receive", fixture.ListFeedback.Message);
        var retry = fixture.Button("Retry receiving");
        Assert.True(retry.IsEnabled && fixture.IsShown(retry));

        fail = false;
        fixture.Session.SetFocus(retry);
        retry.Click();
        fixture.Settle();
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
        Assert.False(fixture.IsShown(retry));
        // Focus leaves the hidden Retry for the list it concerned.
        Assert.IsType<StandardListView>(fixture.Session.FocusedElement);
    }

    [Fact]
    public async Task FailedMessageOffersRetryBesideItsHeaderInsteadOfAnInstruction()
    {
        var account = TestDirectory.Profile();
        var message = Message(account, 1);
        bool fail = true;
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([message], null)), account,
            key => fail ? throw new MailConnectionException("The connection closed.") : Task.FromResult(new MailMessageBody(key, "Loaded body")));
        await fixture.ReceiveAsync();
        await fixture.Model.SelectAsync(message.Key);
        fixture.Settle();

        Assert.Equal(InboxProblemScope.Message, fixture.Model.ProblemScope);
        Assert.Equal("The connection closed.", fixture.MessageFeedback.Message);
        Assert.Equal(FeedbackKind.Error, fixture.MessageFeedback.Kind);
        Assert.Equal("", fixture.Reader.Text);
        Assert.Equal("", fixture.ListFeedback.Message);
        var retry = fixture.Button("Retry loading");
        Assert.True(fixture.IsShown(retry));

        fail = false;
        retry.Click();
        fixture.Settle();
        Assert.Contains("Loaded body", fixture.Reader.Text);
        Assert.Equal("", fixture.MessageFeedback.Message);
        Assert.False(fixture.IsShown(retry));
    }

    [Fact]
    public async Task CancelIsInformationAndMovesFocusOffTheDisabledCancelButton()
    {
        var pending = new TaskCompletionSource<MailInboxPage>();
        using var fixture = new Fixture(token => pending.Task.WaitAsync(token));
        _ = fixture.Model.ReceiveAsync();
        fixture.Render();
        Assert.True(fixture.Model.IsLoadingList);
        Assert.Equal(FeedbackKind.Progress, fixture.ListFeedback.Kind);
        var cancel = fixture.Button("Cancel");
        fixture.Session.SetFocus(cancel);

        cancel.Click();
        fixture.Render();
        Assert.True(fixture.Model.ProblemIsCancellation);
        Assert.Equal(FeedbackKind.Information, fixture.ListFeedback.Kind);
        Assert.Equal("Receiving was canceled.", fixture.ListFeedback.Message);
        Assert.Equal("Receive mail", Assert.IsType<StandardButton>(fixture.Session.FocusedElement).Text);
    }

    [Fact]
    public async Task ChoosingAnotherMessageReplacesAnEarlierMessageProblem()
    {
        var account = TestDirectory.Profile();
        var first = Message(account, 2);
        var second = Message(account, 1);
        using var fixture = new Fixture(_ => Task.FromResult(new MailInboxPage([first, second], null)), account,
            key => key.Uid == 2 ? throw new MailConnectionException("Broken message.") : Task.FromResult(new MailMessageBody(key, "Fine")));
        await fixture.ReceiveAsync();
        await fixture.Model.SelectAsync(first.Key);
        fixture.Settle();
        Assert.Equal(InboxProblemScope.Message, fixture.Model.ProblemScope);

        await fixture.Model.SelectAsync(second.Key);
        fixture.Settle();
        Assert.Equal(InboxProblemScope.None, fixture.Model.ProblemScope);
        Assert.Equal("", fixture.MessageFeedback.Message);
    }

    private static MailMessageSummary Message(AccountProfile account, uint uid) => new()
    { Key = new(account.Id, "INBOX", 7, uid), Sender = "sender@example.test", Subject = $"Subject {uid}" };

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UiElement _content;

        public Fixture(Func<CancellationToken, Task<MailInboxPage>> inbox, AccountProfile? account = null, Func<MailMessageKey, Task<MailMessageBody>>? body = null)
        {
            Dispatcher = new TestQueueDispatcher();
            var receiver = new TestMailReceiver { Inbox = (_, token) => inbox(token) };
            if (body is not null) receiver.Body = (key, _) => body(key);
            Model = new InboxViewModel(receiver, Dispatcher);
            Model.SetAccount(account ?? TestDirectory.Profile());
            _content = new InboxView(Model).CreateContent();
            Session = new StandardUiSessionBuilder().WithDispatcher(Dispatcher).Build(new Host());
            Session.AddRoot(_content);
            Render();
        }

        public TestQueueDispatcher Dispatcher { get; }
        public InboxViewModel Model { get; }
        public UiSession Session { get; }
        public InlineFeedback ListFeedback => Descendants(_content).OfType<InlineFeedback>().First();
        public InlineFeedback MessageFeedback => Descendants(_content).OfType<InlineFeedback>().Skip(1).First();
        public ScrollableMessageText Reader => Descendants(_content).OfType<ScrollableMessageText>().Single();
        public StandardButton Button(string text) => Descendants(_content).OfType<StandardButton>().Single(button => button.Text == text);

        public bool IsShown(UiElement element)
        {
            for (var current = element; current is not null; current = current.Parent)
                if (current.Visibility != UiVisibility.Visible) return false;
            return true;
        }

        public async Task ReceiveAsync()
        {
            await Model.ReceiveAsync();
            Settle();
        }

        public void Settle()
        {
            Dispatcher.DrainUntil(() => !Model.IsBusy);
            Render();
        }

        public void Render()
        {
            Dispatcher.Drain();
            Session.RenderFrame();
        }

        public void Dispose()
        {
            Session.Dispose();
            _content.Dispose();
            Model.Dispose();
        }
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(1000, 700);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
