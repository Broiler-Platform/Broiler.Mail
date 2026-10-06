using System.Text;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Mail;
using Broiler.Mail.Infrastructure.Preview;

namespace Broiler.Mail.Tests;

public sealed class HtmlPreviewPolicyTests
{
    [Fact]
    public void ReducesHostileMarkupToPassiveFormattingAndExplicitWebLinks()
    {
        const string html = "<html><head><meta http-equiv=refresh content='0;url=file:///secret'><base href='https://attacker.test'><link rel=stylesheet href='https://attacker.test/x'></head><body onload='steal()'><h1>Hello &amp; welcome</h1><p style='background:url(file:///secret)' onclick='steal()'>Text <b>bold</b></p><script>alert(1)</script><iframe srcdoc='<script>steal()</script>'>frame</iframe><form action='https://attacker.test'><input autofocus><button>Submit</button></form><svg><a href='javascript:alert(1)'>svg link</a></svg><object data='file:///secret'></object><img src='cid:secret' onerror='steal()'><audio src='https://attacker.test'></audio><a href='https://example.test/a?b=1&amp;c=2' ping='https://attacker.test' download>Good link</a><a href='javascript:alert(1)'>Bad link</a></body></html>";
        var document = HtmlPreviewPolicy.Create(html);
        Assert.Contains("<h1>Hello &amp; welcome</h1>", document.Html);
        Assert.Contains("<b>bold</b>", document.Html);
        foreach (string forbidden in new[] { "<script", "<iframe", "<form", "<input", "<button", "<svg", "<object", "<img", "<audio", "<base", "<link", "onload=", "onclick=", "onerror=", "ping=", "javascript:", "attacker.test", "file:///secret", "url(" })
            Assert.DoesNotContain(forbidden, document.Html);
        Assert.Equal("https://example.test/a?b=1&c=2", Assert.Single(document.ExternalLinks));
        Assert.Contains("default-src 'none'", document.Html);
        Assert.Contains("form-action 'none'", document.Html);
    }

    [Theory]
    [InlineData("file:///C:/secret")]
    [InlineData("data:text/html,hello")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:user@example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("//example.test/path")]
    [InlineData("https://example.test/\nunsafe")]
    public void RejectsExternalSchemesCredentialsAndControlCharacters(string link) =>
        Assert.False(HtmlPreviewPolicy.TryExternalLink(link, out _));

    [Fact]
    public void EncodedAndMalformedMarkupCannotIntroduceAttributesOrActiveTags()
    {
        var document = HtmlPreviewPolicy.Create("<p>&lt;img src=x onerror=alert(1)&gt;</p><a href='jav&#x61;script:alert(1)' style='color:red'>bad</a><div title='\"><iframe src=x>'>end</div><!--<script>x</script>-->");
        Assert.Contains("&lt;img", document.Html);
        Assert.Contains("<a>bad</a>", document.Html);
        Assert.DoesNotContain("<iframe", document.Html);
        Assert.DoesNotContain("<script", document.Html);
        Assert.Empty(document.ExternalLinks);
    }

    [Fact]
    public void EnforcesLengthAndComplexityBudgets()
    {
        Assert.Throws<ArgumentException>(() => HtmlPreviewPolicy.Create(new string('x', HtmlPreviewPolicy.MaximumHtmlCharacters + 1)));
        Assert.Throws<ArgumentException>(() => HtmlPreviewPolicy.Create(string.Concat(Enumerable.Repeat("<b></b>", 10_001))));
    }

    [Fact]
    public void ResolvesBoundedEmbeddedCidImagesOnlyWithinCurrentMessage()
    {
        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var embedded = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["logo@domain"] = new MailEmbeddedImage("logo@domain", "image/png", pngBytes)
        };
        const string html = "<p>Here is an inline logo: <img src=\"cid:logo@domain\" alt=\"Company Logo\" onerror=\"evil()\"></p><p>Missing: <img src=\"cid:external@other\"></p>";
        var document = HtmlPreviewPolicy.Create(html, embedded);
        Assert.True(document.HasEmbeddedImages);
        Assert.Contains("<img src=\"data:image/png;base64,", document.Html);
        Assert.Contains("alt=\"Company Logo\"", document.Html);
        Assert.Contains("style=\"max-width:100%;height:auto\"", document.Html);
        Assert.DoesNotContain("onerror", document.Html);
        Assert.DoesNotContain("evil", document.Html);
        // The missing CID must not produce an <img> tag
        Assert.DoesNotContain("external@other", document.Html);
        Assert.Equal(2, document.Html.Split("<img").Length);
    }

    [Fact]
    public void BlocksRemoteImagesByDefaultAndAllowsThemWhenExplicitlyRequested()
    {
        const string html = "<p>Photo: <img src=\"https://example.test/photo.jpg\" alt=\"Vacation photo\"></p>";
        var blockedDoc = HtmlPreviewPolicy.Create(html, embeddedImages: null, allowRemoteImages: false);
        Assert.DoesNotContain("<img", blockedDoc.Html);
        Assert.Contains("[Remote image blocked: Vacation photo]", blockedDoc.Html);
        Assert.Contains("img-src 'self' data:;", blockedDoc.Html);
        Assert.Equal("https://example.test/photo.jpg", Assert.Single(blockedDoc.RemoteImageUrls));

        var allowedDoc = HtmlPreviewPolicy.Create(html, embeddedImages: null, allowRemoteImages: true);
        Assert.Contains("<img src=\"https://example.test/photo.jpg\" alt=\"Vacation photo\"", allowedDoc.Html);
        Assert.Contains("img-src 'self' data: https: http:;", allowedDoc.Html);
        Assert.Equal("https://example.test/photo.jpg", Assert.Single(allowedDoc.RemoteImageUrls));
    }

    [Fact]
    public async Task MimeDecoderExtractsBoundedEmbeddedImagesFromMultipartRelated()
    {
        byte[] fakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        string base64Png = Convert.ToBase64String(fakePng);
        string raw = "MIME-Version: 1.0\r\nContent-Type: multipart/related; boundary=boundary42\r\n\r\n" +
            "--boundary42\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>Hello <img src=\"cid:myimg@test.test\"></p>\r\n" +
            "--boundary42\r\nContent-Type: image/png\r\nContent-ID: <myimg@test.test>\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            base64Png + "\r\n--boundary42--\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var body = await MessageTextDecoder.DecodeAsync(new(AccountId.New(), "INBOX", 1, 1), stream, CancellationToken.None);
        Assert.Contains("myimg@test.test", body.EmbeddedImages.Keys);
        var image = body.EmbeddedImages["myimg@test.test"];
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(fakePng, image.Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MimeDecoderRetainsBoundedHtmlWithoutRemovingPlainTextFallback(bool oversized)
    {
        string html = oversized ? new string('x', HtmlPreviewPolicy.MaximumHtmlCharacters + 1) : "<p>Hello <b>HTML</b></p>";
        string raw = "MIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=parts\r\n\r\n--parts\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nPlain body\r\n--parts\r\nContent-Type: text/html; charset=utf-8\r\n\r\n" + html + "\r\n--parts--\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var body = await MessageTextDecoder.DecodeAsync(new(AccountId.New(), "INBOX", 1, 1), stream, CancellationToken.None);
        Assert.Contains("Plain body", body.PlainText);
        Assert.False(body.IsHtmlFallback);
        if (oversized) { Assert.Null(body.HtmlText); Assert.Contains("limit", body.HtmlUnavailableReason); }
        else { Assert.Contains("<b>HTML</b>", body.HtmlText); Assert.Null(body.HtmlUnavailableReason); }
    }

    [Fact]
    public void PreservesValidDataUriImagesInHtml()
    {
        string redPixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        string html = $"<p>Here is an inline picture: <img src=\"{redPixel}\" alt=\"Red Dot\"></p>";
        var doc = HtmlPreviewPolicy.Create(html);

        Assert.True(doc.HasEmbeddedImages);
        Assert.Contains("<img src=\"data:image/png;base64,", doc.Html);
        Assert.Contains("alt=\"Red Dot\"", doc.Html);
        Assert.Contains("style=\"max-width:100%;height:auto\"", doc.Html);
    }

    [Theory]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")] // Non-image MIME
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")] // SVG active content
    [InlineData("data:image/png;notbase64,abcd")] // Missing base64
    [InlineData("data:image/png;base64,bad!@#$")] // Invalid characters in base64
    [InlineData("data:image/png;base64,bad&quot;onerror=&quot;alert(1)")] // Encoded breakout attempt
    public void RejectsUnsafeOrMalformedDataUris(string unsafeDataUri)
    {
        string html = $"<p><img src=\"{unsafeDataUri}\" alt=\"Dangerous\"></p>";
        var doc = HtmlPreviewPolicy.Create(html);

        Assert.False(doc.HasEmbeddedImages);
        Assert.DoesNotContain("<img", doc.Html);
        Assert.DoesNotContain("alert(1)", doc.Html);
    }

    [Fact]
    public void AttributeBreakoutAttemptsDoNotLeakActiveHandlers()
    {
        string html = "<p><img src=\"data:image/png;base64,iVBORw0KGgo=\" onerror=\"alert(1)\"></p>";
        var doc = HtmlPreviewPolicy.Create(html);
        Assert.DoesNotContain("onerror", doc.Html);
        Assert.DoesNotContain("alert(1)", doc.Html);
        Assert.Contains("<img src=\"data:image/png;base64,iVBORw0KGgo=\"", doc.Html);
    }

    [Fact]
    public void ResolvesUrlEncodedCidAndFilenameImages()
    {
        byte[] fakeBytes = [1, 2, 3, 4];
        var embedded = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["image@example.com"] = new MailEmbeddedImage("image@example.com", "image/jpeg", fakeBytes),
            ["logo.png"] = new MailEmbeddedImage("logo.png", "image/png", fakeBytes)
        };

        // One URL-encoded CID (%40 for @), one relative filename
        string html = "<p><img src=\"cid:image%40example.com\" alt=\"Encoded CID\"><img src=\"logo.png\" alt=\"Filename Ref\"></p>";
        var doc = HtmlPreviewPolicy.Create(html, embedded);

        Assert.True(doc.HasEmbeddedImages);
        Assert.Contains("<img src=\"data:image/jpeg;base64,", doc.Html);
        Assert.Contains("alt=\"Encoded CID\"", doc.Html);
        Assert.Contains("<img src=\"data:image/png;base64,", doc.Html);
        Assert.Contains("alt=\"Filename Ref\"", doc.Html);
    }

    [Fact]
    public void InlinesDownloadedRemoteImagesWhenAllowed()
    {
        byte[] fakeBytes = [10, 20, 30];
        string remoteUrl = "https://example.test/banner.jpg";
        var downloaded = new Dictionary<string, MailEmbeddedImage>(StringComparer.OrdinalIgnoreCase)
        {
            [remoteUrl] = new MailEmbeddedImage(remoteUrl, "image/jpeg", fakeBytes)
        };

        string html = $"<p><img src=\"{remoteUrl}\" alt=\"Banner\"></p>";
        var doc = HtmlPreviewPolicy.Create(html, downloaded, allowRemoteImages: true);

        Assert.True(doc.HasEmbeddedImages);
        Assert.Contains("<img src=\"data:image/jpeg;base64,", doc.Html);
        Assert.Contains("alt=\"Banner\"", doc.Html);
        Assert.Contains(remoteUrl, doc.RemoteImageUrls);
    }

    [Fact]
    public async Task MimeDecoderExtractsJpegAliasesAndBmpImages()
    {
        byte[] fakeBytes = [0xFF, 0xD8, 0xFF, 0xE0];
        string base64 = Convert.ToBase64String(fakeBytes);
        string raw = "MIME-Version: 1.0\r\nContent-Type: multipart/related; boundary=bound\r\n\r\n" +
            "--bound\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>Test</p>\r\n" +
            "--bound\r\nContent-Type: image/jpg\r\nContent-ID: <jpgtest@example.com>\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            base64 + "\r\n" +
            "--bound\r\nContent-Type: image/bmp\r\nContent-ID: <bmptest@example.com>\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            base64 + "\r\n--bound--\r\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var body = await MessageTextDecoder.DecodeAsync(new(AccountId.New(), "INBOX", 1, 1), stream, CancellationToken.None);

        Assert.Equal(2, body.EmbeddedImages.Count);
        Assert.Contains("jpgtest@example.com", body.EmbeddedImages.Keys);
        Assert.Contains("bmptest@example.com", body.EmbeddedImages.Keys);
        Assert.Equal("image/jpeg", body.EmbeddedImages["jpgtest@example.com"].ContentType);
        Assert.Equal("image/bmp", body.EmbeddedImages["bmptest@example.com"].ContentType);
    }
}
