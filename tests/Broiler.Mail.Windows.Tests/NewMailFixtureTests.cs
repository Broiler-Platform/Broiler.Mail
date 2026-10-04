using System.Text.RegularExpressions;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;
using Broiler.UI;
using Broiler.UI.ListView.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>UI-02: the new-mail fixture, its acceptance-only server changes, and the simulated-scale option.</summary>
public sealed class NewMailFixtureTests
{
    [Theory]
    [InlineData("--demo new-mail", "None", null)]
    [InlineData("--demo new-mail --server-change vanish", "Vanish", null)]
    [InlineData("--demo new-mail --server-change outside --theme dark", "Outside", null)]
    [InlineData("--demo new-mail --scale 200 --server-change renumber", "Renumber", 200)]
    [InlineData("--demo inbox --scale 150", "None", 150)]
    [InlineData("--demo --scale 300", "None", 300)]
    public void Options_Parse_The_Server_Change_And_The_Simulated_Scale(string arguments, string change, int? scale)
    {
        Assert.True(DemoOptions.TryParse(arguments.Split(' '), out var options));
        Assert.Equal((Enum.Parse<DemoServerChange>(change), scale), (options!.ServerChange, options.ScalePercent));
    }

    [Theory]
    [InlineData("--demo inbox --server-change vanish")]
    [InlineData("--demo --server-change vanish")]
    [InlineData("--demo new-mail --server-change none")]
    [InlineData("--demo new-mail --server-change")]
    [InlineData("--demo new-mail --server-change vanish --server-change outside")]
    [InlineData("--demo inbox --scale 99")]
    [InlineData("--demo inbox --scale 301")]
    [InlineData("--demo inbox --scale 1.5")]
    [InlineData("--demo inbox --scale +200")]
    public void Options_Reject_A_Server_Change_Without_New_Mail_And_A_Scale_Outside_100_To_300(string arguments) =>
        Assert.False(DemoOptions.TryParse(arguments.Split(' '), out _));

    [Fact]
    public void The_Window_Title_Names_A_Simulated_Scale()
    {
        Assert.Equal("Broiler.Mail — Demo: new-mail (no network or saved data)", new DemoOptions(DemoScenario.NewMail).WindowTitle);
        Assert.Equal("Broiler.Mail — Demo (no network or saved data)", new DemoOptions(DemoScenario.Inbox, Interactive: true).WindowTitle);
        Assert.Equal("Broiler.Mail — Demo: inbox, simulated 200% scale (no network or saved data)", new DemoOptions(DemoScenario.Inbox, ScalePercent: 200).WindowTitle);
    }

    [Fact]
    public void Help_Lists_New_Mail_As_A_Fixture_And_The_Acceptance_Options_After_The_Workloads()
    {
        using var output = new StringWriter();
        Program.WriteHelp(output);
        string[] lines = output.ToString().Split(Environment.NewLine);
        // Read the fixtures the way scripts/Accept-UI.ps1 does, so the acceptance options never become fixtures.
        var fixtures = new List<string>();
        bool inGallery = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("Plain --demo", StringComparison.OrdinalIgnoreCase)) { inGallery = true; continue; }
            if (line.StartsWith("--measure", StringComparison.OrdinalIgnoreCase)) { inGallery = false; continue; }
            if (inGallery && Regex.Match(line, @"^\s+(\S+)\s") is { Success: true } match) fixtures.Add(match.Groups[1].Value);
        }
        Assert.Equal(DemoOptions.Gallery.Select(item => item.Name), fixtures);
        Assert.Contains("new-mail", fixtures);
        int workloads = Array.FindIndex(lines, line => line.StartsWith("--measure", StringComparison.Ordinal));
        int acceptance = Array.FindIndex(lines, line => line.StartsWith("Acceptance-only", StringComparison.Ordinal));
        Assert.True(acceptance > workloads);
        Assert.Contains(lines[acceptance..], line => line.Trim() == "--server-change vanish|outside|renumber");
        Assert.Contains(lines[acceptance..], line => line.Contains("simulated display scale", StringComparison.Ordinal));
    }

    [Fact]
    public void New_Mail_Arrives_Above_The_Open_Message_Which_Keeps_Its_Body_And_Is_Now_Read()
    {
        using var run = FixtureRun.Start(new DemoOptions(DemoScenario.NewMail, AppTheme.Light, 1100, 720));
        var inbox = run.Model.Inbox;
        Assert.Equal(InboxViewModel.PageSize, inbox.Messages.Count);
        Assert.Equal([58u, 57u, 56u, 55u], inbox.Messages.Take(4).Select(message => message.Key.Uid));
        Assert.All(inbox.Messages.Take(DemoMailbox.Arrivals), message => Assert.StartsWith("New message", message.Subject));
        Assert.Equal(55u, inbox.SelectedMessage?.Key.Uid);
        // Unread in the first receive; another client read it before the second.
        Assert.True(inbox.SelectedMessage!.IsRead);
        Assert.Equal(inbox.SelectedMessage.Key, inbox.Body?.Key);
        Assert.Null(inbox.Problem);
        Assert.DoesNotContain("Select one", inbox.Status);
        // At the top of the list nothing anchors, so the new rows are visible above the open one.
        var list = run.Descendant<StandardListView>();
        Assert.Equal(0, list.VerticalOffset);
        Assert.Equal("1:58", list.Items[0].Id);
        Assert.Equal("1:55", list.SelectedItemId);
        Assert.StartsWith("Read · Welcome to Broiler.Mail", list.Items[3].Text);
    }

    [Fact]
    public void Receive_After_The_Fixture_Keeps_The_List_Anchor_Selection_Reader_Scroll_And_Text_Selection()
    {
        using var run = FixtureRun.Start(new DemoOptions(DemoScenario.NewMail, AppTheme.Light, 1100, 720));
        var list = run.Descendant<StandardListView>();
        list.ScrollIntoView(30);
        run.Render();
        int first = list.FirstVisibleIndex;
        string anchor = list.Items[first].Id;
        double withinRow = list.VerticalOffset - first * list.EffectiveItemHeight;
        Assert.True(first > 0);
        // Choose a row below the anchor, as a user would.
        string chosen = list.Items[first + 2].Id;
        Assert.StartsWith("Unread · ", list.Items[first + 2].Text);
        run.Do(() =>
        {
            list.SelectedItemId = chosen;
            return Task.CompletedTask;
        });
        var body = run.Model.Inbox.Body;
        Assert.Equal(chosen, $"1:{body?.Key.Uid}");
        var reader = run.Descendant<ScrollableMessageText>();
        var scroll = Descendants(reader).OfType<StandardScrollView>().Single();
        Assert.True(scroll.ScrollBy(0, 300));
        run.Render();
        double readerOffset = scroll.VerticalOffset;
        Assert.True(reader.Editor.SetEditorSelection(120, 180));
        var selection = reader.Editor.Selection;

        run.Do(run.Model.Inbox.ReceiveAsync);

        Assert.Equal("1:61", list.Items[0].Id);
        Assert.Equal(anchor, list.Items[list.FirstVisibleIndex].Id);
        Assert.Equal(withinRow, list.VerticalOffset - list.FirstVisibleIndex * list.EffectiveItemHeight, 3);
        Assert.Equal(chosen, list.SelectedItemId);
        Assert.StartsWith("Read · ", list.Items.Single(item => item.Id == chosen).Text);
        Assert.Same(body, run.Model.Inbox.Body);
        Assert.Equal(readerOffset, scroll.VerticalOffset);
        Assert.Equal(selection, reader.Editor.Selection);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Vanish")]
    [InlineData("Outside")]
    [InlineData("Renumber")]
    public void The_Receive_After_The_Fixture_Applies_The_Server_Change_To_The_Open_Message(string name)
    {
        var change = Enum.Parse<DemoServerChange>(name);
        using var run = FixtureRun.Start(new DemoOptions(DemoScenario.NewMail, AppTheme.Light, 1100, 720, ServerChange: change));
        var inbox = run.Model.Inbox;
        var open = inbox.Messages.Single(message => message.Key.Uid == 40).Key;
        run.Do(() => inbox.SelectAsync(open));
        var body = inbox.Body;
        Assert.Equal(open, body?.Key);

        run.Do(inbox.ReceiveAsync);

        switch (change)
        {
            case DemoServerChange.None:
                Assert.Equal(61u, inbox.Messages[0].Key.Uid);
                Assert.Equal(open, inbox.SelectedMessage?.Key);
                Assert.True(inbox.SelectedMessage!.IsRead);
                Assert.Same(body, inbox.Body);
                break;
            case DemoServerChange.Vanish:
                Assert.DoesNotContain(inbox.Messages, message => message.Key == open);
                Assert.Null(inbox.SelectedMessage);
                Assert.Null(inbox.Body);
                Assert.Contains("no longer in the inbox", inbox.Status);
                break;
            case DemoServerChange.Outside:
                Assert.All(inbox.Messages, message => Assert.True(message.Key.Uid > 58));
                Assert.Equal(open, inbox.SelectedMessage?.Key);
                Assert.Same(body, inbox.Body);
                Assert.Contains("older than the newest page", inbox.Status);
                // Load older reaches it with the cursor of the changed inbox.
                run.Do(inbox.LoadOlderAsync);
                Assert.Contains(inbox.Messages, message => message.Key == open);
                Assert.Equal(open, inbox.SelectedMessage?.Key);
                break;
            case DemoServerChange.Renumber:
                Assert.All(inbox.Messages, message => Assert.Equal(2u, message.Key.UidValidity));
                Assert.Null(inbox.SelectedMessage);
                Assert.Contains("renumbered", inbox.Status);
                break;
        }

        // The change applies once; the receive after it is an ordinary arrival again.
        int newest = (int)inbox.Messages[0].Key.Uid;
        run.Do(inbox.ReceiveAsync);
        Assert.Equal(newest + DemoMailbox.Arrivals, (int)inbox.Messages[0].Key.Uid);
    }

    [Fact]
    public async Task Rows_Keep_Their_Dates_While_Mail_Arrives_And_A_Cursor_From_Before_A_Change_Is_Refused()
    {
        var application = DemoApplication.Create(new DemoOptions(DemoScenario.NewMail, ServerChange: DemoServerChange.Vanish));
        await application.InitializeAsync();
        var account = application.LoadedAccount!;
        var receiver = application.Receiver;
        var first = await receiver.GetInboxAsync(account, InboxViewModel.PageSize);
        var open = first.Messages.Single(message => message.Key.Uid == 40);
        Assert.False(open.IsRead);
        await receiver.GetBodyAsync(account, open.Key);

        var second = await receiver.GetInboxAsync(account, InboxViewModel.PageSize);
        Assert.Equal(58u, second.Messages[0].Key.Uid);
        // A row already listed is unchanged apart from the read flag another client set.
        foreach (var row in second.Messages.Where(row => first.Messages.Any(before => before.Key == row.Key)))
            Assert.Equal(first.Messages.Single(before => before.Key == row.Key) with { IsRead = row.IsRead }, row);
        Assert.Equal([open.Key], second.Messages.Where(row => first.Messages.Any(before => before.Key == row.Key && before.IsRead != row.IsRead)).Select(row => row.Key));

        // The third receive applies the change: the open message is deleted and nothing arrives.
        var third = await receiver.GetInboxAsync(account, InboxViewModel.PageSize);
        Assert.Equal(58u, third.Messages[0].Key.Uid);
        Assert.DoesNotContain(third.Messages, row => row.Key == open.Key);
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetInboxAsync(account, InboxViewModel.PageSize, second.Older));
        await Assert.ThrowsAsync<MailConnectionException>(() => receiver.GetBodyAsync(account, open.Key));
        // Paging from the current cursor stays contiguous across the gap and ends at the oldest message.
        var older = await receiver.GetInboxAsync(account, InboxViewModel.PageSize, third.Older);
        Assert.Null(older.Older);
        Assert.Equal(Enumerable.Range(1, 58).Where(uid => uid != 40).Reverse().Select(uid => (uint)uid),
            third.Messages.Concat(older.Messages).Select(row => row.Key.Uid));
        Assert.Equal(first.Messages[^1] with { IsRead = false }, older.Messages.Single(row => row.Key == first.Messages[^1].Key) with { IsRead = false });
    }

    [Fact]
    public async Task Other_Fixtures_Keep_Their_Fixed_Mailbox_Across_Receives()
    {
        var application = DemoApplication.Create(new DemoOptions(DemoScenario.Inbox));
        await application.InitializeAsync();
        var account = application.LoadedAccount!;
        var first = await application.Receiver.GetInboxAsync(account, InboxViewModel.PageSize);
        await application.Receiver.GetBodyAsync(account, first.Messages[0].Key);
        var second = await application.Receiver.GetInboxAsync(account, InboxViewModel.PageSize);
        Assert.Equal(first.Messages, second.Messages);
        Assert.Equal(55u, first.Messages[0].Key.Uid);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2)), first.Messages[0].ReceivedAt);
        Assert.False(first.Messages[0].IsRead);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    /// <summary>
    /// Runs a fixture in a headless session that is attached before the driver starts, so layout and
    /// anchoring take part in every step, as in the window. Draining and rendering stay on the test
    /// thread, which owns the queued dispatcher; nothing here awaits.
    /// </summary>
    private sealed class FixtureRun : IDisposable
    {
        private readonly SemaphoreSlim _woken;
        private readonly StandardQueuedUiDispatcher _dispatcher;
        private readonly UiSession _session;

        private FixtureRun(SemaphoreSlim woken, StandardQueuedUiDispatcher dispatcher, MailShellViewModel model, MailShellView shell, UiSession session)
        {
            _woken = woken; _dispatcher = dispatcher; Model = model; Shell = shell; _session = session;
        }

        public MailShellViewModel Model { get; }
        public MailShellView Shell { get; }

        public static FixtureRun Start(DemoOptions options)
        {
            var application = DemoApplication.Create(options);
            application.InitializeAsync().GetAwaiter().GetResult();
            var woken = new SemaphoreSlim(0);
            var dispatcher = new StandardQueuedUiDispatcher(() => woken.Release());
            var model = application.CreateViewModel(dispatcher);
            var shell = new MailShellView(model, null, DemoApplication.CreateDateFormatter());
            var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new HeadlessHost(options.Width, options.Height));
            session.AddRoot(shell.Window);
            var run = new FixtureRun(woken, dispatcher, model, shell, session);
            run.Render();
            var driver = DemoScenarioDriver.Start(options, model, shell, dispatcher);
            run.DrainUntil(() => driver.Completion.IsCompleted);
            driver.Completion.GetAwaiter().GetResult();
            return run;
        }

        public T Descendant<T>() where T : UiElement => Descendants(Shell.Window).OfType<T>().Single();

        public void Render() => Assert.NotNull(_session.RenderFrame());

        /// <summary>Starts an inbox command and drains until its result has been applied and drawn.</summary>
        public void Do(Func<Task> command)
        {
            var task = command();
            DrainUntil(() => task.IsCompleted && !Model.Inbox.IsBusy);
            task.GetAwaiter().GetResult();
        }

        private void DrainUntil(Func<bool> settled)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                _dispatcher.Drain();
                Render();
                if (settled()) break;
                Assert.True(DateTime.UtcNow < deadline, "The fixture did not settle.");
                _woken.Wait(TimeSpan.FromMilliseconds(100));
            }
            _dispatcher.Drain();
            Render();
        }

        public void Dispose()
        {
            _session.Dispose();
            Shell.Dispose();
            _woken.Dispose();
        }
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
