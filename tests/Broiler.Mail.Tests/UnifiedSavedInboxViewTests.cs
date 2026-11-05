using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Standard;
using static Broiler.Mail.Tests.UnifiedSavedInboxTests;

namespace Broiler.Mail.Tests;

[Collection("UI theme")]
public sealed class UnifiedSavedInboxViewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task ExplicitRefreshAndReadAttributeIdenticalUidsToTheCorrectAccount(double scale)
    {
        var a = TestDirectory.Profile(); var b = TestDirectory.Profile() with { IsEnabled = false };
        var cache = new FakeCache { Headers = (key, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(key.AccountId == a.Id ? a : b, 1)]),
            Body = (key, uid, _) => Task.FromResult<MailMessageBody?>(new(new(key.AccountId, "INBOX", 7, uid), "Body for " + key.AccountId)) };
        using var view = new UnifiedSavedInboxView(cache); view.SetAccounts([a, b]); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var theme = StandardThemeTokens.Light.WithTextScale(scale); StandardControlPaint.SetSessionTheme(session, theme); StandardThemeController.ApplyToSubtree(view, theme); session.RenderFrame();
        Assert.Empty(view.Rows); Assert.Empty(view.PreviewText); Assert.All(All(view).OfType<StandardButton>(), button => Assert.False(button.IsDefault));
        await view.RefreshAsync(); queue.Drain(); Assert.Equal(2, view.Rows.Count); Assert.Empty(view.PreviewText); Assert.Contains("50 newest", view.Status);
        var picker = All(view).OfType<StandardComboBox>().Single(); Assert.Equal(-1, picker.SelectedIndex);
        foreach (var row in view.Rows.ToArray())
        {
            await view.ReadAsync(row); queue.Drain(); session.RenderFrame(); Assert.Contains("Account " + row.Account.Id, view.PreviewText); Assert.Contains("Body for " + row.Account.Id, view.PreviewText);
        }
        view.SetActive(false); Assert.Empty(view.Rows); Assert.Empty(view.PreviewText);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("accounts")]
    [InlineData("dispose")]
    [InlineData("queued")]
    [InlineData("selection")]
    public async Task PendingOrQueuedBodyCannotRepaintLostContext(string mode)
    {
        var a = TestDirectory.Profile(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MailMessageBody?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache { Headers = (_, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(a, 2), Row(a, 1)]),
            Body = (_, _, _) => { entered.TrySetResult(); return release.Task; } };
        using var view = new UnifiedSavedInboxView(cache); view.SetAccounts([a]); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view); session.RenderFrame();
        await view.RefreshAsync(); queue.Drain(); var row = view.Rows[0]; var reading = view.ReadAsync(row); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode == "cancel") view.Cancel(); else if (mode == "close") view.SetActive(false); else if (mode == "accounts") view.SetAccounts([TestDirectory.Profile()]); else if (mode == "dispose") view.Dispose(); else if (mode == "selection") All(view).OfType<StandardComboBox>().Single().SelectedIndex = 1;
        release.TrySetResult(new(row.Message.Key, "Late body")); await reading; if (mode == "queued") view.SetActive(false); queue.Drain(); Assert.Empty(view.PreviewText);
    }
    [Fact]
    public async Task NewAccountSnapshotCanCompleteWhileAnOldCacheReadIgnoresCancellation()
    {
        var a = TestDirectory.Profile(); var b = TestDirectory.Profile(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CachedMailbox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache
        {
            Head = (account, _) => { if (account == a.Id) { entered.TrySetResult(); return release.Task; } return Task.FromResult<CachedMailbox?>(new(new(account, "INBOX", 7), DateTimeOffset.UtcNow)); },
            Headers = (_, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(b, 1)])
        };
        using var view = new UnifiedSavedInboxView(cache); view.SetAccounts([a]); view.SetActive(true);
        var queue = new TestQueueDispatcher(); using var session = new StandardUiSessionBuilder().WithDispatcher(queue).Build(new Host()); session.AddRoot(view);
        var old = view.RefreshAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        view.SetAccounts([b]); await view.RefreshAsync(); queue.Drain(); Assert.Equal(b.Id, Assert.Single(view.Rows).Account.Id);
        release.TrySetResult(null); await old; queue.Drain(); Assert.Equal(b.Id, Assert.Single(view.Rows).Account.Id);
    }

    [Fact]
    public async Task InvalidBodyIsReportedWithoutEscapingTheUiOperation()
    {
        var a = TestDirectory.Profile(); var cache = new FakeCache
        {
            Headers = (_, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(a, 1)]),
            Body = (_, _, _) => Task.FromResult<MailMessageBody?>(new(new(a.Id, "INBOX", 7, 2), "Wrong message"))
        };
        using var view = new UnifiedSavedInboxView(cache); view.SetAccounts([a]); view.SetActive(true);
        await view.RefreshAsync(); await view.ReadAsync(view.Rows[0]); Assert.Empty(view.PreviewText); Assert.Contains("InvalidDataException", view.Status); Assert.False(view.IsBusy);
    }

    [Fact]
    public async Task MissingBodyAndUnavailableHostsHaveHonestCoverageAndDoNotFetch()
    {
        var a = TestDirectory.Profile(); var cache = new FakeCache { Headers = (_, _) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([Row(a, 1)]) };
        using var view = new UnifiedSavedInboxView(cache); view.SetAccounts([a]); view.SetActive(true); await view.RefreshAsync(); await view.ReadAsync(view.Rows[0]); Assert.Contains("not downloaded", view.Status); Assert.Empty(view.PreviewText);
        using var missing = new UnifiedSavedInboxView(null); missing.SetAccounts([a]); missing.SetActive(true); await missing.RefreshAsync(); Assert.Contains("unavailable", missing.Status); Assert.All(All(missing).OfType<StandardButton>(), button => Assert.False(button.IsEnabled));
    }
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class Host : IUiHost
    { public BSize ViewportSize => new(940, 680); public double Scale => 1; public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation value) { } public void Present(BRenderList value) { } }
}
