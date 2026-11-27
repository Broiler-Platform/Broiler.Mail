using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input.Keyboard;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.ViewModels;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

public sealed class ReaderBodyTextTests
{
    private static readonly MailMessageKey Key = SqliteMailCacheTests.Key(new MailboxKey(TestDirectory.Profile().Id, "INBOX", 7), 1);

    [Fact]
    public void OnlyACutPreviewWithLongerRetainedTextCanExpand()
    {
        Assert.True(ReaderBodyText.CanExpand(new MailMessageBody(Key, "abc") { IsTruncated = true, RetainedBodyText = "abcdef" }));
        // Older previews and incomplete MIME keep no retained text.
        Assert.False(ReaderBodyText.CanExpand(new MailMessageBody(Key, "abc") { IsTruncated = true }));
        Assert.False(ReaderBodyText.CanExpand(new MailMessageBody(Key, "abc") { IsTruncated = true, RetainedBodyText = "" }));
        Assert.False(ReaderBodyText.CanExpand(new MailMessageBody(Key, "abcdef") { IsTruncated = false, RetainedBodyText = "abcdef" }));
        Assert.False(ReaderBodyText.CanExpand(new MailMessageBody(Key, "abc") { IsTruncated = true, RetainedBodyText = "abc" }));
        Assert.False(ReaderBodyText.CanExpand(new MailMessageBody(Key, "abc")
        { IsTruncated = true, RetainedBodyText = new string('a', MailMessageBody.MaximumRetainedTextCharacters + 1) }));
    }

    [Fact]
    public void DisplayAndFindTextKeepThePrefixNoteAndLineBreaksAsTheReaderShowsThem()
    {
        var plain = new MailMessageBody(Key, "a\r\nb") { IsTruncated = true, RetainedBodyText = "a\r\nb\rc d" };
        Assert.Equal("a\r\nb" + ReaderBodyText.TruncationNote, ReaderBodyText.Display(plain, false));
        Assert.Equal("a\r\nb\rc d", ReaderBodyText.Display(plain, true));
        Assert.Equal("a\nb", ReaderBodyText.FindText(plain, false));
        Assert.Equal("a\nb\nc d", ReaderBodyText.FindText(plain, true));
        Assert.Equal(0, ReaderBodyText.FindOffset(plain));
        var html = plain with { IsHtmlFallback = true };
        Assert.Equal(ReaderBodyText.HtmlFallbackPrefix + "a\r\nb\rc d", ReaderBodyText.Display(html, true));
        Assert.Equal(ReaderBodyText.HtmlFallbackPrefix.Length, ReaderBodyText.FindOffset(html));
        // Without retained text the full view is still the preview with its note.
        var preview = new MailMessageBody(Key, "short") { IsTruncated = true };
        Assert.Equal(ReaderBodyText.Display(preview, false), ReaderBodyText.Display(preview, true));
        Assert.Equal("short", ReaderBodyText.FindText(preview, true));
        Assert.Equal("short", ReaderBodyText.Display(new MailMessageBody(Key, "short"), false));
    }

    [Fact]
    public void ExpandLabelGivesTheUtf8SizeInWholeKilobytesWithInvariantDigits()
    {
        MailMessageBody Body(string retained) => new(Key, "x") { IsTruncated = true, RetainedBodyText = retained };
        Assert.Equal("Show full message text (2 KB)", ReaderBodyText.ExpandLabel(Body(new string('a', 1500))));
        Assert.Equal("Show full message text (2 KB)", ReaderBodyText.ExpandLabel(Body(new string('ü', 1024))));
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("Show full message text (1,954 KB)", ReaderBodyText.ExpandLabel(Body(new string('ü', 1_000_000))));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
}

[Collection("UI theme")]
public sealed class FullMessageTextTests
{
    private const string Needle = "deepneedle";
    private const int NeedleOffset = 100_000;

    // Lines of 79 letters, with the keyword placed at exactly NeedleOffset.
    private static string RetainedText()
    {
        var chars = new char[150_000];
        for (int i = 0; i < chars.Length; i++) chars[i] = i % 80 == 79 ? '\n' : (char)('a' + (i % 7));
        Needle.CopyTo(0, chars, NeedleOffset, Needle.Length);
        return new string(chars);
    }

    private static MailMessageBody LongBody(MailMessageKey key, string? html = null, bool retained = true)
    {
        string full = RetainedText();
        return new MailMessageBody(key, full[..32_000], html) { IsTruncated = true, RetainedBodyText = retained ? full : null };
    }

    private sealed class Reader : IDisposable
    {
        public required InboxViewModel Model { get; init; }
        public required InboxView View { get; init; }
        public required UiElement Root { get; init; }
        public required UiSession Session { get; init; }
        public required MailboxKey Mailbox { get; init; }
        public required HtmlHost Html { get; init; }
        public ScrollableMessageText Text => All(Root).OfType<ScrollableMessageText>().Single();
        public MessageTextFindBar Find => All(Root).OfType<MessageTextFindBar>().Single();
        public StandardButton? Expand => All(Root).OfType<StandardButton>().SingleOrDefault(button => button.Text.StartsWith("Show full message text (", StringComparison.Ordinal));
        public StandardScrollView Scroll => (StandardScrollView)Text.Children[0];
        public void Dispose() { Root.Dispose(); Session.Dispose(); Model.Dispose(); Html.Dispose(); }
    }

    private static async Task<Reader> OpenAsync(Func<MailMessageKey, MailMessageBody> body, bool html = false)
    {
        var account = TestDirectory.Profile(); var mailbox = new MailboxKey(account.Id, "INBOX", 7);
        var receiver = new TestMailReceiver
        {
            Inbox = (_, _) => Task.FromResult(new MailInboxPage([SqliteMailCacheTests.Summary(mailbox, 1), SqliteMailCacheTests.Summary(mailbox, 2)], null) { Mailbox = mailbox }),
            Body = (key, _) => Task.FromResult(body(key)),
        };
        var model = new InboxViewModel(receiver, new ImmediateUiDispatcher()); model.SetAccount(account); await model.ReceiveAsync();
        var host = new HtmlHost();
        var view = new InboxView(model, html ? host : null); var root = view.CreateContent();
        var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(root);
        await model.SelectAsync(SqliteMailCacheTests.Key(mailbox, 1)); session.RenderFrame();
        return new Reader { Model = model, View = view, Root = root, Session = session, Mailbox = mailbox, Html = host };
    }

    [Fact]
    public async Task ShowFullMessageTextFindsAKeywordAtOffset100000()
    {
        using var reader = await OpenAsync(key => key.Uid == 1 ? LongBody(key) : new MailMessageBody(key, "Short second message"));
        var text = reader.Text;
        Assert.DoesNotContain(Needle, text.Text);
        Assert.Contains("Use Show full message text to read the rest.", reader.Model.Status);
        // The command sits below the preview's end, inside the text's scroll, never in the header.
        var expand = reader.Expand; Assert.NotNull(expand); Assert.True(Shown(expand));
        Assert.True(expand.IsDescendantOf(text));
        int kilobytes = (int)Math.Ceiling(System.Text.Encoding.UTF8.GetByteCount(RetainedText()) / 1024.0);
        Assert.Equal("Show full message text (" + kilobytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " KB)", expand.Text);

        reader.Session.SetFocus(text.Editor);
        Assert.True(reader.View.HandleReaderInput(Key(0x46, KeyboardModifierState.Control))); reader.Session.RenderFrame();
        var bar = reader.Find; Assert.Equal(UiVisibility.Visible, bar.Visibility);
        Query(bar).Text = Needle; bar.Search();
        Assert.Contains("No matches in the 32,000-character preview.", bar.Status);
        Assert.Contains("Show full message text (", bar.Status);

        expand.Click(); reader.Session.RenderFrame();
        Assert.Contains(Needle, text.Text);
        Assert.False(Shown(expand));
        Assert.Same(text.Editor, reader.Session.FocusedElement);
        // The open find bar searched the full text again at once.
        Assert.Contains("Match 1 of 1", bar.Status);
        Assert.Equal(Needle, Selected(text));
        Assert.Equal(NeedleOffset, Caret(text).Start);
        Assert.True(reader.Scroll.VerticalOffset > 0);
        Assert.Same(bar, reader.Find);

        // Another message starts as its own preview with a fresh bar; coming back shows the preview again.
        await reader.Model.SelectAsync(SqliteMailCacheTests.Key(reader.Mailbox, 2)); reader.Session.RenderFrame();
        Assert.True(bar.IsDisposed); Assert.Equal("Short second message", text.Text);
        Assert.Equal("", Query(reader.Find).Text); Assert.False(reader.Expand is { } none && Shown(none));
        await reader.Model.SelectAsync(SqliteMailCacheTests.Key(reader.Mailbox, 1)); reader.Session.RenderFrame();
        Assert.DoesNotContain(Needle, text.Text); Assert.True(Shown(reader.Expand!));
    }

    [Fact]
    public async Task ExpandingWithFindClosedResumesWhereThePreviewEndedAndFindSearchesItAll()
    {
        using var reader = await OpenAsync(key => LongBody(key));
        var text = reader.Text; var expand = reader.Expand!;
        reader.Session.SetFocus(expand);
        expand.Click(); reader.Session.RenderFrame();
        Assert.Same(text.Editor, reader.Session.FocusedElement);
        Assert.Equal(32_000, Caret(text).Start);
        Assert.True(reader.Scroll.VerticalOffset > 0);
        Assert.True(reader.View.HandleReaderInput(Key(0x46, KeyboardModifierState.Control))); reader.Session.RenderFrame();
        var bar = reader.Find; Query(bar).Text = "DEEPNEEDLE"; bar.Search(); reader.Session.RenderFrame();
        Assert.Contains("Match 1 of 1", bar.Status); Assert.Equal(Needle, Selected(text));
    }

    [Fact]
    public async Task FullTextExpansionSurvivesReceivingAgain()
    {
        using var reader = await OpenAsync(key => LongBody(key));
        var body = reader.Model.Body;
        reader.Expand!.Click(); reader.Session.RenderFrame();
        await reader.Model.ReceiveAsync(); reader.Session.RenderFrame();
        Assert.Same(body, reader.Model.Body);
        Assert.Contains(Needle, reader.Text.Text);
        Assert.False(Shown(reader.Expand!));
    }

    [Fact]
    public async Task TheCommandIsHiddenWhileHtmlShowsAndWithoutRetainedText()
    {
        using (var reader = await OpenAsync(key => LongBody(key, "<p>HTML</p>"), html: true))
        {
            Assert.Equal(UiVisibility.Visible, reader.Html.Surface!.Visibility);
            Assert.False(Shown(reader.Expand!));
            // Find switches to the text alternative, which ends with the command.
            reader.Session.SetFocus(reader.Html.Surface);
            Assert.True(reader.View.HandleReaderInput(Key(0x46, KeyboardModifierState.Control))); reader.Session.RenderFrame();
            Assert.True(Shown(reader.Expand!));
        }
        using (var reader = await OpenAsync(key => LongBody(key, retained: false)))
        {
            Assert.False(reader.Expand is { } expand && Shown(expand));
            Assert.DoesNotContain("Show full message text", reader.Model.Status);
            reader.Session.SetFocus(reader.Text.Editor);
            Assert.True(reader.View.HandleReaderInput(Key(0x46, KeyboardModifierState.Control)));
            var bar = reader.Find; Query(bar).Text = Needle; bar.Search();
            Assert.Equal("No matches in this saved text.", bar.Status);
        }
    }

    [Fact]
    public void ReplaceTextDropsOldMatchesAndSearchesAVisibleQueryAgain()
    {
        using var text = new ScrollableMessageText { Text = "Alpha" };
        using var bar = new MessageTextFindBar(text, "Alpha", 0, () => { }, () => true, () => { });
        var host = new Host(); using var session = new StandardUiSessionBuilder().Build(host); session.AddRoot(bar);
        bar.ReplaceText("Hidden"); Assert.Equal("", bar.Status);
        bar.ShowFind(); Query(bar).Text = "Beta"; bar.Search(); Assert.Contains("No matches", bar.Status);
        text.Text = "Alpha Beta";
        bar.ReplaceText("Alpha Beta"); Assert.Contains("Match 1 of 1", bar.Status);
        Button(bar, "Copy match").Click(); Assert.Equal("Beta", host.Clipboard);
        Query(bar).Text = ""; bar.ReplaceText("Alpha Beta Gamma");
        Assert.Equal("Full message text loaded. Press Find to search it.", bar.Status);
        Assert.False(Button(bar, "Copy match").IsEnabled); Assert.False(Button(bar, "Next match").IsEnabled);
    }

    [Fact]
    public void AFooterSitsBelowTheTextInsideTheScrollAndNoneLeavesTheTextAlone()
    {
        using var text = new ScrollableMessageText { Text = string.Join("\n", Enumerable.Range(1, 5).Select(i => "Line " + i)) };
        using var session = new StandardUiSessionBuilder().Build(new Host()); session.AddRoot(text);
        session.RenderFrame();
        var before = text.Editor.Bounds;
        var footer = new StandardButton { Text = "Footer" };
        text.Footer = footer; session.RenderFrame();
        Assert.True(footer.Bounds.Top >= text.Editor.Bounds.Top + text.Editor.DesiredSize.Height);
        Assert.True(footer.Bounds.Height > 0);
        footer.Visibility = UiVisibility.Collapsed; session.RenderFrame();
        Assert.Equal(before, text.Editor.Bounds);
        text.Footer = null; session.RenderFrame();
        Assert.Equal(before, text.Editor.Bounds); Assert.Null(footer.Parent);
        footer.Dispose();
        Assert.False(text.RevealOffset(-1)); Assert.False(text.RevealOffset(text.Text.Length + 1)); Assert.True(text.RevealOffset(3));
        Assert.Equal(3, Caret(text).Start); Assert.Equal(3, Caret(text).End);
    }

    private static bool Shown(UiElement element)
    {
        for (UiElement? current = element; current is not null; current = current.Parent)
            if (current.Visibility != UiVisibility.Visible) return false;
        return true;
    }
    private static (int Start, int End) Caret(ScrollableMessageText text)
    { var metrics = ((IUiTextEditor)text.Editor).GetTextEditorMetrics(); return (metrics.SelectionStart, metrics.SelectionEnd); }
    private static string Selected(ScrollableMessageText text) => text.Editor.Document.Slice(text.Editor.Selection).PlainText;
    private static StandardEdit Query(UiElement root) => All(root).OfType<StandardEdit>().Single(item => item.AccessibleName == "Find in message text");
    private static StandardButton Button(UiElement root, string value) => All(root).OfType<StandardButton>().Single(item => item.Text == value);
    private static IEnumerable<UiElement> All(UiElement root) { yield return root; foreach (var child in root.Children) foreach (var value in All(child)) yield return value; }
    private static UiInputEvent Key(int code, KeyboardModifierState modifiers = KeyboardModifierState.None)
    {
#pragma warning disable CS0618
        return new StandardLegacyGraphicsInputAdapter("full-text").FromKey(new Broiler.Graphics.Windowing.BKeyEventArgs(code, modifiers.HasFlag(KeyboardModifierState.Control), modifiers.HasFlag(KeyboardModifierState.Shift), modifiers.HasFlag(KeyboardModifierState.Alt)), KeyboardKeyTransition.Down);
#pragma warning restore CS0618
    }
    private sealed class Host : IUiHost, IUiClipboardHost
    {
        public string Clipboard = "";
        public bool TryGetText(out string value) { value = Clipboard; return true; }
        public void SetText(string value) => Clipboard = value;
        public BSize ViewportSize => new(1100, 720); public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity); public void Invalidate(UiInvalidation invalidation) { } public void Present(BRenderList list) { }
    }
    private sealed class HtmlHost : IInlineHtmlPreviewHost, IDisposable
    {
        public UiElement? Surface;
        public UiElement CreateInlineContent(MailMessageBody body) => Surface = new StandardButton { Text = "Passive HTML surface" };
        public void Dispose() { }
    }
}
