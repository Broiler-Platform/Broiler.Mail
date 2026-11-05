using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class FolderNavigationViewTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task NamedFolderSelectorStaysBoundedAndRejectsHierarchyOnlySelection(double scale)
    {
        var dispatcher = new TestQueueDispatcher();
        using var model = new InboxViewModel(new FolderNavigationTests.FolderReceiver(), dispatcher);
        model.SetAccount(TestDirectory.Profile()); await model.RefreshFoldersAsync(); dispatcher.Drain();
        using var root = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host());
        session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        session.RenderFrame();
        var selector = Descendants(root).OfType<StandardComboBox>().Single(control => control.AccessibleName == "Mail folder");
        session.SetFocus(selector);
        selector.SelectedIndex = model.Folders.ToList().FindIndex(folder => folder.MailboxId == "Container");
        Assert.Equal("INBOX", model.SelectedMailboxId); Assert.Equal(0, selector.SelectedIndex);
        selector.SelectedIndex = model.Folders.ToList().FindIndex(folder => folder.MailboxId == "Archive");
        Assert.Equal("Archive", model.SelectedMailboxId); Assert.Null(model.Body); Assert.Same(selector, session.FocusedElement);
        await model.ReceiveAsync(); dispatcher.Drain(); session.RenderFrame();
        Assert.Equal("Archive", Assert.Single(model.Messages).Key.MailboxId);
        foreach (var control in new UiElement[] { selector, Descendants(root).OfType<StandardButton>().Single(b => b.Text == "Refresh folders") })
        {
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
            Assert.InRange(control.Bounds.Left, -1, 640);
            Assert.InRange(control.Bounds.Right, 0, 641);
            Assert.InRange(control.Bounds.Bottom, 0, 481);
        }
        Assert.True(Descendants(root).OfType<StandardListView>().Single().Bounds.Height >= 40);
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(2.0, false)]
    [InlineData(1.0, true)]
    [InlineData(2.0, true)]
    public async Task FlagButtonIsReachableAndHandsFocusToCancelWhilePending(double scale, bool starred)
    {
        var dispatcher = new TestQueueDispatcher(); var writer = new ReadStateChangeTests.Writer();
        using var model = new InboxViewModel(writer, dispatcher); model.SetAccount(TestDirectory.Profile());
        await model.ReceiveAsync(); dispatcher.Drain(); await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var body = model.Body;
        var view = new InboxView(model); using var root = view.CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        Assert.True(view.OpenSelected()); dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        var button = Descendants(root).OfType<StandardButton>().Single(control => control.Text == (starred ? "Star message" : "Mark read"));
        session.SetFocus(button);
        for (var parent = button.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not Broiler.UI.ScrollView.Standard.StandardScrollView scroll) continue;
            double delta = button.Bounds.Bottom - scroll.ContentBounds.Bottom;
            if (delta > 0) { scroll.ScrollBy(0, delta); session.RenderFrame(); }
        }
        Assert.True(button.IsEnabled); Assert.InRange(button.Bounds.Bottom, 1, 481);
        button.Click(); Assert.True(model.IsChangingMessageFlags);
        Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        Assert.True(starred ? model.SelectedMessage!.IsStarred : model.SelectedMessage!.IsRead); Assert.Equal(starred ? "Unstar message" : "Mark unread", button.Text);
        Assert.Equal("Body", model.Body!.PlainText); Assert.Equal(1, writer.Calls);
    }

    [Theory]
    [InlineData(1.0, "move")]
    [InlineData(2.0, "move")]
    [InlineData(1.0, "archive")]
    [InlineData(2.0, "archive")]
    [InlineData(1.0, "trash")]
    [InlineData(2.0, "trash")]
    public async Task MoveControlsUseExplicitDestinationOrConfirmedRoleAndRemainReachable(double scale, string action)
    {
        var dispatcher = new TestQueueDispatcher(); var mover = new MoveWorkflowTests.Mover();
        using var model = new InboxViewModel(mover, dispatcher); model.SetAccount(TestDirectory.Profile());
        await model.RefreshFoldersAsync(); dispatcher.Drain(); await model.ReceiveAsync(); dispatcher.Drain(); await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var view = new InboxView(model); using var root = view.CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        Assert.True(view.OpenSelected()); dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        var picker = Descendants(root).OfType<StandardComboBox>().Single(control => control.AccessibleName == "Move destination");
        Assert.DoesNotContain(model.MoveDestinations, folder => folder.MailboxId is "INBOX" or "Container");
        var button = Descendants(root).OfType<StandardButton>().Single(control => control.Text == (action == "move" ? "Move message" : action == "archive" ? "Archive" : "Move to trash"));
        if (action == "move") { Assert.False(button.IsEnabled); picker.SelectedIndex = model.MoveDestinations.ToList().FindIndex(folder => folder.MailboxId == "Archive"); }
        session.SetFocus(button);
        for (var parent = button.Parent; parent is not null; parent = parent.Parent)
            if (parent is Broiler.UI.ScrollView.Standard.StandardScrollView scroll)
            { double delta = button.Bounds.Bottom - scroll.ContentBounds.Bottom; if (delta > 0) { scroll.ScrollBy(0, delta); session.RenderFrame(); } }
        Assert.True(button.IsEnabled); Assert.InRange(button.Bounds.Left, -1, 640); Assert.InRange(button.Bounds.Right, 0, 641); Assert.InRange(button.Bounds.Bottom, 1, 481);
        button.Click(); Assert.True(model.IsMoving); Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        Assert.Equal(action == "trash" ? "Trash" : "Archive", mover.LastDestination); Assert.Equal(1, mover.Calls);
        Assert.Empty(model.Messages); Assert.Null(model.Body); Assert.True(session.FocusedElement?.CanFocus);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task UnknownMoveIsExplainedInsideTheCompactReader(double scale)
    {
        var dispatcher = new TestQueueDispatcher(); var mover = new MoveWorkflowTests.Mover { Status = Broiler.Mail.Core.Services.MailMoveStatus.Unknown };
        using var model = new InboxViewModel(mover, dispatcher); model.SetAccount(TestDirectory.Profile());
        await model.RefreshFoldersAsync(); dispatcher.Drain(); await model.ReceiveAsync(); dispatcher.Drain(); await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var view = new InboxView(model); using var root = view.CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        Assert.True(view.OpenSelected()); dispatcher.DrainUntil(() => !model.IsBusy);
        await model.ArchiveSelectedAsync(); dispatcher.Drain(); session.RenderFrame();
        Assert.NotNull(model.Body); Assert.True(view.ShowsPaneOf(InboxProblemScope.Message));
        var header = Descendants(root).OfType<BoundedScrollArea>().Single(area => area.Scroll.AccessibleName == "Message header");
        Assert.Contains(Descendants(header).OfType<Broiler.UI.Label.Standard.StandardLabel>(), label => label.Text.Contains("Synthetic Unknown"));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task UndoIsReachableWithoutASelectionAndConsumesItsAction(double scale)
    {
        var dispatcher = new TestQueueDispatcher(); var mover = new MoveWorkflowTests.Mover();
        using var model = new InboxViewModel(mover, dispatcher); model.SetAccount(TestDirectory.Profile());
        await model.RefreshFoldersAsync(); dispatcher.Drain(); await model.ReceiveAsync(); dispatcher.Drain(); await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        using var root = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        await model.ArchiveSelectedAsync(); dispatcher.Drain(); session.RenderFrame(); Assert.Null(model.SelectedMessage);
        var button = Descendants(root).OfType<StandardButton>().Single(control => control.Text == "Undo last move");
        Assert.True(button.IsEnabled); Assert.Equal(UiVisibility.Visible, button.Visibility);
        Assert.InRange(button.Bounds.Left, -1, 640); Assert.InRange(button.Bounds.Right, 0, 641); Assert.InRange(button.Bounds.Bottom, 1, 481);
        session.SetFocus(button); button.Click(); Assert.True(model.IsMoving); Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        Assert.False(model.CanUndoMove); Assert.Equal(UiVisibility.Collapsed, button.Visibility); Assert.Equal(2, mover.Calls); Assert.True(session.FocusedElement?.CanFocus);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task PermanentDeletionFocusesKeepMessageAndRequiresSeparateConfirmation(double scale)
    {
        var dispatcher = new TestQueueDispatcher(); var deleter = new PermanentDeleteWorkflowTests.Deleter();
        using var model = new InboxViewModel(deleter, dispatcher); model.SetAccount(TestDirectory.Profile());
        await model.RefreshFoldersAsync(); dispatcher.Drain(); model.SelectFolder("Trash"); await model.ReceiveAsync(); dispatcher.Drain(); await model.SelectAsync(model.Messages[0].Key); dispatcher.Drain();
        var view = new InboxView(model); using var root = view.CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        view.OpenSelected(); dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        StandardButton Button(string text) => Descendants(root).OfType<StandardButton>().Single(button => button.Text == text);
        var request = Button("Delete permanently…"); request.Click(); session.RenderFrame();
        var keep = Button("Keep message"); var confirm = Button("Confirm permanent deletion");
        Assert.Same(keep, session.FocusedElement); Assert.False(confirm.IsDefault); Assert.True(model.CanConfirmPermanentDelete); Assert.Equal(0, deleter.Calls);
        Assert.InRange(keep.Bounds.Top, -1, 480); Assert.InRange(keep.Bounds.Bottom, 1, 481);
        keep.Click(); Assert.Null(model.PendingPermanentDelete); Assert.Equal(0, deleter.Calls);
        request.Click(); confirm.Click(); Assert.True(model.IsDeleting); Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame(); Assert.Equal(1, deleter.Calls); Assert.Null(model.Body); Assert.Empty(model.Messages);
        Assert.True(session.FocusedElement?.CanFocus);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task SavedSearchControlsKeepFocusUsableAndDoNotFetchBodies(double scale)
    {
        using var directory = new TestDirectory(); var dispatcher = new TestQueueDispatcher();
        var account = TestDirectory.Profile(); var cache = new Broiler.Mail.Infrastructure.Persistence.SqliteMailCache(directory.File("mail.db"));
        var mailbox = new Broiler.Mail.Core.Messages.MailboxKey(account.Id, "INBOX", 7);
        var rows = Enumerable.Range(1, 501).Select(uid => new Broiler.Mail.Core.Messages.MailMessageSummary
            { Key = new(account.Id, "INBOX", 7, (uint)uid), Sender = "Sender", Subject = "needle", ReceivedAt = DateTimeOffset.UtcNow.AddDays(-1) }).ToArray();
        await cache.SaveSummariesAsync(mailbox, rows.Take(500).ToArray(), DateTimeOffset.UtcNow);
        await cache.SaveSummariesAsync(mailbox, rows.Skip(500).ToArray(), DateTimeOffset.UtcNow);
        var receiver = new InboxSearchTests.NoNetworkReceiver();
        using var model = new InboxViewModel(receiver, dispatcher, cache); model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain();
        using var root = new InboxView(model).CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale);
        StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme); session.RenderFrame();
        StandardButton Button(string text) => Descendants(root).OfType<StandardButton>().Single(button => button.Text == text);
        Button("Search saved mail").Click(); session.RenderFrame();
        var input = Descendants(root).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(control => control.AccessibleName == "Search saved sender or subject");
        Assert.Same(input, session.FocusedElement); input.Text = "needle";
        var field = Descendants(root).OfType<StandardComboBox>().Single(control => control.AccessibleName == "Saved search header field");
        field.SelectedIndex = 4; Assert.Equal(Broiler.Mail.Core.Services.MailHeaderSearchField.HeadersOrBodyPreview, model.SearchField);
        var flags = Descendants(root).OfType<StandardComboBox>().Single(control => control.AccessibleName == "Saved search filter");
        flags.SelectedIndex = 25; Assert.Equal(false, model.SearchReadState); Assert.Equal(false, model.SearchStarState); Assert.Equal(false, model.SearchBodyCached);
        var dates = Descendants(root).OfType<StandardComboBox>().Single(control => control.AccessibleName == "Saved search received date");
        dates.SelectedIndex = 4; Assert.True(model.SearchUsesCustomDates); Assert.False(model.CanSearch);
        var range = Descendants(root).OfType<Broiler.UI.Edit.Standard.StandardEdit>().Single(control => control.AccessibleName?.StartsWith("UTC date range", StringComparison.Ordinal) == true);
        range.Text = "0001-01-01..9999-12-31"; Assert.True(model.CanSearch);
        var find = Button("Find saved headers"); session.SetFocus(find); find.Click();
        Assert.True(model.IsBusy); Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame(); Assert.Equal(500, model.Messages.Count);
        flags.SelectedIndex = 17; Assert.Equal(true, model.SearchReadState); Assert.Equal(false, model.SearchStarState); Assert.Equal(true, model.SearchBodyCached);
        var next = Button("Next saved results"); Assert.True(next.IsEnabled);
        session.SetFocus(next); next.Click(); Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame(); Assert.Single(model.Messages); Assert.False(next.IsEnabled);
        Assert.False(Button("Receive mail").IsEnabled); Assert.True(session.FocusedElement?.CanFocus);
        Assert.True(Descendants(root).OfType<StandardListView>().Single().Bounds.Height >= 40);
        var previous = Button("Previous saved results"); Assert.True(previous.IsEnabled); session.SetFocus(previous); previous.Click();
        Assert.Equal("Cancel", Assert.IsType<StandardButton>(session.FocusedElement).Text);
        dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame(); Assert.Equal(500, model.Messages.Count); Assert.False(previous.IsEnabled);
        Button("Close search").Click(); dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame();
        Assert.False(model.IsSearchActive); Assert.Same(Button("Search saved mail"), session.FocusedElement); Assert.Equal(0, receiver.Calls);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task ParsedHeaderDisclosureUsesSavedMetadataWithoutReloadingBody(double scale)
    {
        using var directory = new TestDirectory(); var dispatcher = new TestQueueDispatcher(); var account = TestDirectory.Profile();
        var cache = new Broiler.Mail.Infrastructure.Persistence.SqliteMailCache(directory.File("mail.db"));
        var key = new Broiler.Mail.Core.Messages.MailMessageKey(account.Id, "INBOX", 7, 1);
        var mailbox = new Broiler.Mail.Core.Messages.MailboxKey(account.Id, "INBOX", 7);
        await cache.SaveSummariesAsync(mailbox, [new() { Key = key, Sender = "Sender", Subject = "Topic" }], DateTimeOffset.UtcNow);
        await cache.SaveBodyAsync(mailbox, new(key, "Saved body") { Composition = new() { From = ["sender@example.test"], MessageId = "fixture@example.test" } });
        var receiver = new InboxSearchTests.NoNetworkReceiver(); using var model = new InboxViewModel(receiver, dispatcher, cache);
        model.SetAccount(account); await model.CacheLoadTask; dispatcher.Drain(); await model.SelectAsync(key); dispatcher.Drain();
        var view = new InboxView(model); using var root = view.CreateContent();
        using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host()); session.AddRoot(root);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(root, theme);
        view.OpenSelected(); dispatcher.DrainUntil(() => !model.IsBusy); session.RenderFrame(); var body = model.Body;
        var details = Descendants(root).OfType<Broiler.UI.RichEdit.Standard.StandardRichEdit>().Single(control => control.AccessibleName == "Sender and recipients");
        Assert.DoesNotContain("Message-ID:", details.GetPlainText());
        var show = Descendants(root).OfType<StandardButton>().Single(button => button.Text == "Show parsed headers"); show.Click(); session.RenderFrame();
        Assert.Contains("Message-ID: fixture@example.test", details.GetPlainText()); Assert.Same(body, model.Body); Assert.Equal(0, receiver.Calls);
        show.Click(); Assert.DoesNotContain("Message-ID:", details.GetPlainText());
    }

    private static IEnumerable<UiElement> Descendants(UiElement parent)
    {
        foreach (var child in parent.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class Host : IUiHost
    {
        public Broiler.Graphics.Geometry.BSize ViewportSize => new(640, 480);
        public double Scale => 1;
        public Broiler.Graphics.RenderList.BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(Broiler.Graphics.RenderList.BRenderList renderList) { }
    }
}
