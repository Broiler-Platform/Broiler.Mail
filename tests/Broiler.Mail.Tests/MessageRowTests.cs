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
    [InlineData("de-DE", "27. Sep", "27.09", "27.09.2025")]
    public void NarrowRowsGetShorterDateFormsFromTheCulture(string culture, string monthDay, string numeric, string older)
    {
        var dates = new MessageDateFormatter(new UtcClock(), CultureInfo.GetCultureInfo(culture));
        var thisYear = dates.ListForms(Now.AddDays(-6).AddHours(-2.5));
        // The abbreviation comes from the platform's culture data; the numeric short date does not differ.
        Assert.Equal(2, thisYear.Count);
        Assert.StartsWith(monthDay, thisYear[0], StringComparison.Ordinal);
        Assert.Equal(numeric, thisYear[1]);
        Assert.Equal([older, "2025"], dates.ListForms(Now.AddYears(-1).AddDays(-6)));
        // Today's time has no shorter form; the row leaves it out instead.
        Assert.Equal([dates.List(Now.AddHours(-1))], dates.ListForms(Now.AddHours(-1)));
        foreach (var timestamp in new[] { Now.AddDays(-6), Now.AddYears(-1), Now })
            Assert.Equal(dates.List(timestamp), dates.ListForms(timestamp)[0]);
    }

    [Theory]
    [InlineData("yyyy. MM. dd.", "09. 27.")]
    [InlineData("dd/MM/yy", "27/09")]
    [InlineData("yyyy-MM-dd", "09-27")]
    // A quoted word or another field never leaves a stray label behind; the abbreviated form stays alone.
    [InlineData("d.MM.yyyy 'г.'", null)]
    [InlineData("gg yyyy/M/d", null)]
    public void TheNumericMonthAndDayDropOnlyTheYearAndItsSeparator(string shortDatePattern, string? expected)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortDatePattern = shortDatePattern;
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

    private static double SubjectSize(BFontStyle font) => Math.Max(10, font.Size - 1);

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
