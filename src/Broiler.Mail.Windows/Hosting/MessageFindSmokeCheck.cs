using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Preview;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

internal static class MessageFindSmokeCheck
{
    internal static void Run()
    {
        string body = string.Join("\n", Enumerable.Range(1, 80).Select(i => "Published paragraph " + i)) + "\nNeedle then Needle";
        using var root = new StandardPanel { LayoutMode = UiPanelLayoutMode.Dock }; using var text = new ScrollableMessageText { Text = body };
        bool current = true; using var find = new MessageTextFindBar(text, body, 0, () => { }, () => current, () => { });
        root.AddChild(find); root.SetDock(find, UiDock.Top); root.AddChild(text); var host = new Host();
        using var session = new StandardUiSessionBuilder().Build(host); session.AddRoot(root); find.ShowFind(); session.RenderFrame();
        All(find).OfType<StandardEdit>().Single().Text = "NEEDLE"; find.Search(); session.RenderFrame();
        if (!find.Status.Contains("Match 1 of 2", StringComparison.Ordinal) || text.ReadingPosition <= 0 || text.Editor.Document.Slice(text.Editor.Selection).PlainText != "Needle")
            throw new InvalidOperationException("Published message find/reveal failed.");
        Button("Next match").Click(); if (!find.Status.Contains("Match 2 of 2", StringComparison.Ordinal)) throw new InvalidOperationException("Published next message match failed.");
        Button("Next match").Click(); if (!find.Status.Contains("Wrapped", StringComparison.Ordinal)) throw new InvalidOperationException("Published find wrap failed.");
        Button("Copy match").Click(); if (host.Clipboard != "Needle" || host.Reads != 0) throw new InvalidOperationException("Published bounded explicit match copy failed.");
        host.FailCopy = true; Button("Copy match").Click(); if (!find.Status.Contains("failed", StringComparison.Ordinal)) throw new InvalidOperationException("Published clipboard failure handling failed.");
        current = false; host.Clipboard = "Keep"; host.FailCopy = false; Button("Copy match").Click(); if (host.Clipboard != "Keep") throw new InvalidOperationException("Published stale match copied text.");
        var capped = MessageTextFinder.Find(new string('a', 1001), "A"); if (!capped.HasMore || capped.Matches.Count != 1000) throw new InvalidOperationException("Published find bounds failed.");
        StandardButton Button(string name) => All(find).OfType<StandardButton>().Single(button => button.Text == name);
    }
    private static IEnumerable<UiElement> All(UiElement root)
    { yield return root; foreach (var child in root.Children) foreach (var item in All(child)) yield return item; }
    private sealed class Host : IUiHost, IUiClipboardHost
    {
        public string Clipboard = ""; public int Reads; public bool FailCopy;
        public bool TryGetText(out string value) { Reads++; value = Clipboard; return true; }
        public void SetText(string value) { if (FailCopy) throw new InvalidOperationException("Synthetic clipboard busy"); Clipboard = value; }
        public BSize ViewportSize => new(640, 480); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList list) { }
    }
}
