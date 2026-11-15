using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>
/// An async UI handler resumes on its session's dispatcher. A view model's task completes on a pool thread once
/// it has posted its commit; resuming there let a handler change the element tree while the UI thread laid it
/// out ("Collection was modified" from FocusNavigation.Reveal), and before the commit it awaited had run.
/// </summary>
public sealed class UiAsyncTests
{
    [Fact]
    public async Task AHandlerResumesOnTheDispatcherNotOnThePoolThreadThatCompletedItsWork()
    {
        var (dispatcher, button, session) = Attached();
        using var _ = session;
        var work = new TaskCompletionSource();
        int? resumedOn = null;
        using var resumed = new ManualResetEventSlim();
        button.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            await work.Task;
            resumedOn = Environment.CurrentManagedThreadId;
            resumed.Set();
        });

        button.Click();
        await Task.Run(work.SetResult);

        Assert.False(resumed.Wait(TimeSpan.FromMilliseconds(200)), "The handler resumed before the UI thread drained its dispatcher.");
        int uiThread = Environment.CurrentManagedThreadId;
        dispatcher.Drain();
        Assert.Equal(uiThread, resumedOn);
    }

    [Fact]
    public async Task AHandlerResumesAfterTheCommitItsAwaitedWorkPosted()
    {
        var (dispatcher, button, session) = Attached();
        using var _ = session;
        var order = new List<string>();
        var work = new TaskCompletionSource();
        button.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            await work.Task;
            order.Add("handler");
        });

        button.Click();
        // As InboxViewModel.RunAsync does: post the commit, then complete the awaited task on the pool thread.
        await Task.Run(() => { dispatcher.Post(() => order.Add("commit")); work.SetResult(); });
        dispatcher.Drain();

        Assert.Equal(["commit", "handler"], order);
    }

    [Fact]
    public async Task AnExceptionAfterAnAwaitSurfacesWhereTheDispatcherRunsNotOnThePool()
    {
        var (dispatcher, button, session) = Attached();
        using var _ = session;
        var work = new TaskCompletionSource();
        button.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            await work.Task;
            throw new InvalidOperationException("handler failed");
        });

        button.Click();
        await Task.Run(work.SetResult);

        var error = Assert.Throws<InvalidOperationException>(dispatcher.Drain);
        Assert.Equal("handler failed", error.Message);
    }

    [Fact]
    public void AHandlerOnADetachedElementRunsAsBefore()
    {
        var button = new StandardButton { Text = "Detached" };
        bool ran = false;
        button.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) =>
        {
            await Task.CompletedTask;
            ran = true;
        });
        var caller = SynchronizationContext.Current;

        button.Click();

        Assert.True(ran);
        Assert.Same(caller, SynchronizationContext.Current);
    }

    [Fact]
    public void TheCallersContextIsRestoredOnceTheHandlerAwaits()
    {
        var (_, button, session) = Attached();
        using var _ = session;
        var work = new TaskCompletionSource();
        button.Clicked += UiAsync.Handler<UiButtonClickEventArgs>(async (_, _) => await work.Task);
        var caller = SynchronizationContext.Current;

        button.Click();

        Assert.Same(caller, SynchronizationContext.Current);
        work.SetResult();
    }

    private static (TestQueueDispatcher Dispatcher, StandardButton Button, UiSession Session) Attached()
    {
        var dispatcher = new TestQueueDispatcher();
        var button = new StandardButton { Text = "Run" };
        var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());
        session.AddRoot(button);
        return (dispatcher, button, session);
    }

    private sealed class Host : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(320, 240);
        public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(Broiler.Graphics.RenderList.BRenderList renderList) { }
    }
}
