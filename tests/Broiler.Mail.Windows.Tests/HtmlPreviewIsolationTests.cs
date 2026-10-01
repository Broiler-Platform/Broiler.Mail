using System.Net;
using System.Net.Sockets;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.Mail.Windows.Preview;
using Broiler.Media;
using Broiler.Media.Image;
using Broiler.UI;
using Broiler.UI.Standard;

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
    public void TableLayoutAndOverflowFixture_ProducesValidDiagnosticsAndRenderList()
    {
        string tableHtml = @"
            <table border='1' style='width: 100%; border-collapse: collapse;'>
              <thead>
                <tr>
                  <th style='width: 30%;'>Header A</th>
                  <th style='width: 70%;'>Header B with wide description</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>Col 1 Row 1</td>
                  <td>Col 2 Row 1 text content that explains the table row in detail.</td>
                </tr>
                <tr>
                  <td colspan='2' style='background-color: #eee;'>Colspan row spanning entire width</td>
                </tr>
                <tr>
                  <td>Nested:</td>
                  <td>
                    <table style='width: 100%;'>
                      <tr><td>Subcell 1</td><td>Subcell 2</td></tr>
                    </table>
                  </td>
                </tr>
              </tbody>
            </table>";

        var renderer = new TestBroilerRenderer();
        var view = new ScrollableHtmlView(tableHtml, () => renderer, _ => { });
        view.Measure(new BSize(800, 600));
        view.Arrange(new BRect(0, 0, 800, 600));

        var snapshot = view.Snapshot;
        Assert.NotNull(snapshot);
        Assert.Equal(788f, snapshot.Width);
        Assert.True(snapshot.ContentHeight > 50f, $"ContentHeight should be > 50, was {snapshot.ContentHeight}");
        Assert.False(snapshot.IsTruncated);
        Assert.True(snapshot.LayoutDurationTicks >= 0);

        // Verify R-03 diagnostics exposure
        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "table");
        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "th");
        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "td");

        var host = new TestUiHost { ViewportSize = new BSize(800, 600), Scale = 1.0 };
        var session = new StandardUiSessionBuilder().Build(host);
        var renderList = new BRenderList();
        var context = new UiRenderContext(renderList, session, host);

        view.Render(context);
        Assert.NotEmpty(renderer.CreatedImages);
        Assert.Contains(renderList.Commands, c => c is BRenderCommand.DrawImage);
    }

    [Fact]
    public void LongWordsAndOverflowFixture_MeasuresBoundedWithoutCrash()
    {
        string longWordsHtml = @"
            <div style='width: 350px; overflow: hidden; text-overflow: ellipsis;'>
              <p>SupercalifragilisticexpialidociousSupercalifragilisticexpialidociousSupercalifragilisticexpialidociousSupercalifragilisticexpialidociousSupercalifragilisticexpialidocious</p>
              <p>https://example.test/very/long/unbroken/path/that/does/not/contain/any/whitespace/and/could/overflow/unbounded/rendering/containers/if/not/properly/measured/and/handled</p>
            </div>";

        var renderer = new TestBroilerRenderer();
        var view = new ScrollableHtmlView(longWordsHtml, () => renderer, _ => { });
        view.Measure(new BSize(600, 500));
        view.Arrange(new BRect(0, 0, 600, 500));

        Assert.True(double.IsFinite(view.DesiredSize.Width));
        Assert.True(double.IsFinite(view.DesiredSize.Height));
        Assert.True(view.DesiredSize.Height > 40f);

        var snapshot = view.Snapshot;
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "div");
        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "p");
    }

    [Fact]
    public void QuotationsAndInlineImagesFixture_PreservesDataUrisAndQuotations()
    {
        // 1x1 red PNG data URI
        string redPixelDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        string quoteHtml = $@"
            <blockquote style='border-left: 4px solid #0066cc; margin: 8px 0; padding-left: 12px;'>
              <p>Primary quotation header</p>
              <blockquote style='border-left: 4px solid #999; margin: 4px 0; padding-left: 8px;'>
                <p>Nested quotation block</p>
              </blockquote>
            </blockquote>
            <p><img src='{redPixelDataUri}' width='24' height='24' alt='Inline marker' /></p>
            <p><img src='http://127.0.0.1:8888/blocked.png' width='24' height='24' alt='Remote blocked' /></p>";

        var renderer = new TestBroilerRenderer();
        var view = new ScrollableHtmlView(quoteHtml, () => renderer, _ => { });
        view.Measure(new BSize(700, 500));
        view.Arrange(new BRect(0, 0, 700, 500));

        var snapshot = view.Snapshot;
        Assert.NotNull(snapshot);
        Assert.True(snapshot.ContentHeight > 60f);

        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "blockquote");
        Assert.Contains(snapshot.Diagnostics, d => d.TagName == "img");
    }

    [Fact]
    public void DeeplyNestedBlocksFixture_HandlesLargeDepthWithoutStackOverflow()
    {
        int depth = 350;
        string nestedHtml = string.Concat(Enumerable.Repeat("<div>", depth))
            + "<p>Deep content safely reached</p>"
            + string.Concat(Enumerable.Repeat("</div>", depth));

        var renderer = new TestBroilerRenderer();
        var view = new ScrollableHtmlView(nestedHtml, () => renderer, _ => { });
        view.Measure(new BSize(800, 600));
        view.Arrange(new BRect(0, 0, 800, 600));

        var snapshot = view.Snapshot;
        Assert.NotNull(snapshot);
        Assert.True(snapshot.ContentHeight > 30f);
        Assert.Equal(depth, snapshot.Diagnostics.Count(d => d.TagName == "div"));
    }

    [Fact]
    public void RtlTextFixture_LaysOutAndSupportsHitTesting()
    {
        string rtlHtml = @"
            <div dir='rtl' style='text-align: right;'>
              <h2>رسالة واردة باللغة العربية</h2>
              <p>هذا نص للتجربة داخل مشغل البريد الإلكتروني المخصص للتأكد من المحاذاة.</p>
              <a href='https://example.test/rtl-action'>انقر هنا لتأكيد الحساب</a>
            </div>";

        string? clickedUrl = null;
        var renderer = new TestBroilerRenderer();
        var element = new HtmlViewElement(rtlHtml, () => renderer, url => clickedUrl = url);
        element.Measure(new BSize(750, 500));
        element.Arrange(new BRect(0, 0, 750, 500));

        var snapshot = element.Snapshot;
        Assert.NotNull(snapshot);
        Assert.True(snapshot.ContentHeight > 50f);

        var link = Assert.Single(snapshot.Links);
        Assert.Equal("https://example.test/rtl-action", link.Href);
        Assert.True(link.Bounds.Width > 10, "Link bounds width should be positive.");
        Assert.True(link.Bounds.Height > 5, "Link bounds height should be positive.");

        // Simulate click on the link
        var clickEvent = MouseClick(link.Bounds.X + (link.Bounds.Width / 2), link.Bounds.Y + (link.Bounds.Height / 2));
        bool handled = element.SendInput(clickEvent);
        Assert.True(handled);
        Assert.Equal("https://example.test/rtl-action", clickedUrl);
    }

    [Fact]
    public void HighDpiTilingFixture_ScalesBitmapsProportionallyAndInvalidatesOnDpiChange()
    {
        string html = "<div style='height: 400px; background: #eee;'><p>High DPI Test</p></div>";
        double currentDpi = 1.0;
        var renderer = new TestBroilerRenderer();
        var element = new HtmlViewElement(html, () => renderer, _ => { }, () => currentDpi);

        element.Measure(new BSize(800, 600));
        element.Arrange(new BRect(0, 0, 800, 600));

        var host = new TestUiHost { ViewportSize = new BSize(800, 600), Scale = 1.0 };
        var session = new StandardUiSessionBuilder().Build(host);
        var renderList = new BRenderList();
        var context = new UiRenderContext(renderList, session, host);

        // 1. Render at 1.0x DPI
        element.Render(context);
        Assert.Single(renderer.CreatedImages);
        Assert.Equal(1, element.CachedTileCount);

        // 2. Switch to 2.0x DPI
        currentDpi = 2.0;
        host.Scale = 2.0;
        var renderList2 = new BRenderList();
        var context2 = new UiRenderContext(renderList2, session, host);

        element.Render(context2);
        // Previous image should be released
        Assert.NotEmpty(renderer.ReleasedImages);
        // New tile created at 2.0x
        Assert.Equal(2, renderer.CreatedImages.Count);
        Assert.Equal(1, element.CachedTileCount);
    }

    [Fact]
    public void BoundedTilingAndBudgetTruncationFixture_TruncatesVisiblyAndCapsTileCache()
    {
        // 900 paragraphs * 45px = ~40,500px, which exceeds MaxBudgetHeight (32,768px)
        string hugeHtml = string.Concat(Enumerable.Repeat("<p style='height: 45px; margin: 0;'>Extremely tall document line exceeding height limit</p>", 900));

        var renderer = new TestBroilerRenderer();
        var element = new HtmlViewElement(hugeHtml, () => renderer, _ => { });
        element.Measure(new BSize(800, 600));
        element.Arrange(new BRect(0, 0, 800, HtmlViewElement.MaxBudgetHeight));

        var snapshot = element.Snapshot;
        Assert.NotNull(snapshot);
        Assert.True(snapshot.UnclampedHeight > HtmlViewElement.MaxBudgetHeight, $"UnclampedHeight ({snapshot.UnclampedHeight}) should exceed limit.");
        Assert.True(snapshot.IsTruncated, "Document exceeding budget must be flagged as truncated.");
        Assert.Equal(HtmlViewElement.MaxBudgetHeight, snapshot.ContentHeight);

        var host = new TestUiHost { ViewportSize = new BSize(800, 600), Scale = 1.0 };
        var session = new StandardUiSessionBuilder().Build(host);

        // 1. Render top tile
        var rlTop = new BRenderList();
        element.Render(new UiRenderContext(rlTop, session, host));
        Assert.True(element.IsTileCached(0));

        // 2. Simulate scrolling down through many tiles (tiles 1 to 20)
        for (int tileIndex = 1; tileIndex <= 20; tileIndex++)
        {
            double tileY = tileIndex * HtmlViewElement.DefaultTileHeight;
            element.Arrange(new BRect(0, -tileY, 800, HtmlViewElement.MaxBudgetHeight));
            var rl = new BRenderList();
            element.Render(new UiRenderContext(rl, session, host));
        }

        // Cache must NEVER exceed MaxCachedTiles (16)
        Assert.True(element.CachedTileCount <= HtmlViewElement.MaxCachedTiles,
            $"Tile cache must be bounded to {HtmlViewElement.MaxCachedTiles}, but was {element.CachedTileCount}");
        Assert.NotEmpty(renderer.ReleasedImages);

        // 3. Render bottom tile (where truncation banner sits)
        double bottomY = HtmlViewElement.MaxBudgetHeight - 600;
        element.Arrange(new BRect(0, -bottomY, 800, HtmlViewElement.MaxBudgetHeight));
        var rlBottom = new BRenderList();
        element.Render(new UiRenderContext(rlBottom, session, host));

        // Must render the visible truncation banner with explicit warning
        var textCommands = rlBottom.Commands.OfType<BRenderCommand.DrawText>().ToList();
        Assert.Contains(textCommands, t => t.Text.Text.Contains("exceeds maximum render limit"));
    }

    [Fact]
    public void CachedLayoutSnapshotAndLinkHitTestingFixture_CachesLayoutAndDispatchesLink()
    {
        string html = "<p>Message content with <a href='https://example.test/link1'>Link 1</a> and <a href='https://example.test/link2'>Link 2</a>.</p>";
        string? clicked = null;
        var renderer = new TestBroilerRenderer();
        var element = new HtmlViewElement(html, () => renderer, url => clicked = url);

        element.Measure(new BSize(800, 600));
        element.Arrange(new BRect(0, 0, 800, 600));

        var snapshot1 = element.Snapshot;
        Assert.NotNull(snapshot1);
        Assert.Equal(2, snapshot1.Links.Count);

        // Second measure at same width must reuse identical snapshot
        element.Measure(new BSize(800, 600));
        var snapshot2 = element.Snapshot;
        Assert.Same(snapshot1, snapshot2);

        // Test link hit testing
        var targetLink = snapshot1.Links[0];
        var hitEvent = MouseClick(targetLink.Bounds.X + 2, targetLink.Bounds.Y + 2);

        Assert.True(element.SendInput(hitEvent));
        Assert.Equal("https://example.test/link1", clicked);

        // Updating HTML must invalidate snapshot and tiles
        element.UpdateHtml("<p>Updated document</p>");
        Assert.Null(element.Snapshot);
        Assert.Equal(0, element.CachedTileCount);
    }

    [Fact]
    public void ImageCodecInspection_ValidImages_ParsedAndValidatedSuccessfully()
    {
        // 1x1 Red PNG
        byte[] pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var (pngValid, pngInfo, pngReason, pngDetail) = HtmlResourceLoader.InspectImageBytes(pngBytes);
        Assert.True(pngValid);
        Assert.NotNull(pngInfo);
        Assert.Equal(1, pngInfo.Width);
        Assert.Equal(1, pngInfo.Height);
        Assert.Equal("PNG", pngInfo.FormatName);
        Assert.Equal(HtmlResourceFailureReason.None, pngReason);
        Assert.Null(pngDetail);

        // 1x1 GIF
        byte[] gifBytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var (gifValid, gifInfo, gifReason, _) = HtmlResourceLoader.InspectImageBytes(gifBytes);
        Assert.True(gifValid);
        Assert.NotNull(gifInfo);
        Assert.Equal(1, gifInfo.Width);
        Assert.Equal(1, gifInfo.Height);
        Assert.Equal("GIF", gifInfo.FormatName);
        Assert.Equal(HtmlResourceFailureReason.None, gifReason);

        // Synthetic JPEG 640x480
        byte[] jpegBytes = CreateJpegWithDimensions(640, 480);
        var (jpegValid, jpegInfo, jpegReason, _) = HtmlResourceLoader.InspectImageBytes(jpegBytes);
        Assert.True(jpegValid);
        Assert.NotNull(jpegInfo);
        Assert.Equal(640, jpegInfo.Width);
        Assert.Equal(480, jpegInfo.Height);
        Assert.Equal("JPEG", jpegInfo.FormatName);
        Assert.Equal(HtmlResourceFailureReason.None, jpegReason);
    }

    [Fact]
    public void ImageCodecInspection_CorruptedAndMalformedHeaders_SafelyRejectedWithoutCrash()
    {
        // Empty bytes
        var (emptyValid, _, emptyReason, _) = HtmlResourceLoader.InspectImageBytes(Array.Empty<byte>());
        Assert.False(emptyValid);
        Assert.Equal(HtmlResourceFailureReason.CorruptedHeader, emptyReason);

        // Random non-image bytes
        byte[] garbage = new byte[256];
        Random.Shared.NextBytes(garbage);
        var (garbageValid, _, garbageReason, _) = HtmlResourceLoader.InspectImageBytes(garbage);
        Assert.False(garbageValid);
        Assert.Equal(HtmlResourceFailureReason.CorruptedHeader, garbageReason);

        // Truncated PNG signature (only 4 bytes)
        byte[] truncatedPng = [137, 80, 78, 71];
        var (truncValid, _, truncReason, _) = HtmlResourceLoader.InspectImageBytes(truncatedPng);
        Assert.False(truncValid);
        Assert.Equal(HtmlResourceFailureReason.CorruptedHeader, truncReason);

        // Corrupted JPEG header (SOI present, but truncated marker)
        byte[] corruptJpeg = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x04];
        var (corruptJpValid, _, corruptJpReason, _) = HtmlResourceLoader.InspectImageBytes(corruptJpeg);
        Assert.False(corruptJpValid);
        Assert.Equal(HtmlResourceFailureReason.CorruptedHeader, corruptJpReason);
    }

    [Fact]
    public void DecompressionBombProtection_ExcessiveDimensionsOrPixels_RejectedAsBudgetExceeded()
    {
        // Header declaring 5000 x 5000 (exceeds default MaxImageDimension of 4096)
        byte[] bombDim = CreateJpegWithDimensions(5000, 5000);
        var (bombDimValid, bombDimInfo, bombDimReason, bombDimDetail) = HtmlResourceLoader.InspectImageBytes(bombDim);
        Assert.False(bombDimValid);
        Assert.NotNull(bombDimInfo);
        Assert.Equal(5000, bombDimInfo.Width);
        Assert.Equal(5000, bombDimInfo.Height);
        Assert.Equal(HtmlResourceFailureReason.DimensionBudgetExceeded, bombDimReason);
        Assert.Contains("exceed maximum allowed dimension", bombDimDetail);

        // Header within dimension limits (4096 x 4096) but exceeding pixel budget of 16,000,000 pixels (16.7M px)
        byte[] bombPixel = CreateJpegWithDimensions(4096, 4096);
        var (bombPxValid, bombPxInfo, bombPxReason, bombPxDetail) = HtmlResourceLoader.InspectImageBytes(bombPixel);
        Assert.False(bombPxValid);
        Assert.NotNull(bombPxInfo);
        Assert.Equal(HtmlResourceFailureReason.PixelBudgetExceeded, bombPxReason);
        Assert.Contains("exceeds maximum allowed pixel budget", bombPxDetail);
    }

    [Fact]
    public void ByteBudgetProtection_OversizedPayload_RejectedAsByteBudgetExceeded()
    {
        // Custom tight limits for testing
        var tightLimits = new MediaLimits(maxEncodedBytes: 1024, maxDecodedBytes: 64 * 1024, maxImagePixels: 10000, maxImageDimension: 100);
        byte[] largePayload = new byte[2048];
        var (valid, _, reason, detail) = HtmlResourceLoader.InspectImageBytes(largePayload, tightLimits);
        Assert.False(valid);
        Assert.Equal(HtmlResourceFailureReason.ByteBudgetExceeded, reason);
        Assert.Contains("exceeds limit", detail);
    }

    [Fact]
    public void BatchResourceLoading_AggregatesStructuredOutcomesCorrectly()
    {
        var resSuccess = new HtmlResourceResult("https://example.test/1.png", HtmlResourceOutcomeKind.Rendered, HtmlResourceFailureReason.None, 100);
        var resFailed = new HtmlResourceResult("https://example.test/2.jpg", HtmlResourceOutcomeKind.Failed, HtmlResourceFailureReason.CorruptedHeader, 50);
        var resBudget = new HtmlResourceResult("https://example.test/3.jpg", HtmlResourceOutcomeKind.BudgetExceeded, HtmlResourceFailureReason.DimensionBudgetExceeded, 200);
        var resBlocked = new HtmlResourceResult("https://example.test/4.jpg", HtmlResourceOutcomeKind.Blocked, HtmlResourceFailureReason.PolicyBlocked);

        // Mixed success and failures -> Partial
        var partialBatch = HtmlResourceBatchResult.Create([resSuccess, resFailed, resBudget], 350);
        Assert.Equal(HtmlResourceOutcomeKind.Partial, partialBatch.OverallOutcome);
        Assert.Equal(3, partialBatch.TotalRequested);
        Assert.Equal(1, partialBatch.TotalSucceeded);
        Assert.Equal(1, partialBatch.TotalFailed);
        Assert.Equal(1, partialBatch.TotalBudgetExceeded);
        Assert.Equal(350, partialBatch.TotalDownloadedBytes);

        // All success -> Rendered
        var successBatch = HtmlResourceBatchResult.Create([resSuccess], 100);
        Assert.Equal(HtmlResourceOutcomeKind.Rendered, successBatch.OverallOutcome);

        // Only budget exceeded -> BudgetExceeded
        var budgetBatch = HtmlResourceBatchResult.Create([resBudget], 200);
        Assert.Equal(HtmlResourceOutcomeKind.BudgetExceeded, budgetBatch.OverallOutcome);

        // Only blocked -> Blocked
        var blockedBatch = HtmlResourceBatchResult.Create([resBlocked], 0);
        Assert.Equal(HtmlResourceOutcomeKind.Blocked, blockedBatch.OverallOutcome);

        // Only failed -> Failed
        var failedBatch = HtmlResourceBatchResult.Create([resFailed], 50);
        Assert.Equal(HtmlResourceOutcomeKind.Failed, failedBatch.OverallOutcome);
    }

    [Fact]
    public async Task StreamingResourceLoader_EnforcesStreamingLimitAndValidatesMimeTypes()
    {
        // 1. Mock streaming response exceeding 5 MB limit
        var infiniteHandler = new MockHttpMessageHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InfiniteStream())
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return resp;
        });
        using var clientInfinite = new HttpClient(infiniteHandler);
        var (resInfinite, dataInfinite) = await HtmlResourceLoader.FetchAndValidateImageAsync(
            clientInfinite, "https://example.test/huge.png", HtmlResourceLoader.DefaultMailLimits, 0, 20_000_000, CancellationToken.None);

        Assert.Equal(HtmlResourceOutcomeKind.BudgetExceeded, resInfinite.Outcome);
        Assert.Equal(HtmlResourceFailureReason.ByteBudgetExceeded, resInfinite.Reason);
        Assert.Null(dataInfinite);

        // 2. Mock unsafe SVG response
        var svgHandler = new MockHttpMessageHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<svg><script>alert(1)</script></svg>", System.Text.Encoding.UTF8, "image/svg+xml")
            };
            return resp;
        });
        using var clientSvg = new HttpClient(svgHandler);
        var (resSvg, dataSvg) = await HtmlResourceLoader.FetchAndValidateImageAsync(
            clientSvg, "https://example.test/vector.svg", HtmlResourceLoader.DefaultMailLimits, 0, 20_000_000, CancellationToken.None);

        Assert.Equal(HtmlResourceOutcomeKind.Failed, resSvg.Outcome);
        Assert.Equal(HtmlResourceFailureReason.InvalidContentType, resSvg.Reason);
        Assert.Null(dataSvg);

        // 3. Mock corrupted image bytes with image/jpeg header
        var corruptHandler = new MockHttpMessageHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 })
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return resp;
        });
        using var clientCorrupt = new HttpClient(corruptHandler);
        var (resCorrupt, dataCorrupt) = await HtmlResourceLoader.FetchAndValidateImageAsync(
            clientCorrupt, "https://example.test/corrupt.jpg", HtmlResourceLoader.DefaultMailLimits, 0, 20_000_000, CancellationToken.None);

        Assert.Equal(HtmlResourceOutcomeKind.Failed, resCorrupt.Outcome);
        Assert.Equal(HtmlResourceFailureReason.CorruptedHeader, resCorrupt.Reason);
        Assert.Null(dataCorrupt);

        // 4. Mock valid 1x1 PNG response
        byte[] validPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var validHandler = new MockHttpMessageHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(validPng)
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return resp;
        });
        using var clientValid = new HttpClient(validHandler);
        var (resValid, dataValid) = await HtmlResourceLoader.FetchAndValidateImageAsync(
            clientValid, "https://example.test/pixel.png", HtmlResourceLoader.DefaultMailLimits, 0, 20_000_000, CancellationToken.None);

        Assert.Equal(HtmlResourceOutcomeKind.Rendered, resValid.Outcome);
        Assert.Equal(HtmlResourceFailureReason.None, resValid.Reason);
        Assert.NotNull(dataValid);
        Assert.Equal(validPng.Length, resValid.BytesLoaded);
        Assert.NotNull(resValid.ImageInfo);
        Assert.Equal(1, resValid.ImageInfo.Width);
        Assert.Equal(1, resValid.ImageInfo.Height);
    }

    [Fact]
    public async Task StreamingResourceLoader_FullBatchLoad_PopulatesOnlyValidImages()
    {
        byte[] validPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        byte[] bombJpeg = CreateJpegWithDimensions(5000, 5000);

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("good.png"))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(validPng) };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return resp;
            }
            if (req.RequestUri.AbsoluteUri.Contains("bomb.jpg"))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bombJpeg) };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return resp;
            }
            if (req.RequestUri.AbsoluteUri.Contains("missing.jpg"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        using var client = new HttpClient(handler);
        var urls = new[]
        {
            "https://example.test/good.png",
            "https://example.test/bomb.jpg",
            "https://example.test/missing.jpg"
        };

        var (batchResult, downloadedImages) = await HtmlResourceLoader.LoadBatchAsync(
            client, urls, initialImages: null, cancellationToken: CancellationToken.None);

        // Overall outcome should be Partial because 1 succeeded and others failed/budget-exceeded
        Assert.Equal(HtmlResourceOutcomeKind.Partial, batchResult.OverallOutcome);
        Assert.Equal(3, batchResult.TotalRequested);
        Assert.Equal(1, batchResult.TotalSucceeded);
        Assert.Equal(1, batchResult.TotalBudgetExceeded);
        Assert.Equal(1, batchResult.TotalFailed);

        // Only the valid PNG should be in downloadedImages
        Assert.Single(downloadedImages);
        Assert.True(downloadedImages.ContainsKey("https://example.test/good.png"));
        Assert.False(downloadedImages.ContainsKey("https://example.test/bomb.jpg"));
        Assert.False(downloadedImages.ContainsKey("https://example.test/missing.jpg"));
    }

    private static byte[] CreateJpegWithDimensions(ushort width, ushort height)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]); // SOI
        ms.Write([0xFF, 0xC0]); // SOF0
        ms.Write([0x00, 0x11]); // length 17
        ms.WriteByte(8); // precision
        ms.Write([(byte)(height >> 8), (byte)(height & 0xFF)]);
        ms.Write([(byte)(width >> 8), (byte)(width & 0xFF)]);
        ms.WriteByte(3); // components
        ms.Write([1, 0x11, 0]);
        ms.Write([2, 0x11, 0]);
        ms.Write([3, 0x11, 0]);
        ms.Write([0xFF, 0xD9]); // EOI
        return ms.ToArray();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class InfiniteStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill(0xAA);
            return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            buffer.AsSpan(offset, count).Fill(0xAA);
            return Task.FromResult(count);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static InputEventHeader Header() => new(InputDeviceId.FromOpaqueValue("test"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1);

    private static UiInputEvent MouseClick(double x, double y) => UiInputEvent.FromMouseButton(new MouseButtonEvent(
        Header(), InputPoint.ClientDeviceIndependentPixels(x, y),
        MouseButtons.None, MouseButton.Left, MouseButtonTransition.Up, InputEventSource.Synthetic));

    private sealed class TestBroilerRenderer : IBroilerRenderer
    {
        private int _nextId = 1;
        public List<(int HandleId, byte[] PngData)> CreatedImages { get; } = new();
        public List<BImageHandle> ReleasedImages { get; } = new();

        public BImageHandle CreateImage(ReadOnlySpan<byte> encodedData)
        {
            int id = _nextId++;
            CreatedImages.Add((id, encodedData.ToArray()));
            return new BImageHandle(new BResourceHandle(BResourceKind.Image, (ulong)id), new BSize(100, 100));
        }

        public BImageHandle CreateImage(BPixelBuffer pixelBuffer) =>
            new BImageHandle(new BResourceHandle(BResourceKind.Image, (ulong)_nextId++), new BSize(100, 100));

        public void ReleaseImage(BImageHandle handle) => ReleasedImages.Add(handle);
        public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor) => throw new NotImplementedException();
        public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext) { }
        public Broiler.Graphics.Imaging.BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext) => throw new NotImplementedException();
        public void Dispose() { }
    }

    private sealed class TestUiHost : IUiHost
    {
        public BSize ViewportSize { get; set; } = new(900, 700);
        public double Scale { get; set; } = 1.0;
        public BRenderList CreateRenderList(int capacity = 0) => new();
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }
}
