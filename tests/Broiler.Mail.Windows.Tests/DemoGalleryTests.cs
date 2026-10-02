using System;
using System.Linq;
using System.Threading;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Forms;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

public sealed class DemoGalleryTests
{
    [Theory]
    [InlineData("--demo", "inbox", true, AppTheme.System, 1100, 720)]
    [InlineData("--demo inbox", "inbox", false, AppTheme.System, 1100, 720)]
    [InlineData("--demo large-inbox", "large-inbox", false, AppTheme.System, 1100, 720)]
    [InlineData("--demo send-unknown --theme dark --size 640x480", "send-unknown", false, AppTheme.Dark, 640, 480)]
    [InlineData("--demo --size 1920x1080 --theme light", "inbox", true, AppTheme.Light, 1920, 1080)]
    public void Options_Parse_Scenario_Theme_And_Size(string args, string scenario, bool interactive, AppTheme theme, int width, int height)
    {
        Assert.True(DemoOptions.TryParse(args.Split(' '), out var options));
        Assert.Equal(scenario, options!.Name);
        Assert.Equal((interactive, theme, width, height), (options.Interactive, options.Theme, options.Width, options.Height));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--demo unknown")]
    [InlineData("--demo --theme blue")]
    [InlineData("--demo --theme dark --theme light")]
    [InlineData("--demo --size 639x480")]
    [InlineData("--demo --size 800")]
    [InlineData("--demo --size +800x600")]
    [InlineData("--demo --theme")]
    [InlineData("--smoke-test")]
    public void Options_Reject_Invalid_Arguments(string args) =>
        Assert.False(DemoOptions.TryParse(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), out _));

    [Fact]
    public void Every_Gallery_Scenario_Has_One_Unique_Name()
    {
        Assert.Equal(Enum.GetValues<DemoScenario>().Order(), DemoOptions.Gallery.Select(item => item.Scenario).Order());
        Assert.Equal(DemoOptions.Gallery.Count, DemoOptions.Gallery.Select(item => item.Name).Distinct().Count());
    }

    [Fact]
    public void Gallery_Dates_Use_A_Fixed_Clock_And_Culture()
    {
        var dates = DemoApplication.CreateDateFormatter();
        var newest = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal("10:00 AM", dates.List(newest));
        Assert.Equal("September 27", dates.List(newest.AddDays(-1)));
        Assert.Equal("9/28/2025", dates.List(newest.AddYears(-1)));
        Assert.Equal("9/28/2026 10:00 AM", dates.Detail(newest));
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("long-message")]
    [InlineData("html-only")]
    public void Reading_Scenarios_Select_Their_Message(string name)
    {
        var scenario = DemoOptions.Gallery.Single(item => item.Name == name).Scenario;
        Run(scenario, model =>
        {
            Assert.Equal(InboxViewModel.PageSize, model.Inbox.Messages.Count);
            Assert.Equal(scenario == DemoScenario.HtmlOnly ? 54u : 55u, model.Inbox.SelectedMessage?.Key.Uid);
            Assert.Equal(model.Inbox.SelectedMessage!.Key, model.Inbox.Body?.Key);
            Assert.Equal(scenario == DemoScenario.HtmlOnly, model.Inbox.Body!.IsHtmlFallback);
            if (scenario == DemoScenario.LongMessage) Assert.True(model.Inbox.SelectedMessage.Subject.Length > 100);
        });
    }

    [Fact]
    public void Empty_Scenario_Reports_An_Empty_Inbox()
    {
        Run(DemoScenario.Empty, model =>
        {
            Assert.Empty(model.Inbox.Messages);
            Assert.Equal("The inbox is empty.", model.Inbox.Status);
        });
    }

    [Fact]
    public void Large_Inbox_Loads_The_Session_Limit_Without_Duplicates()
    {
        Run(DemoScenario.LargeInbox, model =>
        {
            Assert.Equal(InboxViewModel.MaximumLoadedMessages, model.Inbox.Messages.Count);
            Assert.Equal(model.Inbox.Messages.Count, model.Inbox.Messages.Select(message => message.Key).Distinct().Count());
            Assert.False(model.Inbox.CanLoadOlder);
            Assert.Equal(model.Inbox.Messages[0].Key, model.Inbox.Body?.Key);
        });
    }

    [Fact]
    public void Receive_Error_Keeps_The_Previously_Loaded_Inbox()
    {
        Run(DemoScenario.ReceiveError, model =>
        {
            Assert.Equal(InboxViewModel.PageSize, model.Inbox.Messages.Count);
            Assert.Equal(55u, model.Inbox.SelectedMessage?.Key.Uid);
            Assert.NotNull(model.Inbox.Body);
            Assert.Contains("did not respond", model.Inbox.Status);
            Assert.Contains("previously loaded inbox is still shown", model.Inbox.Status);
        });
    }

    [Fact]
    public void Body_Error_Keeps_The_Message_Selected_With_A_Retry()
    {
        Run(DemoScenario.BodyError, model =>
        {
            Assert.Equal(55u, model.Inbox.SelectedMessage?.Key.Uid);
            Assert.Null(model.Inbox.Body);
            Assert.Equal(InboxProblemScope.Message, model.Inbox.ProblemScope);
            Assert.Contains("closed the connection", model.Inbox.Problem);
            Assert.True(model.Inbox.CanRetry);
        });
    }

    [Fact]
    public void Save_Error_Shows_Settings_Feedback()
    {
        Run(DemoScenario.SaveError, model =>
        {
            Assert.Equal(FeedbackKind.Error, model.Settings.StatusKind);
            Assert.Contains("read-only", model.Settings.Status);
        });
    }

    [Fact]
    public void Draft_Scenarios_Recover_Their_Drafts()
    {
        Run(DemoScenario.LargeDraft, model =>
        {
            var large = model.Composer;
            Assert.True(large.HasDraft);
            Assert.Equal(DraftSubmissionState.Editing, large.SubmissionState);
            Assert.True(large.CanEdit);
            Assert.NotEmpty(large.Cc);
            Assert.NotEmpty(large.Bcc);
            Assert.True(large.PlainText.Length > 5000);
        });
        Run(DemoScenario.SendUnknown, model =>
        {
            Assert.Equal(DraftSubmissionState.Unknown, model.Composer.SubmissionState);
            Assert.False(model.Composer.CanEdit);
            Assert.False(model.Composer.CanSend);
        });
    }

    // Mirrors WindowsMailWindow: a queued dispatcher drained on the owning thread, then a rendered frame.
    // Verification runs before the shell is disposed, because disposal also disables the view models.
    private static void Run(DemoScenario scenario, Action<MailShellViewModel> verify)
    {
        var options = new DemoOptions(scenario, AppTheme.Light, 640, 480);
        var application = DemoApplication.Create(options);
        application.InitializeAsync().GetAwaiter().GetResult();
        using var woken = new SemaphoreSlim(0);
        var dispatcher = new StandardQueuedUiDispatcher(() => woken.Release());
        var model = application.CreateViewModel(dispatcher);
        using var shell = new MailShellView(model, null, DemoApplication.CreateDateFormatter());
        var driver = DemoScenarioDriver.Start(options, model, shell, dispatcher);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!driver.Completion.IsCompleted)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Scenario {scenario} did not settle.");
            woken.Wait(TimeSpan.FromMilliseconds(100));
            dispatcher.Drain();
        }
        driver.Completion.GetAwaiter().GetResult();
        dispatcher.Drain();
        Assert.Equal(options.InitialTab, shell.Navigation.SelectedTab?.Id);

        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new HeadlessHost(options.Width, options.Height));
        session.AddRoot(shell.Window);
        Assert.NotNull(session.RenderFrame());
        verify(model);
    }

    private sealed class HeadlessHost(int width, int height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
