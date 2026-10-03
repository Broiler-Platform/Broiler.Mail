using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Infrastructure.Persistence;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-04: toolbars wrap predictably: reading order kept, every action whole and inside the window.</summary>
[Collection("UI theme")]
public sealed class ToolbarWrapTests
{
    [Theory]
    [InlineData(640, 480, 1.0)]
    [InlineData(640, 480, 2.0)]
    [InlineData(1100, 720, 2.0)]
    public void EveryTabsToolbarsWrapInReadingOrderWithWholeActions(double width, double height, double textScale)
    {
        using var directory = new TestDirectory();
        var dispatcher = new TestQueueDispatcher();
        var receiver = new TestMailReceiver();
        var model = new MailShellViewModel(
            new(new JsonAccountStore(directory.File("accounts.json")), new TestCredentialStore(), receiver, dispatcher, TestDirectory.Profile(), null),
            new(new JsonSettingsStore(directory.File("settings.json")), dispatcher, new(), null),
            new(receiver, dispatcher),
            new ComposerViewModel(dispatcher: dispatcher));
        StandardControlPaint.ApplyTheme(StandardThemeTokens.Light.WithTextScale(textScale));
        try
        {
            using var shell = new MailShellView(model);
            using var session = new StandardUiSessionBuilder().WithDispatcher(dispatcher).Build(new Host(width, height));
            session.AddRoot(shell.Window);
            int checkedToolbars = 0, wrapped = 0;
            foreach (string tab in new[] { "inbox", "compose", "account", "settings" })
            {
                shell.Navigation.SelectTab(tab);
                session.RenderFrame();
                session.RenderFrame();
                foreach (var toolbar in Descendants(shell.Navigation.SelectedTab!.Content!).OfType<StandardToolbar>().Where(Shown))
                {
                    checkedToolbars++;
                    string where = $"{tab} toolbar {toolbar.AccessibleName ?? toolbar.GetType().Name} at {width}x{height}, text {textScale:P0}";
                    Assert.True(toolbar.Bounds.Right <= width + 0.5, $"{where} ends at {toolbar.Bounds.Right}, past the window.");
                    UiElement[] items = toolbar.Children.Where(child => child.Visibility == UiVisibility.Visible && child.Bounds.Width > 0).ToArray();
                    for (int i = 0; i < items.Length; i++)
                    {
                        BRect item = items[i].Bounds;
                        Assert.True(Inside(item, toolbar.Bounds), $"{where}: item {i} at {item} is outside {toolbar.Bounds}.");
                        Assert.True(item.Width >= items[i].DesiredSize.Width - 0.5, $"{where}: item {i} is cut to {item.Width} of {items[i].DesiredSize.Width}.");
                        if (i == 0) continue;
                        BRect previous = items[i - 1].Bounds;
                        bool sameRow = Math.Abs(item.Top - previous.Top) < 0.5 && item.Left >= previous.Right - 0.5;
                        bool nextRow = item.Top >= previous.Bottom - 0.5;
                        Assert.True(sameRow || nextRow, $"{where}: item {i} at {item} breaks reading order after {previous}.");
                        if (!sameRow) wrapped++;
                    }
                }
            }
            Assert.True(checkedToolbars >= 3, $"Only {checkedToolbars} toolbars were shown.");
            if (width == 640 && textScale == 2)
                Assert.True(wrapped > 0, "At the minimum size with doubled text some toolbar must wrap.");
        }
        finally
        {
            StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
        }
    }

    private static bool Inside(BRect inner, BRect outer) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 && inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static bool Shown(UiElement element)
    {
        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0) return false;
        for (UiElement? current = element; current is not null; current = current.Parent)
            if (current.Visibility != UiVisibility.Visible) return false;
        return true;
    }

    private static IEnumerable<UiElement> Descendants(UiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var item in Descendants(child)) yield return item;
    }

    private sealed class Host(double width, double height) : IUiHost
    {
        public BSize ViewportSize => new(width, height);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
