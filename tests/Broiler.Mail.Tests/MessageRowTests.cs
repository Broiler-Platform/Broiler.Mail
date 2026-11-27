using System.Globalization;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Mail.Application.Views;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Tests;

/// <summary>UI-04: two-line rows stay readable at narrow widths and large text, and semantics keep everything.</summary>
public sealed class MessageRowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageDateFormatter Dates = new(new UtcClock(), CultureInfo.GetCultureInfo("en-US"));

    [Theory]
    [InlineData("Broiler team <hello@example.test>", "Broiler team")]
    [InlineData("\"Doe, Jane\" <jane@example.test>", "Doe, Jane")]
    [InlineData("\"Say \\\"hi\\\"\" <hi@example.test>", "Say \"hi\"")]
    [InlineData("hello@example.test", "hello@example.test")]
    [InlineData("<hello@example.test>", "<hello@example.test>")]
    [InlineData("a@example.test, Bob <b@example.test>", "a@example.test, Bob <b@example.test>")]
    [InlineData("\"A\" <a@example.test>, \"B\" <b@example.test>", "\"A\" <a@example.test>, \"B\" <b@example.test>")]
    public void SenderNameIsTheDisplayNameOfASingleAddressOnly(string sender, string expected) =>
        Assert.Equal(expected, MailMessageItemPresenter.SenderName(sender));

    [Fact]
    public void AWideRowShowsTheFullSenderAndANarrowRowDropsTheAddressFirst()
    {
        const string sender = "Broiler team <hello@example.test>";
        BFontStyle font = BFontStyle.Default;

        Assert.Equal(sender, Rendered(Message(sender, read: false), 600, font).Sender);

        (string narrowSender, string date, string subject) = Rendered(Message(sender, read: false), 400, font with { Size = font.Size * 2 });
        Assert.Equal("Broiler team", narrowSender);
        Assert.Equal("Sep 27", date);
        Assert.Equal("Welcome", subject);

        // A bare address has nothing less important to drop; the row shortens it with an ellipsis.
        Assert.EndsWith("...", Rendered(Message("someone.with.a.long.address@example.test", read: true), 280, font with { Size = font.Size * 2 }).Sender);
    }

    [Fact]
    public void TheSenderAndDateNeverOverlapAtTheNarrowestListWidthAndDoubleText()
    {
        BFontStyle font = BFontStyle.Default with { Size = BFontStyle.Default.Size * 2 };
        var list = new BRenderList();
        Render(list, Message("A very long display name that keeps going <long@example.test>", read: false), 280, font);

        var texts = list.Commands.OfType<BRenderCommand.DrawText>().ToArray();
        // Drawing order: date, sender, subject. How much of the name fits depends on the platform's fonts.
        var date = texts[0];
        var sender = texts[1];
        Assert.Equal("Sep 27", date.Text.Text);
        Assert.StartsWith("A", sender.Text.Text, StringComparison.Ordinal);
        double senderRight = sender.Origin.X + BTextMeasurer.MeasureAdvance(sender.Text.Text, sender.Text.Font);
        Assert.True(senderRight <= date.Origin.X, $"Sender ends at {senderRight}, the date starts at {date.Origin.X}.");
        Assert.True(date.Origin.X + BTextMeasurer.MeasureAdvance(date.Text.Text, date.Text.Font) <= 280, "The date must stay inside the row.");
        Assert.DoesNotContain("<", sender.Text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAccessibleNameCarriesTheFullSenderAndReceivedTime()
    {
        MailMessageSummary message = Message("Broiler team <hello@example.test>", read: false);
        var presenter = new MailMessageItemPresenter(Dates);
        UiSemanticNode node = presenter.CreateSemanticNode(new UiListItemSemanticContext
        {
            Item = Item(message),
            Index = 0,
            Bounds = new BRect(0, 0, 280, 52),
            State = new UiListItemState(IsSelected: true, IsFocused: false, IsRead: false, Index: 0),
        });

        // The time's spacing comes from the platform's culture data (Linux ICU uses a narrow no-break space).
        Assert.Equal($"Unread, From: Broiler team <hello@example.test>, Subject: Welcome, Received: {Dates.Detail(message.ReceivedAt)}", node.Name);
        Assert.StartsWith("9/27/2026 9:30", Dates.Detail(message.ReceivedAt), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "Sep 27")]
    [InlineData("de-DE", "27. Sep")]
    public void ThisYearsDatesUseTheAbbreviatedMonth(string culture, string prefix)
    {
        // The abbreviation itself comes from the platform's culture data ("Sep." or "Sept." in German).
        string date = new MessageDateFormatter(new UtcClock(), CultureInfo.GetCultureInfo(culture)).List(Now.AddDays(-6).AddHours(-2.5));
        Assert.StartsWith(prefix, date, StringComparison.Ordinal);
        Assert.DoesNotContain("September", date, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "Sep 27", "9/27", "9/27/2025")]
    // German writes "27. September", so the month keeps its period: "27.09.".
    [InlineData("de-DE", "27. Sep", "27.09.", "27.09.2025")]
    public void NarrowRowsGetShorterDateFormsFromTheCulture(string culture, string monthDay, string numeric, string older)
    {
        var format = CultureInfo.GetCultureInfo(culture);
        var dates = new MessageDateFormatter(new UtcClock(), format);
        var thisYear = dates.ListForms(Now.AddDays(-6).AddHours(-2.5));
        // The abbreviation comes from the platform's culture data; the numeric short date does not differ.
        Assert.Equal(2, thisYear.Count);
        Assert.StartsWith(monthDay, thisYear[0], StringComparison.Ordinal);
        Assert.Equal(numeric, thisYear[1]);
        var olderYear = dates.ListForms(Now.AddYears(-1).AddDays(-6));
        Assert.Equal([older, "2025"], olderYear);
        // Each form is shorter than the one before it.
        foreach (var forms in new[] { thisYear, olderYear })
            for (int index = 1; index < forms.Count; index++)
                Assert.True(forms[index].Length < forms[index - 1].Length, $"'{forms[index]}' is not shorter than '{forms[index - 1]}'.");
        // Today's time has no shorter form; the row leaves it out instead.
        Assert.Equal(Now.AddHours(-1).ToString("t", format), Assert.Single(dates.ListForms(Now.AddHours(-1))));
    }

    [Theory]
    [InlineData("yyyy. MM. dd.", "MMMM d", "09. 27.")]
    [InlineData("dd/MM/yy", "MMMM d", "27/09")]
    [InlineData("yyyy-MM-dd", "MMMM d", "09-27")]
    // A period after the day or month is an ordinal mark where the language writes "27. September"
    // ("d. MMMM"), and stays without the year; elsewhere ("d MMMM") it only separated the year.
    [InlineData("dd.MM.yyyy", "d. MMMM", "27.09.")]
    [InlineData("d. M. yyyy", "d. MMMM", "27. 9.")]
    [InlineData("d.M.yyyy.", "d. MMMM", "27.9.")]
    [InlineData("dd.MM.yyyy", "d MMMM", "27.09")]
    // A quoted word or another field never leaves a stray label behind; the abbreviated form stays alone.
    [InlineData("d.MM.yyyy 'г.'", "d MMMM", null)]
    [InlineData("gg yyyy/M/d", "MMMM d", null)]
    public void TheNumericMonthAndDayDropOnlyTheYearAndItsSeparator(string shortDatePattern, string monthDayPattern, string? expected)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortDatePattern = shortDatePattern;
        culture.DateTimeFormat.MonthDayPattern = monthDayPattern;
        culture.DateTimeFormat.DateSeparator = "/";
        var forms = new MessageDateFormatter(new UtcClock(), culture).ListForms(Now.AddDays(-6));
        Assert.Equal(expected is null ? 1 : 2, forms.Count);
        if (expected is not null) Assert.Equal(expected, forms[1]);
    }

    public static TheoryData<double, double, int, bool> NarrowRows()
    {
        var data = new TheoryData<double, double, int, bool>();
        foreach (double width in new[] { 120, 150, 168, 200, 240, 268, 280 })
            foreach (double scale in new[] { 1, 2, 2.25 })
                foreach (int age in new[] { 0, 1, 2 })
                    foreach (bool unread in new[] { false, true })
                        data.Add(width, scale, age, unread);
        return data;
    }

    [Theory]
    [MemberData(nameof(NarrowRows))]
    public void TheDateIsShortenedOrLeftOutRatherThanRunPastTheRow(double width, double scale, int age, bool unread)
    {
        // Today, this year, or an older year.
        var received = age switch { 0 => Now.AddHours(-2), 1 => Now.AddDays(-6), _ => Now.AddYears(-1) };
        var message = Message("Broiler team <hello@example.test>", read: !unread) with { ReceivedAt = received };
        BFontStyle font = BFontStyle.Default with { Size = BFontStyle.Default.Size * scale };
        var list = new BRenderList();
        Render(list, message, width, font);

        var line1 = list.Commands.OfType<BRenderCommand.DrawText>().Where(text => text.Text.Font.Size != SubjectSize(font)).ToArray();
        var date = line1.SingleOrDefault(text => text.Text.Font.Size < font.Size);
        var sender = line1.SingleOrDefault(text => text.Text.Font.Size == font.Size);
        if (date is not null)
        {
            Assert.Contains(date.Text.Text, Dates.ListForms(received));
            double dateRight = date.Origin.X + BTextMeasurer.MeasureAdvance(date.Text.Text, date.Text.Font);
            Assert.True(dateRight <= width - 8 + 0.5, $"The date ends at {dateRight} in a {width} DIP row.");
            Assert.NotNull(sender);
        }
        if (sender is not null && date is not null)
        {
            double senderRight = sender.Origin.X + BTextMeasurer.MeasureAdvance(sender.Text.Text, sender.Text.Font);
            Assert.True(senderRight <= date.Origin.X, $"The sender ends at {senderRight}, the date starts at {date.Origin.X}.");
        }
        // From the narrowest list row up, the sender keeps at least the start of its name.
        if (width >= 168)
            Assert.StartsWith("Broi", sender?.Text.Text ?? "", StringComparison.Ordinal);

        // The drawn form is the longest that leaves the sender "Broi..." beside it (line 1: 10 DIP
        // inset, the unread dot, 8 DIP to the edge, 10 DIP before the date in a font 2 DIP smaller).
        var forms = Dates.ListForms(received).ToList();
        double space = width - 10 - (unread ? 12 : 0) - 8;
        double needed = Math.Max(BTextMeasurer.MeasureAdvance("Broi...", unread ? font with { Weight = BFontWeight.Bold } : font), 10);
        bool Fits(string form) => space - BTextMeasurer.MeasureAdvance(form, font with { Size = Math.Max(9, font.Size - 2) }) - 10 >= needed;
        int drawn = date is null ? forms.Count : forms.IndexOf(date.Text.Text);
        for (int longer = 0; longer < drawn; longer++)
            Assert.False(Fits(forms[longer]), $"'{forms[longer]}' fits in a {width} DIP row, but '{date?.Text.Text}' was drawn.");
        if (date is not null) Assert.True(Fits(date.Text.Text));
        // At ordinary text size, a row this wide shows the full date.
        if (scale == 1 && width >= 200)
            Assert.Equal(forms[0], date?.Text.Text);
    }

    [Fact]
    public void AtTheNarrowestListAndLargestTextAnOlderDateShortensAndKeepsTheSender()
    {
        // The split list's minimum (280 DIP) less its scrollbar, at 225 % text.
        BFontStyle font = BFontStyle.Default with { Size = BFontStyle.Default.Size * 2.25 };
        var message = Message("Broiler team <hello@example.test>", read: false) with { ReceivedAt = Now.AddYears(-1).AddDays(-6) };
        (string sender, string date, _) = Rendered(message, 268, font);

        Assert.NotEqual(Dates.List(message.ReceivedAt), date);
        Assert.Equal("2025", date);
        Assert.StartsWith("Broi", sender, StringComparison.Ordinal);
        UiSemanticNode node = new MailMessageItemPresenter(Dates).CreateSemanticNode(new UiListItemSemanticContext
        {
            Item = Item(message), Index = 0, Bounds = new BRect(0, 0, 268, 80),
            State = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: false, Index: 0),
        });
        Assert.EndsWith($"Received: {Dates.Detail(message.ReceivedAt)}", node.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ASelectedRowDrawsItsTextInTheSelectionTextColors()
    {
        // A system contrast palette draws the selection in the highlight pair, whose text differs from Text.
        var selectionText = BColor.FromArgb(0xFF, 0x26, 0x3B, 0x50);
        var palette = StandardThemeTokens.HighContrastDark with { SelectionText = selectionText, SelectionTextMuted = selectionText };
        Assert.NotEqual(palette.Text, palette.SelectionText);
        var selected = Message("Broiler team <hello@example.test>", read: false);
        var other = Message("Someone else <someone@example.test>", read: true) with { Key = selected.Key with { Uid = 2 }, Subject = "Other", ReceivedAt = new DateTimeOffset(2026, 9, 20, 9, 30, 0, TimeSpan.Zero) };
        var list = new StandardListView { ItemPresenter = new MailMessageItemPresenter(Dates) };
        list.ApplyTheme(palette);
        list.SetItems([Item(selected), Item(other)]);
        list.SelectIndex(0);
        using var session = new StandardUiSessionBuilder().Build(new Host());
        session.AddRoot(list);

        var texts = session.RenderFrame().Commands.OfType<BRenderCommand.DrawText>().ToArray();
        // The selected row's sender, date and subject, and the other row's sender, which stays in Text.
        string[] selectedTexts = ["Broiler team <hello@example.test>", "Sep 27", "Welcome"];
        foreach (string text in selectedTexts)
            Assert.Equal(selectionText, Assert.Single(texts, draw => draw.Text.Text == text).Text.Color);
        Assert.Equal(palette.Text, Assert.Single(texts, draw => draw.Text.Text == "Someone else <someone@example.test>").Text.Color);
    }

    /// <summary>
    /// Dark's accent is a fill, too close to its selection fill for the unread dot (2.76:1, where a mark needs
    /// 3:1), so a selected unread row draws the dot in the theme's accent text, the list's shade of the accent
    /// for marks; the message row hands it on to the two-line row it draws. An unselected row keeps the accent.
    /// </summary>
    [Fact]
    public void ASelectedUnreadRowInDarkDrawsItsDotInTheAccentText()
    {
        var palette = StandardThemeTokens.Dark;
        Assert.NotEqual(palette.Accent, palette.AccentText);
        var selected = Message("Broiler team <hello@example.test>", read: false);
        var other = Message("Someone else <someone@example.test>", read: false) with { Key = selected.Key with { Uid = 2 }, Subject = "Other" };
        var list = new StandardListView { ItemPresenter = new MailMessageItemPresenter(Dates) };
        list.ApplyTheme(palette);
        list.SetItems([Item(selected), Item(other)]);
        list.SelectIndex(0);
        using var session = new StandardUiSessionBuilder().Build(new Host());
        session.AddRoot(list);

        // The dot is a 6 DIP square before the sender, one per unread row, in row order.
        var dots = session.RenderFrame().Commands.OfType<BRenderCommand.FillRect>()
            .Where(fill => fill.Rect.Width == 6 && fill.Rect.Height == 6).OrderBy(fill => fill.Rect.Top).ToArray();
        Assert.Equal(2, dots.Length);
        Assert.Equal(palette.AccentText, dots[0].Color);
        Assert.True(StandardContrast.Ratio(dots[0].Color, list.SelectedBackground) >= StandardContrast.AaLargeOrUi,
            $"The dot is {dots[0].Color} on {list.SelectedBackground}.");
        Assert.Equal(palette.Accent, dots[1].Color);
    }

    [Theory]
    [InlineData(false, "Unread, Has attachments, From: Broiler team <hello@example.test>, Subject: Welcome, Received: ")]
    [InlineData(true, "Starred, Unread, Has attachments, From: Broiler team <hello@example.test>, Subject: Welcome, Received: ")]
    public void TheAccessibleNameSaysHasAttachmentsBeforeTheSender(bool starred, string expected)
    {
        MailMessageSummary message = Message("Broiler team <hello@example.test>", read: false) with { HasAttachments = true, IsStarred = starred };
        UiSemanticNode node = new MailMessageItemPresenter(Dates).CreateSemanticNode(new UiListItemSemanticContext
        {
            Item = Item(message), Index = 0, Bounds = new BRect(0, 0, 280, 52),
            State = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: false, Index: 0),
        });
        Assert.Equal(expected + Dates.Detail(message.ReceivedAt), node.Name);
        // Without the mark the name says nothing about attachments.
        UiSemanticNode plain = new MailMessageItemPresenter(Dates).CreateSemanticNode(new UiListItemSemanticContext
        {
            Item = Item(message with { HasAttachments = false }), Index = 0, Bounds = new BRect(0, 0, 280, 52),
            State = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: false, Index: 0),
        });
        Assert.DoesNotContain("attachments", plain.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAttachmentBadgeIsDrawnBetweenTheSenderAndTheDate()
    {
        var list = new BRenderList();
        Render(list, Message("Broiler team <hello@example.test>", read: false) with { HasAttachments = true }, 600, BFontStyle.Default);

        // The texts keep their order (date, sender, subject); the badge is drawn after them.
        var texts = list.Commands.OfType<BRenderCommand.DrawText>().ToArray();
        Assert.Equal(["Sep 27", "Broiler team <hello@example.test>", "Welcome"], texts.Select(text => text.Text.Text));
        var badge = Badge(list);
        Assert.Equal(2, list.Commands.OfType<BRenderCommand.StrokeRoundedRect>().Count());
        var commands = list.Commands.ToList();
        Assert.True(commands.FindIndex(command => command is BRenderCommand.StrokeRoundedRect) > commands.IndexOf(texts[2]));
        double senderRight = texts[1].Origin.X + BTextMeasurer.MeasureAdvance(texts[1].Text.Text, texts[1].Text.Font);
        Assert.True(badge.Left >= senderRight + 6 - 0.5, $"The badge starts at {badge.Left}, the sender ends at {senderRight}.");
        Assert.True(badge.Right <= texts[0].Origin.X - 10 + 0.5, $"The badge ends at {badge.Right}, the date starts at {texts[0].Origin.X}.");
        // On line 1, inside the sender's line.
        Assert.True(badge.Top >= texts[1].Origin.Y && badge.Bottom <= texts[1].Origin.Y + BTextMeasurer.GetLineHeight(BFontStyle.Default) + 0.5);
    }

    public static TheoryData<double, double, bool> BadgeRows()
    {
        var data = new TheoryData<double, double, bool>();
        foreach (double width in new[] { 160, 220, 280, 320, 480 })
            foreach (double scale in new[] { 1, 2 })
                foreach (bool unread in new[] { false, true })
                    data.Add(width, scale, unread);
        return data;
    }

    [Theory]
    [MemberData(nameof(BadgeRows))]
    public void TheBadgeNeverOverlapsTheSenderOrDate(double width, double scale, bool unread)
    {
        BFontStyle font = BFontStyle.Default with { Size = BFontStyle.Default.Size * scale };
        var message = Message("A very long display name that keeps going <long@example.test>", read: !unread) with { HasAttachments = true };
        var list = new BRenderList();
        Render(list, message, width, font);

        var line1 = list.Commands.OfType<BRenderCommand.DrawText>().Where(text => text.Text.Font.Size != SubjectSize(font)).ToArray();
        var date = line1.SingleOrDefault(text => text.Text.Font.Size < font.Size);
        var sender = Assert.Single(line1, text => text.Text.Font.Size == font.Size);
        var badge = Badge(list);
        double senderRight = sender.Origin.X + BTextMeasurer.MeasureAdvance(sender.Text.Text, sender.Text.Font);
        Assert.StartsWith("A", sender.Text.Text, StringComparison.Ordinal);
        Assert.True(badge.Left >= senderRight + 6 - 0.5, $"The badge starts at {badge.Left}, the sender ends at {senderRight}.");
        Assert.True(badge.Left >= 10 + (unread ? 12 : 0), "The badge stays after the unread dot.");
        if (date is not null)
            Assert.True(badge.Right <= date.Origin.X - 10 + 0.5, $"The badge ends at {badge.Right}, the date starts at {date.Origin.X}.");
        else
            Assert.True(badge.Right <= width - 8 + 0.5, $"The badge ends at {badge.Right} in a {width} DIP row.");
        // The date only takes a form that still leaves the badge and the sender's first characters room.
        if (date is not null)
        {
            double dateRight = date.Origin.X + BTextMeasurer.MeasureAdvance(date.Text.Text, date.Text.Font);
            Assert.True(dateRight <= width - 8 + 0.5);
        }
    }

    [Fact]
    public void ASelectedRowDrawsTheBadgeInTheSelectionSecondaryColor()
    {
        var secondary = BColor.FromArgb(0xFF, 0x60, 0x60, 0x60);
        var selectedSecondary = BColor.FromArgb(0xFF, 0x10, 0x20, 0x30);
        var message = Message("Broiler team <hello@example.test>", read: true) with { HasAttachments = true };
        var unselected = new BRenderList();
        new MailMessageItemPresenter(Dates).Render(BadgeContext(unselected, message, selected: false, highContrast: false, secondary, selectedSecondary));
        Assert.All(unselected.Commands.OfType<BRenderCommand.StrokeRoundedRect>(), stroke => Assert.Equal(secondary, stroke.Color));
        var selected = new BRenderList();
        new MailMessageItemPresenter(Dates).Render(BadgeContext(selected, message, selected: true, highContrast: false, secondary, selectedSecondary));
        var strokes = selected.Commands.OfType<BRenderCommand.StrokeRoundedRect>().ToArray();
        Assert.Equal(2, strokes.Length);
        Assert.All(strokes, stroke => Assert.Equal(selectedSecondary, stroke.Color));
    }

    [Fact]
    public void HighContrastDrawsTheBadgeInTheTextColor()
    {
        var message = Message("Broiler team <hello@example.test>", read: true) with { HasAttachments = true };
        var list = new BRenderList();
        new MailMessageItemPresenter(Dates).Render(BadgeContext(list, message, selected: false, highContrast: true,
            BColor.FromArgb(0xFF, 0x60, 0x60, 0x60), BColor.FromArgb(0xFF, 0x10, 0x20, 0x30)));
        var sender = Assert.Single(list.Commands.OfType<BRenderCommand.DrawText>(), text => text.Text.Text == "Broiler team <hello@example.test>");
        var strokes = list.Commands.OfType<BRenderCommand.StrokeRoundedRect>().ToArray();
        Assert.Equal(2, strokes.Length);
        Assert.All(strokes, stroke => Assert.Equal(sender.Text.Color, stroke.Color));
    }

    [Fact]
    public void RowsWithoutAttachmentsDrawNoBadge()
    {
        var list = new BRenderList();
        Render(list, Message("Broiler team <hello@example.test>", read: false), 600, BFontStyle.Default);
        Assert.Empty(list.Commands.OfType<BRenderCommand.StrokeRoundedRect>());
    }

    [Fact]
    public void TogglingTheAttachmentMarkGivesANewRow()
    {
        var presenter = new MailMessageItemPresenter(Dates);
        var message = Message("Broiler team <hello@example.test>", read: true);
        int Strokes(MailMessageSummary row)
        {
            var list = new BRenderList();
            presenter.Render(Context(list, row, 600, BFontStyle.Default, true));
            return list.Commands.OfType<BRenderCommand.StrokeRoundedRect>().Count();
        }
        Assert.Equal(0, Strokes(message));
        var marked = message with { HasAttachments = true };
        Assert.Equal(2, Strokes(marked));
        Assert.Equal(0, Strokes(marked with { HasAttachments = false }));
        Assert.Equal(2, Strokes(marked));
    }

    // The outer outline of the paperclip, the first of its two strokes.
    private static BRect Badge(BRenderList list) => list.Commands.OfType<BRenderCommand.StrokeRoundedRect>().First().Rect;

    private static UiListItemRenderContext BadgeContext(BRenderList list, MailMessageSummary message, bool selected, bool highContrast, BColor secondary, BColor selectedSecondary) => new()
    {
        RenderList = list,
        Bounds = new BRect(0, 0, 600, 120),
        Item = Item(message),
        State = new UiListItemState(IsSelected: selected, IsFocused: false, IsRead: message.IsRead, Index: 0),
        Font = BFontStyle.Default,
        Foreground = BColor.Black,
        SecondaryForeground = secondary,
        SelectedSecondaryForeground = selectedSecondary,
        Background = BColor.White,
        SelectedBackground = BColor.White,
        FocusRing = BColor.Black,
        Accent = BColor.Black,
        IsHighContrast = highContrast,
    };

    private static double SubjectSize(BFontStyle font) => Math.Max(10, font.Size - 1);

    private sealed class Host : IUiHost
    {
        public BSize ViewportSize => new(600, 200);
        public double Scale => 1;
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        public void Invalidate(UiInvalidation invalidation) { }
        public void Present(BRenderList renderList) { }
    }

    [Fact]
    public void ARowDrawnAgainIsTheSameAndFollowsItsMessageWidthReadStateAndDay()
    {
        var clock = new MovableClock { Now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero) };
        var presenter = new MailMessageItemPresenter(new MessageDateFormatter(clock, CultureInfo.GetCultureInfo("en-US")));
        BFontStyle font = BFontStyle.Default;
        (string Sender, string Date, string Subject) Draw(MailMessageSummary message, double width, BFontStyle? size = null)
        {
            var list = new BRenderList();
            presenter.Render(Context(list, message, width, size ?? font, message.IsRead));
            string[] texts = list.Commands.OfType<BRenderCommand.DrawText>().Select(text => text.Text.Text).ToArray();
            return (texts[1], texts[0], texts[2]);
        }
        const string sender = "Broiler team <hello@example.test>";
        var message = Message(sender, read: false);

        var first = Draw(message, 600);
        Assert.Equal(first, Draw(message, 600));
        // Today a row shows the time, in the culture's own spacing.
        string time = new DateTime(2026, 9, 27, 9, 30, 0).ToString("t", CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal((sender, time, "Welcome"), first);
        // A narrower row with larger text drops the address again rather than reuse the wide row's text.
        Assert.Equal("Broiler team", Draw(message, 400, font with { Size = font.Size * 2 }).Sender);
        Assert.Equal(sender, Draw(message, 600).Sender);
        // Read is drawn without the unread emphasis, from the row's own state.
        var read = new BRenderList();
        presenter.Render(Context(read, message, 600, font, read: true));
        var unread = new BRenderList();
        presenter.Render(Context(unread, message, 600, font, read: false));
        Assert.NotEqual(Fonts(read), Fonts(unread));
        // A changed message is a new instance and is drawn from its own members.
        Assert.Equal("Agenda", Draw(message with { Subject = "Agenda" }, 600).Subject);
        Assert.Equal("★ Welcome", Draw(message with { IsStarred = true }, 600).Subject);
        // The next day the same message shows its day instead of its time.
        clock.Now = clock.Now.AddDays(1);
        Assert.Equal("Sep 27", Draw(message, 600).Date);
    }

    [Fact]
    public void ManyWidthsDoNotGrowTheRowCacheWithoutBound()
    {
        var presenter = new MailMessageItemPresenter(Dates);
        var message = Message("Broiler team <hello@example.test>", read: true);
        // A long resize draws the row at every width; the drawn text stays right past the cache's limit.
        for (int width = 200; width < 1400; width++)
            presenter.Render(Context(new BRenderList(), message, width, BFontStyle.Default, true));
        var list = new BRenderList();
        presenter.Render(Context(list, message, 600, BFontStyle.Default, true));
        Assert.Contains(list.Commands.OfType<BRenderCommand.DrawText>(), text => text.Text.Text == "Broiler team <hello@example.test>");
    }

    private static UiListItemRenderContext Context(BRenderList list, MailMessageSummary message, double width, BFontStyle font, bool read) => new()
    {
        RenderList = list,
        Bounds = new BRect(0, 0, width, 120),
        Item = Item(message),
        State = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: read, Index: 0),
        Font = font,
        Foreground = BColor.Black,
        SecondaryForeground = BColor.Black,
        Background = BColor.White,
        SelectedBackground = BColor.White,
        FocusRing = BColor.Black,
        Accent = BColor.Black,
    };

    private static string Fonts(BRenderList list) =>
        string.Join("|", list.Commands.OfType<BRenderCommand.DrawText>().Select(text => text.Text.Font.ToString()));

    private sealed class MovableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (string Sender, string Date, string Subject) Rendered(MailMessageSummary message, double width, BFontStyle font)
    {
        var list = new BRenderList();
        Render(list, message, width, font);
        string[] texts = list.Commands.OfType<BRenderCommand.DrawText>().Select(text => text.Text.Text).ToArray();
        // Drawing order: date, sender, subject.
        return (texts[1], texts[0], texts[2]);
    }

    private static void Render(BRenderList list, MailMessageSummary message, double width, BFontStyle font) =>
        new MailMessageItemPresenter(Dates).Render(new UiListItemRenderContext
        {
            RenderList = list,
            Bounds = new BRect(0, 0, width, 120),
            Item = Item(message),
            State = new UiListItemState(IsSelected: false, IsFocused: false, IsRead: message.IsRead, Index: 0),
            Font = font,
            Foreground = BColor.Black,
            SecondaryForeground = BColor.Black,
            Background = BColor.White,
            SelectedBackground = BColor.White,
            FocusRing = BColor.Black,
            Accent = BColor.Black,
        });

    private static UiListItem Item(MailMessageSummary message) => new(message.Key.ToString(), message.Sender, message.Subject, null, message.IsRead, message);

    private static MailMessageSummary Message(string sender, bool read) => new()
    {
        Key = new MailMessageKey(new AccountId(Guid.Parse("00000000-0000-0000-0000-000000000001")), "INBOX", 7, 1),
        Sender = sender,
        Subject = "Welcome",
        ReceivedAt = new DateTimeOffset(2026, 9, 27, 9, 30, 0, TimeSpan.Zero),
        IsRead = read,
    };

    private sealed class UtcClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
