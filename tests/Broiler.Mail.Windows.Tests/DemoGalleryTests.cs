using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Graphics.Windows;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Forms;
using Broiler.UI.Forms.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
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
            string fixture = DemoOptions.MeasuresPreview(workload) ? "long-html" : "large-inbox";
            Assert.True(DemoOptions.TryParse(["--demo", fixture, "--measure", name, "--report", "out.json"], out var options));
            Assert.Equal(workload, options!.Measure);
            Assert.True(System.IO.Path.IsPathFullyQualified(options.Report!));
            Assert.False(options.Detail);
        }
        Assert.Equal(Enum.GetValues<Measurement.MeasureWorkload>().Order(), DemoOptions.Workloads.Select(item => item.Workload).Order());
    }

    [Theory]
    [InlineData("--demo long-html --measure long-html", true)]
    [InlineData("--demo long-html --measure preview-zoom --scale 200", true)]
    [InlineData("--demo html-only --measure long-html", false)]
    [InlineData("--demo inbox --measure preview-zoom", false)]
    public void Preview_Workloads_Need_The_Long_Html_Fixture(string args, bool valid) =>
        Assert.Equal(valid, DemoOptions.TryParse(args.Split(' '), out _));

    [Theory]
    [InlineData("--demo inbox --measure scroll --detail", true)]
    [InlineData("--demo inbox --measure scroll --detail --report out.json", true)]
    [InlineData("--demo inbox --detail --measure resize --scale 150", true)]
    [InlineData("--demo inbox --measure scroll", false)]
    public void Detail_Is_A_Switch_For_A_Measurement(string args, bool detail)
    {
        Assert.True(DemoOptions.TryParse(args.Split(' '), out var options));
        Assert.Equal(detail, options!.Detail);
    }

    [Theory]
    [InlineData("--demo inbox --detail")]
    [InlineData("--demo --measure scroll --detail")]
    [InlineData("--demo inbox --measure scroll --detail --detail")]
    [InlineData("--demo inbox --measure scroll --detail on")]
    public void Detail_Needs_A_Measurement_And_Takes_No_Value(string args) =>
        Assert.False(DemoOptions.TryParse(args.Split(' '), out _));

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
    public void Large_Inbox_Loads_The_Session_Limit_Without_Duplicates_And_Explains_It()
    {
        Run(DemoScenario.LargeInbox, (model, shell) =>
        {
            Assert.Equal(InboxViewModel.MaximumLoadedMessages, model.Inbox.Messages.Count);
            Assert.Equal(model.Inbox.Messages.Count, model.Inbox.Messages.Select(message => message.Key).Distinct().Count());
            Assert.False(model.Inbox.CanLoadOlder);
            Assert.Equal(model.Inbox.Messages[0].Key, model.Inbox.Body?.Key);
            // Older mail is left on the server, so the notice above the list says why Load older is
            // unavailable, although the newest message was read since.
            Assert.False(Button(shell, "inbox", "Load older").IsEnabled);
            var notice = Descendants(Tab(shell, "inbox")).OfType<InlineFeedback>().First();
            Assert.Equal((FeedbackKind.Information, "Session limit reached (500 messages): older messages cannot be loaded now. Receive mail to start again from the newest page."),
                (notice.Kind, notice.Message));
            Assert.True(IsAvailable(Button(shell, "inbox", "Receive mail")));
            // The newest row is unread and every third row from it is read, as in the captures of the
            // 500-message mailbox.
            Assert.Equal(Enumerable.Range(0, InboxViewModel.MaximumLoadedMessages).Select(row => row % 3 == 2), model.Inbox.Messages.Select(message => message.IsRead));
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

    /// <summary>
    /// Beside the list, the footer points to the explanation and Retry loading below the message's header,
    /// where the reader shows them, as the compact reader does.
    /// </summary>
    [Fact]
    public void Body_Error_Footer_Points_Below_The_Message_Header()
    {
        Run(DemoScenario.BodyError, 1100, 720, null, (_, shell, render) =>
        {
            render();
            Assert.Equal("The message could not be loaded. Details and Retry are below the message header.", Footer(shell));
            var date = Descendants(Tab(shell, "inbox")).OfType<StandardLabel>().Single(label => label.Text.StartsWith("Received ", StringComparison.Ordinal));
            var retry = Button(shell, "inbox", "Retry loading");
            Assert.True(IsAvailable(retry));
            Assert.True(retry.Bounds.Top >= date.Bounds.Bottom - 0.5, $"Retry loading is at {retry.Bounds}, the date at {date.Bounds}.");
        });
    }

    /// <summary>
    /// The reader header of the gallery's reading fixtures, with their own subject, sender, recipient and
    /// date, as Accept-UI -OpenReader shows them: in the compact reader at 640x480, and at 1100x720 with
    /// twice the text size. Each row is shown whole, scrolled below the header whole, or cut between two
    /// lines of its text, and no line of the date starts with its separator. With no message text
    /// (body-error), a header that scrolls takes all the reader that the line below it leaves. Beside the
    /// list, Reply, Reply all and Forward are on screen whole below the header, whatever it shows, where
    /// they fit on one row with the subject's first line and six lines of text; just wider than the compact
    /// reader at twice the text size, where they wrapped and left the text less than a line, they end the
    /// header, as in the compact reader. Their row is never flush with the line that separates the header
    /// from the message text, as it was in html-only's scrolling compact header. The text keeps at least
    /// the given number of lines: the compact reader at 640x480 with twice the text size keeps two, after
    /// Back to inbox and its inset. So does the large inbox at the session limit, whose footer points to
    /// the explanation above the hidden list instead of adding it to the reading status.
    /// </summary>
    [Theory]
    [InlineData("inbox", 640, 480, 1.0, false, true, 5)]
    [InlineData("long-message", 640, 480, 1.0, false, false, 8)]
    [InlineData("html-only", 640, 480, 1.0, false, true, 6)]
    [InlineData("long-html", 640, 480, 1.0, false, true, 6)]
    [InlineData("body-error", 640, 480, 1.0, false, true, 0)]
    [InlineData("large-inbox", 640, 480, 1.0, false, true, 5)]
    [InlineData("inbox", 640, 480, 2.0, false, false, 2)]
    [InlineData("large-inbox", 640, 480, 2.0, false, false, 2)]
    [InlineData("inbox", 700, 480, 2.0, false, false, 2)]
    [InlineData("long-message", 700, 520, 2.0, false, false, 3)]
    [InlineData("inbox", 1100, 720, 2.0, true, true, 5)]
    [InlineData("long-message", 1100, 720, 2.0, true, true, 5)]
    [InlineData("html-only", 1100, 720, 2.0, true, true, 6)]
    public void Reader_Header_Ends_Between_Its_Rows(string name, int width, int height, double textScale, bool pinned, bool replyShown, int textLines)
    {
        var scenario = DemoOptions.Gallery.Single(item => item.Name == name).Scenario;
        // The app measures text with DirectWrite, which a renderer registers for the process. The headless
        // measurer's lines are shorter (60 instead of 64 DIP for a title at 200 %), so the header would end
        // elsewhere than in the app.
        using (new Direct2DRenderer()) { }
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            // The app's preview host, which the HTML preview's row needs; nothing is opened.
            Run(scenario, width, height, new NoPreviewHost(), (model, shell, render) =>
            {
                Assert.True(shell.Inbox.OpenSelected());
                render();
                var header = Descendants(shell.Window).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
                BRect shown = header.Scroll.ContentBounds;
                string where = $"{name} at {width}x{height}, text {textScale:P0}: the header shows {shown} of {header.AvailableHeight}";
                Assert.Equal(width < 680, Descendants(shell.Window).OfType<AdaptiveInboxLayout>().Single().ShowsReaderOnly);
                // Only the large inbox is at the session limit; its compact reader's footer points to the explanation.
                Assert.Equal(name == "large-inbox", model.Inbox.SessionLimitNotice is not null);
                if (model.Inbox.SessionLimitNotice is not null)
                    Assert.Equal($"Session limit reached. Use {InboxView.BackText} to see the details.", Footer(shell));
                if (model.Inbox.Body is null && header.Scroll.HasVerticalScrollbar)
                    Assert.True(Math.Abs(header.AvailableHeight - header.Bounds.Height) < 0.5, where);
                else
                    foreach (var row in header.Scroll.Children.Single().Children.Single().Children.Where(row => row.Visibility == UiVisibility.Visible && row.Bounds.Height > 0))
                    {
                        // The date line's separator ends a line, never starts one, where it would read as a
                        // bullet; the read state is one phrase, so a header cut between lines shows it whole.
                        if (row is StandardLabel date && date.Text.StartsWith("Received ", StringComparison.Ordinal))
                            Assert.DoesNotContain(WrappedLines(date), text => text.StartsWith('\u00B7'));
                        if (row.Bounds.Bottom <= shown.Bottom + 0.5 || row.Bounds.Top >= shown.Bottom - 0.5) continue;
                        double line = row switch
                        {
                            StandardLabel label => BTextMeasurer.GetLineHeight(label.Font),
                            StandardRichEdit edit => BTextMeasurer.GetLineHeight(edit.Font),
                            _ => 0,
                        };
                        double lines = (shown.Bottom - row.Bounds.Top) / line;
                        Assert.True(line > 0 && Math.Abs(lines - Math.Round(lines)) < 0.01, $"{where}: the {row.GetType().Name} at {row.Bounds} is cut.");
                    }
                var reader = Tab(shell, "inbox");
                var divider = Descendants(reader).OfType<Divider>().Single().Bounds;
                var reply = Descendants(reader).OfType<StandardButton>().Single(button => button.Text == "Reply");
                bool inHeader = reply.IsDescendantOf(header);
                Assert.True(pinned != inHeader, $"{where}: Reply is {(inHeader ? "in" : "below")} the header.");
                bool onScreen = inHeader ? reply.Bounds.Bottom <= shown.Bottom + 0.5
                    : reply.Bounds.Top >= header.Bounds.Bottom - 0.5 && reply.Bounds.Bottom <= divider.Top + 0.5;
                Assert.True(replyShown == onScreen, $"{where}: Reply is at {reply.Bounds}, the line at {divider}.");
                // Shown, their row keeps a gap of 4 DIP from the line, as below the header; the compact
                // header that scrolled left it flush with the line.
                if (replyShown)
                    Assert.True(divider.Top - reply.Parent!.Bounds.Bottom >= 3.5, $"{where}: Reply's row ends at {reply.Parent.Bounds.Bottom}, the line at {divider}.");
                Assert.True(divider.Top >= header.Bounds.Bottom - 0.5, $"{where}: the line is at {divider}.");
                var text = Descendants(reader).OfType<ScrollableMessageText>().Single();
                Assert.Equal(divider.Bottom, text.Bounds.Top, 0.5);
                // The lines are whole below the text's top margin; the gap below Reply's row, where the header
                // ends with it, may narrow the margin below them.
                Assert.True(text.Bounds.Height >= text.HeightOfLines(textLines) - (text.HeightOfLines(0) / 2) - 0.5, $"{where}: the text has {text.Bounds.Height}, less than {textLines} lines.");
                // Beside the list, the subject's first line is whole.
                if (width >= 680)
                {
                    var subject = Descendants(header).OfType<StandardLabel>().First(label => label.Visibility == UiVisibility.Visible && label.Bounds.Height > 0);
                    Assert.True(shown.Bottom >= subject.Bounds.Top + BTextMeasurer.GetLineHeight(subject.Font) - 0.5, $"{where}: the subject at {subject.Bounds} is cut.");
                }
            });
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>
    /// The inbox notice of the gallery's list problems, with their own explanations and DirectWrite's
    /// metrics, at the sizes of the 200 % captures. The explanation shows the given number of whole lines,
    /// all of them or cut between two, never inside one, and Retry is whole right below it, so the footer's
    /// pointer to it holds: in the 640x480 captures and for load-error at 1100x720, Retry was cut or out of view.
    /// </summary>
    [Theory]
    [InlineData("receive-error", 640, 480, 1)]
    [InlineData("receive-canceled", 640, 480, 1)]
    [InlineData("load-error", 640, 480, 1)]
    [InlineData("receive-error", 1100, 720, 6)]
    [InlineData("receive-canceled", 1100, 720, 2)]
    [InlineData("load-error", 1100, 720, 4)]
    public void Inbox_Notice_Ends_Between_Its_Lines_And_Keeps_Retry_In_View(string name, int width, int height, int lines)
    {
        var scenario = DemoOptions.Gallery.Single(item => item.Name == name).Scenario;
        using (new Direct2DRenderer()) { }
        StandardThemeTokens previous = StandardControlPaint.Theme;
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(2));
        try
        {
            Run(scenario, width, height, null, (model, shell, render) =>
            {
                // The app's window is compact before the fixture chooses a message, so it shows the list.
                shell.Inbox.GoBackToList();
                render();
                Assert.NotNull(model.Inbox.ListProblem);
                var notice = Descendants(shell.Window).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Inbox notice");
                BRect shown = notice.Scroll.ContentBounds;
                string where = $"{name} at {width}x{height}, text 200 %: the notice shows {shown} of {notice.AvailableHeight}";
                var label = Descendants(notice).OfType<StandardLabel>().Single();
                double line = BTextMeasurer.GetLineHeight(label.Font);
                double shownLines = (Math.Min(shown.Bottom, label.Bounds.Bottom) - label.Bounds.Top) / line;
                Assert.True(Math.Abs(shownLines - lines) < 0.01, $"{where}: the explanation at {label.Bounds} shows {shownLines} lines of {line}.");
                var retry = Descendants(Tab(shell, "inbox")).OfType<StandardButton>().Single(button => button.Text is "Retry receiving" or "Retry loading older");
                BRect row = retry.Parent!.Bounds;
                Assert.Equal(notice.Bounds.Bottom, row.Top, 0.5);
                Assert.True(row.Top <= retry.Bounds.Top && retry.Bounds.Bottom <= row.Bottom, $"{where}: Retry is at {retry.Bounds} in its row at {row}.");
                // No scroll view clips it: it is on screen, not scrolled out of the notice.
                for (var parent = retry.Parent; parent is not null; parent = parent.Parent)
                    if (parent is StandardScrollView scroll)
                        Assert.True(retry.Bounds.Top >= scroll.ContentBounds.Top - 0.5 && retry.Bounds.Bottom <= scroll.ContentBounds.Bottom + 0.5, $"{where}: Retry at {retry.Bounds} is out of {scroll.ContentBounds}.");
                Assert.EndsWith(model.Inbox.ProblemIsCancellation ? "Retry is available." : "Details and Retry are above the list.", Footer(shell), StringComparison.Ordinal);
            });
        }
        finally { StandardControlPaint.ApplyTheme(previous); }
    }

    /// <summary>The lines a wrapping label shows at its width: it breaks only at spaces.</summary>
    private static IEnumerable<string> WrappedLines(StandardLabel label)
    {
        string current = "";
        foreach (string word in label.Text.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && BTextMeasurer.MeasureAdvance(candidate, label.Font) > label.Bounds.Width)
            {
                yield return current;
                candidate = word;
            }
            current = candidate;
        }
        if (current.Length > 0) yield return current;
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
    public void Smtp_Test_Failure_Is_Beside_The_Outgoing_Step_And_Starts_No_Submission()
    {
        Run(DemoScenario.SmtpTestFailed, (model, shell) =>
        {
            var account = model.Account;
            Assert.Equal(ConnectionCheck.Failed, account.OutgoingCheck);
            Assert.Equal(MailConnectionFailure.AuthenticationRejected, account.OutgoingFailureKind);
            Assert.Equal(FeedbackKind.Error, account.StatusKind);
            Assert.Equal("SMTP sign-in test failed: The demo SMTP server rejected the sign-in. Check the SMTP username and password, then test again.", account.Status);
            Assert.Contains("Optional — " + account.Status, StepLines(shell));
            // Receiving keeps its own state, and the composer has no draft and no submission or Sent copy.
            Assert.Equal(ConnectionCheck.NotRun, account.ConnectionCheck);
            AssertNoSubmission(model);
            // Valid actions: test again; nothing to cancel.
            Assert.False(account.IsBusy);
            Assert.True(account.CanTestOutgoing);
            Assert.True(Button(shell, "account", "Test SMTP sign-in").IsEnabled);
            Assert.Equal(UiVisibility.Collapsed, Button(shell, "account", "Cancel test").Visibility);
        });
    }

    [Fact]
    public void Smtp_Test_Pass_Says_That_No_Message_Was_Sent()
    {
        Run(DemoScenario.SmtpTestPassed, (model, shell) =>
        {
            var account = model.Account;
            Assert.Equal(ConnectionCheck.Passed, account.OutgoingCheck);
            Assert.Equal(FeedbackKind.Success, account.StatusKind);
            Assert.Equal("The SMTP server accepted the sign-in over an encrypted connection. No message was sent.", account.Status);
            Assert.Contains("Done — Outgoing sign-in tested; no message was sent.", StepLines(shell));
            Assert.Equal(ConnectionCheck.NotRun, account.ConnectionCheck);
            AssertNoSubmission(model);
            Assert.True(Button(shell, "account", "Test SMTP sign-in").IsEnabled);
            Assert.Equal(UiVisibility.Collapsed, Button(shell, "account", "Cancel test").Visibility);
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

    [Fact]
    public async Task Smtp_Test_Fixtures_Mark_Their_Own_Smtp_Password_As_Saved_Without_Any_Secret()
    {
        foreach (var (scenario, saved) in new[] { (DemoScenario.SmtpTestFailed, true), (DemoScenario.SmtpTestPassed, true), (DemoScenario.SendRejected, false) })
        {
            var application = DemoApplication.Create(new DemoOptions(scenario));
            await application.InitializeAsync();
            var profile = application.LoadedAccount!;
            var smtp = CredentialKey.For(profile, MailProtocol.Smtp);
            Assert.Equal(saved, await application.Credentials.ContainsAsync(smtp));
            // Presence only: no lookup returns a secret, and neither IMAP nor other server details have a password.
            Assert.Null(await application.Credentials.ReadAsync(smtp));
            Assert.False(await application.Credentials.ContainsAsync(CredentialKey.For(profile, MailProtocol.Imap)));
            var moved = profile with { OutgoingServer = profile.OutgoingServer! with { Port = 2525 } };
            Assert.False(await application.Credentials.ContainsAsync(CredentialKey.For(moved, MailProtocol.Smtp)));
        }
    }

    [Fact]
    public void Receive_Canceled_Is_Information_Beside_The_Kept_Inbox_With_Retry()
    {
        Run(DemoScenario.ReceiveCanceled, (model, shell) =>
        {
            var inbox = model.Inbox;
            Assert.Equal(InboxViewModel.PageSize, inbox.Messages.Count);
            Assert.Equal(55u, inbox.SelectedMessage?.Key.Uid);
            Assert.NotNull(inbox.Body);
            Assert.Equal(InboxProblemScope.List, inbox.ProblemScope);
            Assert.True(inbox.ProblemIsCancellation);
            Assert.True(inbox.CanRetry);
            var notice = Descendants(Tab(shell, "inbox")).OfType<InlineFeedback>().First();
            Assert.Equal((FeedbackKind.Information, "Receiving was canceled."), (notice.Kind, notice.Message));
            Assert.True(IsAvailable(Button(shell, "inbox", "Retry receiving")));
            Assert.True(IsAvailable(Button(shell, "inbox", "Receive mail")));
            Assert.False(Button(shell, "inbox", "Cancel").IsEnabled);
            Assert.Equal("Canceled. Retry is available.", Footer(shell));
        });
    }

    [Fact]
    public void Load_Error_Keeps_The_Loaded_Messages_And_Offers_Retry_Above_The_List()
    {
        Run(DemoScenario.LoadError, (model, shell) =>
        {
            var inbox = model.Inbox;
            Assert.Equal(InboxViewModel.PageSize, inbox.Messages.Count);
            Assert.Equal(55u, inbox.SelectedMessage?.Key.Uid);
            Assert.NotNull(inbox.Body);
            Assert.Equal(InboxProblemScope.List, inbox.ProblemScope);
            Assert.False(inbox.ProblemIsCancellation);
            Assert.Contains("did not respond while loading older messages", inbox.Problem);
            Assert.Contains("from the last successful receive", inbox.Problem);
            // Retry repeats Load older, which stays available too, and both name the older page.
            Assert.True(inbox.CanRetry);
            Assert.True(inbox.CanLoadOlder);
            Assert.True(inbox.ProblemIsOlderPage);
            var notice = Descendants(Tab(shell, "inbox")).OfType<InlineFeedback>().First();
            Assert.Equal((FeedbackKind.Error, inbox.Problem), (notice.Kind, notice.Message));
            Assert.True(IsAvailable(Button(shell, "inbox", "Retry loading older")));
            Assert.True(IsAvailable(Button(shell, "inbox", "Load older")));
            Assert.Equal("Older messages could not be loaded. Details and Retry are above the list.", Footer(shell));
        });
    }

    [Fact]
    public void Draft_Invalid_Keeps_The_Recipient_Error_Beside_The_Editable_Draft()
    {
        Run(DemoScenario.DraftInvalid, (model, shell) =>
        {
            var composer = model.Composer;
            Assert.Equal("team.example.test", composer.To);
            Assert.Equal(FeedbackKind.Error, composer.StatusKind);
            Assert.Equal("Enter valid email addresses separated by commas.", composer.Status);
            Assert.True(composer.CanEdit);
            // The error is the first line below the buttons, above the send hint.
            var status = Descendants(Tab(shell, "compose")).OfType<InlineFeedback>().First(line => line.Message.Length > 0);
            Assert.Equal((FeedbackKind.Error, composer.Status), (status.Kind, status.Message));
            // The To field carries the error too, and reports Invalid with it.
            var to = Descendants(Tab(shell, "compose")).OfType<FormField>().Single(field => field.Label.Text == "To");
            Assert.Equal(composer.Status, to.Error);
            Assert.True(to.GetSemanticNode().State.HasFlag(UiSemanticState.Invalid));
            foreach (var action in new[] { "Check draft", "Save draft", "Discard draft" })
                Assert.True(IsAvailable(Button(shell, "compose", action)), action);
            // The demo never sends; the send hint says so instead of the error.
            Assert.False(Button(shell, "compose", "Send").IsEnabled);
        });
    }

    // The demo tester cannot send; this checks that the fixture's command left the composer untouched as well.
    private static void AssertNoSubmission(MailShellViewModel model)
    {
        Assert.False(model.Composer.HasDraft);
        Assert.Equal(DraftSubmissionState.Editing, model.Composer.SubmissionState);
        Assert.Equal(SentCopyState.NotRequested, model.Composer.SentCopy);
    }

    private static string[] StepLines(MailShellView shell) => Descendants(Tab(shell, "account")).OfType<FormSection>().First().Content.Children
        .OfType<StandardLabel>().Where(label => label.Visibility == UiVisibility.Visible).Select(label => label.Text).ToArray();

    private static UiElement Tab(MailShellView shell, string id) => shell.Navigation.Tabs.Single(tab => tab.Id == id).Content!;

    private static StandardButton Button(MailShellView shell, string tab, string text) =>
        Descendants(Tab(shell, tab)).OfType<StandardButton>().Single(button => button.Text == text);

    private static string Footer(MailShellView shell) => shell.Footer.Text;

    private static bool IsAvailable(StandardButton button)
    {
        for (UiElement? current = button; current is not null; current = current.Parent)
            if (current.Visibility != UiVisibility.Visible) return false;
        return button.IsEnabled;
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private static void Run(DemoScenario scenario, Action<MailShellViewModel> verify) => Run(scenario, (model, _) => verify(model));

    private static void Run(DemoScenario scenario, Action<MailShellViewModel, MailShellView> verify) =>
        Run(scenario, 640, 480, null, (model, shell, _) => verify(model, shell));

    // Mirrors WindowsMailWindow: a queued dispatcher drained on the owning thread, then a rendered frame.
    // Verification runs before the shell is disposed, because disposal also disables the view models. It
    // can render again after an action, as the window does.
    private static void Run(DemoScenario scenario, int width, int height, IHtmlPreviewHost? preview, Action<MailShellViewModel, MailShellView, Action> verify)
    {
        var options = new DemoOptions(scenario, AppTheme.Light, width, height);
        var application = DemoApplication.Create(options);
        application.InitializeAsync().GetAwaiter().GetResult();
        using var woken = new SemaphoreSlim(0);
        var dispatcher = new StandardQueuedUiDispatcher(() => woken.Release());
        var model = application.CreateViewModel(dispatcher);
        using var shell = new MailShellView(model, preview, DemoApplication.CreateDateFormatter());
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
        verify(model, shell, () =>
        {
            dispatcher.Drain();
            session.RenderFrame();
            // What the frame's layout posted, as the window drains it before the next frame.
            dispatcher.Drain();
            session.RenderFrame();
        });
    }

    private sealed class NoPreviewHost : IHtmlPreviewHost
    {
        public event EventHandler<HtmlPreviewChange>? Changed { add { } remove { } }
        public MailMessageKey? Current => null;
        public Task<string> ShowAsync(MailMessageBody message) => Task.FromResult("");
        public void Close() { }
        public void Dispose() { }
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
