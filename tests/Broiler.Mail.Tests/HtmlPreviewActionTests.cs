using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-11: the reader's HTML preview action follows its own message's preview window.</summary>
[Collection("UI theme")]
public sealed class HtmlPreviewActionTests
{
    private static readonly MailMessageKey First = new(new(Guid.Parse("11111111-1111-1111-1111-111111111111")), "INBOX", 7, 1);
    private static readonly MailMessageKey Second = new(First.AccountId, "INBOX", 7, 2);

    [Fact]
    public async Task OpeningFollowsTheWindowAndCloseUsesTheSameButton()
    {
        using var fixture = Fixture.Create(First);

        fixture.Button.Click();
        Assert.False(fixture.Button.IsEnabled);
        Assert.Equal(HtmlMessagePreview.OpeningText, fixture.Status.Text);

        fixture.Host.Raise(First, HtmlPreviewPhase.Open, "Open in its own window.");
        fixture.Host.Next.SetResult("Open in its own window.");
        await fixture.SettleAsync();
        Assert.Equal(HtmlMessagePreview.CloseText, fixture.Button.Text);
        Assert.True(fixture.Button.IsEnabled);
        Assert.Equal("Open in its own window.", fixture.Status.Text);

        fixture.Button.Click();
        Assert.Equal(1, fixture.Host.Closes);
        fixture.Host.Raise(First, HtmlPreviewPhase.Closed, "Closed; the text remains here.");
        await fixture.SettleAsync();
        Assert.Equal(HtmlMessagePreview.OpenText, fixture.Button.Text);
        Assert.True(fixture.Button.IsEnabled);
        Assert.Equal("Closed; the text remains here.", fixture.Status.Text);
    }

    [Fact]
    public async Task AnOpeningResultArrivingAfterThePhaseDoesNotOverwriteIt()
    {
        using var fixture = Fixture.Create(First);
        fixture.Button.Click();

        fixture.Host.Raise(First, HtmlPreviewPhase.Open, "Open.");
        await fixture.SettleAsync();
        fixture.Host.Next.SetResult("Stale opening result.");
        await fixture.SettleAsync();

        Assert.Equal("Open.", fixture.Status.Text);
        Assert.Equal(HtmlMessagePreview.CloseText, fixture.Button.Text);
    }

    [Fact]
    public async Task AFailureBeforeAnyWindowIsShownAsStatus()
    {
        using var fixture = Fixture.Create(First);
        fixture.Button.Click();

        fixture.Host.Next.SetResult("HTML exceeds the preview limits.");
        await fixture.SettleAsync();

        Assert.Equal("HTML exceeds the preview limits.", fixture.Status.Text);
        Assert.Equal(HtmlMessagePreview.OpenText, fixture.Button.Text);
        Assert.True(fixture.Button.IsEnabled);
    }

    [Fact]
    public async Task ChangesForAnotherMessageAreIgnored()
    {
        using var fixture = Fixture.Create(First);
        string before = fixture.Status.Text;

        fixture.Host.Raise(Second, HtmlPreviewPhase.Open, "Another message's preview.");
        fixture.Host.Raise(Second, HtmlPreviewPhase.Unavailable, "Another failure.");
        await fixture.SettleAsync();

        Assert.Equal(before, fixture.Status.Text);
        Assert.Equal(HtmlMessagePreview.OpenText, fixture.Button.Text);
    }

    [Fact]
    public void AReaderRebuiltWhileItsPreviewIsOpenOffersClose()
    {
        using var fixture = Fixture.Create(First, current: First);

        Assert.Equal(HtmlMessagePreview.CloseText, fixture.Button.Text);
        Assert.True(fixture.Button.IsEnabled);
    }

    [Fact]
    public void DisposingTheReaderStopsListening()
    {
        var fixture = Fixture.Create(First);
        Assert.Equal(1, fixture.Host.Subscribers);

        fixture.Dispose();

        Assert.Equal(0, fixture.Host.Subscribers);
    }

    [Fact]
    public void MessagesWithoutHtmlExplainWhyAndCannotOpen()
    {
        using var fixture = Fixture.Create(First, html: null, unavailable: "HTML was too large to keep.");

        Assert.False(fixture.Button.IsEnabled);
        Assert.Equal("HTML was too large to keep.", fixture.Status.Text);
        // A Closed change for this message never enables an action that has nothing to open.
        fixture.Host.Raise(First, HtmlPreviewPhase.Closed, "Closed.");
        fixture.Dispatcher.Drain();
        Assert.False(fixture.Button.IsEnabled);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(FakePreviewHost host, TestQueueDispatcher dispatcher, UiSession session, UiElement content)
        {
            Host = host; Dispatcher = dispatcher; Session = session; Content = content;
        }

        public FakePreviewHost Host { get; }
        public TestQueueDispatcher Dispatcher { get; }
        public UiSession Session { get; }
        public UiElement Content { get; }
        public StandardButton Button => Descendants(Content).OfType<StandardButton>().Single();
        public StandardLabel Status => Descendants(Content).OfType<StandardLabel>().Single();

        public static Fixture Create(MailMessageKey key, MailMessageKey? current = null, string? html = "<p>Hello</p>", string? unavailable = null)
        {
            var host = new FakePreviewHost { Current = current };
            var dispatcher = new TestQueueDispatcher();
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());
            var content = new HtmlMessagePreview(host).CreateContent(new MailMessageBody(key, "Hello", html) { HtmlUnavailableReason = unavailable });
            session.AddRoot(content);
            session.RenderFrame();
            return new(host, dispatcher, session, content);
        }

        /// <summary>Lets the click handler's continuation run, then applies what it posted.</summary>
        public async Task SettleAsync()
        {
            for (int i = 0; i < 5; i++)
            {
                await Task.Yield();
                Dispatcher.Drain();
            }
        }

        public void Dispose()
        {
            Session.Dispose();
            Content.Dispose();
        }
    }

    private sealed class FakePreviewHost : IHtmlPreviewHost
    {
        public event EventHandler<HtmlPreviewChange>? Changed;
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public MailMessageKey? Current { get; set; }
        public TaskCompletionSource<string> Next { get; } = new();
        public int Closes { get; private set; }
        public Task<string> ShowAsync(MailMessageBody message) => Next.Task;
        public void Close() => Closes++;
        public void Raise(MailMessageKey key, HtmlPreviewPhase phase, string text) => Changed?.Invoke(this, new(key, phase, text));
        public void Dispose() { }
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(600, 200);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
