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

    [Theory]
    [InlineData("--demo --measure scroll")]
    [InlineData("--demo inbox --report out.json")]
    [InlineData("--demo inbox --measure fly")]
    [InlineData("--demo inbox --measure idle --measure type")]
    public void Measurement_Needs_A_Fixture_A_Known_Workload_And_A_Measure_For_A_Report(string args) =>
        Assert.False(DemoOptions.TryParse(args.Split(' '), out _));

    [Fact]
    public void Measurement_Options_Parse_Every_Workload()
    {
        foreach (var (name, workload, _) in DemoOptions.Workloads)
        {
            Assert.True(DemoOptions.TryParse(["--demo", "large-inbox", "--measure", name, "--report", "out.json"], out var options));
            Assert.Equal(workload, options!.Measure);
            Assert.True(System.IO.Path.IsPathFullyQualified(options.Report!));
        }
        Assert.Equal(Enum.GetValues<Measurement.MeasureWorkload>().Order(), DemoOptions.Workloads.Select(item => item.Workload).Order());
    }

    [Fact]
    public void Percentiles_Use_The_Nearest_Rank()
    {
        double[] values = [5, 1, 4, 2, 3, 10, 9, 8, 7, 6];
        Assert.Equal(5, Measurement.FrameSamples.Percentile(values, 50));
        Assert.Equal(10, Measurement.FrameSamples.Percentile(values, 95));
        Assert.Equal(1, Measurement.FrameSamples.Percentile(values, 1));
        Assert.True(double.IsNaN(Measurement.FrameSamples.Percentile([], 50)));
    }

    [Theory]
    [InlineData("--demo inbox --text-scale 150", 150)]
    [InlineData("--demo inbox", null)]
    public void Text_Scale_Option_Fixes_The_System_Text_Size(string arguments, int? percent)
    {
        Assert.True(DemoOptions.TryParse(arguments.Split(' '), out var options));
        Assert.Equal(percent, options!.TextScalePercent);
        foreach (var invalid in new[] { "99", "226", "1.5", "big" })
            Assert.False(DemoOptions.TryParse(["--demo", "inbox", "--text-scale", invalid], out _));
        Assert.True(DemoOptions.TryParse(["--demo", "inbox", "--contrast", "high"], out var contrast));
        Assert.True(contrast!.HighContrast);
        Assert.False(DemoOptions.TryParse(["--demo", "inbox", "--contrast", "low"], out _));
    }

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
        Assert.Equal("Sep 27", dates.List(newest.AddDays(-1)));
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

    [Fact]
    public void Invalid_Setup_Marks_The_Field_And_Keeps_The_Saved_Profile()
    {
        Run(DemoScenario.InvalidSetup, model =>
        {
            var account = model.Account;
            Assert.Equal(FeedbackKind.Error, account.StatusKind);
            Assert.Equal("EmailAddress", account.ValidationField);
            Assert.Equal("Enter an email address without a display name.", account.ValidationMessage);
            Assert.Equal("reader@example.test", account.Profile?.EmailAddress);
            Assert.True(account.CanSave);
        });
    }

    [Fact]
    public void Canceled_Test_Is_Information_And_Can_Be_Repeated()
    {
        Run(DemoScenario.TestCanceled, model =>
        {
            var account = model.Account;
            Assert.Equal("Connection test canceled.", account.Status);
            Assert.Equal(FeedbackKind.Information, account.StatusKind);
            Assert.Equal(ConnectionCheck.NotRun, account.ConnectionCheck);
            Assert.False(account.IsBusy);
            Assert.True(account.CanManagePassword);
        });
    }

    [Fact]
    public void Draft_Conflict_Keeps_The_Edits_Open_And_Explains_The_Other_Instance()
    {
        Run(DemoScenario.DraftConflict, model =>
        {
            var composer = model.Composer;
            Assert.Equal(FeedbackKind.Error, composer.StorageKind);
            Assert.Contains("Another app instance changed the saved draft", composer.StorageStatus);
            Assert.EndsWith("One more line typed after another window saved this draft.", composer.PlainText);
            Assert.True(composer.CanEdit);
        });
    }

    [Fact]
    public void Rejected_Send_Keeps_The_Draft_With_The_Server_Reason()
    {
        Run(DemoScenario.SendRejected, model =>
        {
            var composer = model.Composer;
            Assert.Equal(DraftSubmissionState.Failed, composer.SubmissionState);
            Assert.Equal(FeedbackKind.Error, composer.StatusKind);
            Assert.Contains("550 5.1.1", composer.Status);
            Assert.True(composer.CanEdit);
            Assert.True(composer.CanSend);
        });
    }

    [Fact]
    public void Failed_Sent_Copy_Is_Recovered_Without_Offering_To_Send_Again()
    {
        Run(DemoScenario.SentCopyFailed, model =>
        {
            var composer = model.Composer;
            Assert.Equal(DraftSubmissionState.Accepted, composer.SubmissionState);
            Assert.Equal(SentCopyState.Failed, composer.SentCopy);
            Assert.Contains("do not resend", composer.SentCopyText);
            Assert.False(composer.CanSend);
            Assert.False(composer.CanEdit);
        });
    }

    [Fact]
    public void Long_Html_Selects_A_Document_Past_The_Preview_Budget()
    {
        Run(DemoScenario.LongHtml, model =>
        {
            Assert.Equal(54u, model.Inbox.SelectedMessage?.Key.Uid);
            Assert.Equal("Long HTML newsletter", model.Inbox.SelectedMessage!.Subject);
            var body = model.Inbox.Body!;
            Assert.True(body.IsHtmlFallback);
            Assert.Contains("Read section 400", body.HtmlText);
            Assert.Equal("Short HTML note", model.Inbox.Messages.Single(message => message.Key.Uid == 53).Subject);
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
