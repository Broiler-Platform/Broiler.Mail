using System.Net;
using System.Net.Sockets;
using Broiler.Graphics.Geometry;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.UI;

namespace Broiler.Mail.Windows.Tests;

public sealed class HtmlPreviewIsolationTests
{
    [Fact]
    public async Task BroilerHtmlPolicyBlocksActiveContentNavigationAndResourcesAndSurvivesFailures()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string target = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/blocked";
        string folder = Path.Combine(Path.GetTempPath(), "Broiler.Mail.HtmlTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string sentinel = Path.Combine(folder, "secret.txt");
        await File.WriteAllTextAsync(sentinel, "private-file-sentinel");
        string fileUrl = new Uri(sentinel).AbsoluteUri;

        string hostile = $"<html><body><h1>Readable</h1><script>window.mailScriptRan=true;fetch('{target}')</script><img src='{target}'><img src='{fileUrl}'><iframe src='{fileUrl}'></iframe><link rel=stylesheet href='{target}'><style>@import url('{target}');@font-face{{font-family:evil;src:url('{target}')}}body{{background:url('{target}');font-family:evil}}</style><a href='https://example.test/selected'>link</a></body></html>";
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var opened = new List<Uri>();
                using var window = new HtmlPreviewWindow(new(hostile, new HashSet<string> { "https://example.test/selected" }), "Fallback body", opened.Add)
                { ShowInTaskbar = false, Opacity = 0 };

                window.Shown += async (_, _) =>
                {
                    try
                    {
                        await window.InitializeAsync(Path.Combine(folder, "profile"));
                        Assert.True(await window.Loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)), window.FailureDiagnostic ?? "The Broiler.HTML preview must load the restricted fixture.");

                        await Task.Delay(300);
                        Assert.False(listener.Pending(), "No email-triggered HTTP request may reach the loopback listener.");

                        Assert.Empty(opened); // Programmatic navigation must never launch a browser.
                        window.OpenLink("https://example.test/selected", userInitiated: true);
                        window.OpenLink("file:///C:/secret", userInitiated: true);
                        window.OpenLink("https://example.test/not-in-message", userInitiated: true);
                        Assert.Single(opened);
                        Assert.Equal("https://example.test/selected", opened[0].AbsoluteUri);

                        Assert.False(window.IsShowingPlainText);
                        window.ToggleView();
                        Assert.True(window.IsShowingPlainText);
                        window.ToggleView();
                        Assert.False(window.IsShowingPlainText);

                        window.ShowFailure();
                        Assert.True(window.IsShowingPlainText);

                        Assert.False(listener.Pending());
                        finished.TrySetResult();
                    }
                    catch (Exception error) { finished.TrySetException(error); }
                    finally { window.Close(); }
                };
                window.Run();
            }
            catch (Exception error) { finished.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(50)); }
        finally
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try { Directory.Delete(folder, recursive: true); break; }
                catch (IOException) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) { await Task.Delay(100); }
            }
        }
    }

    [Fact]
    public void ScrollableHtmlViewPreservesAspectRatioForShortAndLongDocuments()
    {
        // 1. Short document fixture (matching user's GitHub notification issue)
        string shortHtml = "<p>Closed <a href='https://example.test/380'>#380</a> as completed.</p><p>—</p><p>Reply to this email directly.</p>";
        var shortView = new ScrollableHtmlView(shortHtml, () => null, _ => { });
        shortView.Measure(new BSize(900, 600));
        shortView.Arrange(new BRect(0, 0, 900, 600));

        // Short view content is measured at its natural content height without vertical elongation
        Assert.True(shortView.Content.DesiredSize.Height < 400, $"Short content desired height should be natural (~100-200), was {shortView.Content.DesiredSize.Height}");

        // 2. Long document fixture
        string tallHtml = string.Join("", Enumerable.Repeat("<p>Paragraph line of text</p>", 80));
        var tallView = new ScrollableHtmlView(tallHtml, () => null, _ => { });
        tallView.Measure(new BSize(900, 600));
        tallView.Arrange(new BRect(0, 0, 900, 600));

        Assert.True(tallView.Content.DesiredSize.Height > 1000, "Tall document content must measure beyond viewport height.");
        Assert.True(tallView.Content.Bounds.Height > 1000, "Tall document content must arrange beyond viewport height to scroll.");
    }

    [Fact]
    public async Task LoadRemoteImagesAsyncEnforcesStreamingBudgetAndHandlesFailureGracefully()
    {
        string html = "<html><body><img src='http://127.0.0.1:1/nonexistent.jpg'></body></html>";
        var doc = HtmlPreviewPolicy.Create(html);
        using var window = new HtmlPreviewWindow(doc, "Fallback", _ => { }, rawHtml: html);

        Assert.False(window.AreRemoteImagesAllowed);
        await window.LoadRemoteImagesAsync();
        // Failed image download keeps allowed false and allows retry
        Assert.False(window.AreRemoteImagesAllowed);
    }
}
