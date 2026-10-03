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
        var date = texts.Single(text => text.Text.Text == "Sep 27");
        var sender = texts.Single(text => text.Text.Text.StartsWith("A very", StringComparison.Ordinal));
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

        Assert.Equal("Unread, From: Broiler team <hello@example.test>, Subject: Welcome, Received: 9/27/2026 9:30 AM", node.Name);
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
