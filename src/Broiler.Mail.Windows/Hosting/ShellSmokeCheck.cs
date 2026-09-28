using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Checks composition and layout without opening a native window or touching accounts.</summary>
internal static class ShellSmokeCheck
{
    public static void Run(MailShellView shell)
    {
        using var session = new StandardUiSessionBuilder().Build(new HeadlessHost());
        session.AddRoot(shell.Window);
        foreach (string tabId in new[] { "inbox", "account", "settings" })
        {
            if (!shell.Navigation.SelectTab(tabId))
                throw new InvalidOperationException($"Missing shell tab: {tabId}");
            _ = session.RenderFrame();
        }
    }

    private sealed class HeadlessHost : IUiHost
    {
        public BSize ViewportSize => new(1100, 720);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
