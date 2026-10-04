using Broiler.Graphics.Text;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RichEdit.Standard;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// UI-10: wheel and character messages posted to the real render window, as Windows delivers them, reach the
/// inbox and the composer exactly once and in proportion. Posted messages carry no modifier state and involve
/// no real layout, touchpad, or IME, so the physical checks stay manual.
/// </summary>
public sealed class NativeInputFidelityTests
{
    private const uint WmChar = 0x0102;
    private const uint WmDeadChar = 0x0103;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmMouseHWheel = 0x020E;
    private const string UnbrokenAddress = "https://example.test/a/very/long/path/without/any/natural/break/opportunities/whatsoever/index.html";

    [Fact]
    public void QuarterNotchWheelStepsScrollTheListAndTheReaderInProportionAndAddUpToOneNotch()
    {
        using var fixture = OpenFixture(DemoScenario.Inbox);
        var list = Find<StandardListView>(fixture);
        var reader = Find<ScrollableMessageText>(fixture).Children.OfType<StandardScrollView>().Single();

        // A notch scrolls the list by its wheel rows and the reader by its line step. Exact distances also show
        // that the wheel arrives once: Hosting's bridge and the legacy Graphics callback must not both deliver it.
        AssertQuarterNotchesAddUp(fixture, list, () => list.VerticalOffset, fixture.Ui(() => list.EffectiveItemHeight * list.WheelScrollItems));
        AssertQuarterNotchesAddUp(fixture, reader, () => reader.VerticalOffset, fixture.Ui(() => reader.LineScrollAmount));
    }

    [Fact]
    public void AHorizontalWheelOverTheReaderScrollsNothingBecauseEvenAnUnbrokenLineWrapsAtLargeText()
    {
        using var fixture = OpenFixture(DemoScenario.LongMessage);
        var text = Find<ScrollableMessageText>(fixture);
        // The largest text size Windows offers, as the system setting would apply it.
        var theme = StandardThemeTokens.Light.WithTextScale(2.25);
        fixture.Ui(() => fixture.Window.ApplyThemeForMeasurement(theme));
        fixture.Layout();
        var reader = text.Children.OfType<StandardScrollView>().Single();
        var header = fixture.Ui(() => HiddenMailWindow.Descendants(fixture.Window.Shell.Window).OfType<BoundedScrollArea>()
            .Single(area => area.Scroll.AccessibleName == "Message header").Scroll);
        var list = Find<StandardListView>(fixture);
        var editor = text.Editor;

        Assert.Contains(UnbrokenAddress, fixture.Ui(() => text.Text));
        // The address is far wider than the reading column, so it wraps inside itself; nothing is left to the side.
        Assert.True(BTextMeasurer.MeasureAdvance(UnbrokenAddress, theme.FontBody) > 2 * fixture.Ui(() => editor.Bounds.Width));
        Assert.Equal(fixture.Ui(() => reader.ViewportSize.Width), fixture.Ui(() => reader.ExtentSize.Width), 3);
        Assert.Equal(fixture.Ui(() => header.ViewportSize.Width), fixture.Ui(() => header.ExtentSize.Width), 3);

        // The reader cannot use a horizontal wheel, and no container around it can either. Scroll the text down
        // first, so a horizontal wheel turned into a vertical one would show.
        fixture.Post(WmMouseWheel, Wheel(-120), Center(fixture, reader));
        fixture.Settle();
        var before = Offsets();
        Assert.True(before.ReaderY > 0);
        foreach (var target in new UiElement[] { reader, header })
            foreach (int delta in new[] { 120, -120, 30 })
            {
                fixture.Post(WmMouseHWheel, Wheel(delta), Center(fixture, target));
                fixture.Settle();
                Assert.Equal(before, Offsets());
            }

        (double ReaderX, double ReaderY, double EditorX, double HeaderX, double HeaderY, double List) Offsets() => fixture.Ui(() =>
            (reader.HorizontalOffset, reader.VerticalOffset, editor.HorizontalScrollOffset, header.HorizontalOffset, header.VerticalOffset, list.VerticalOffset));
    }

    [Fact]
    public void ADeadKeyTypesOnlyItsComposedCharacterInTheToFieldAndTheBody()
    {
        using var fixture = HiddenMailWindow.Start();
        var (to, body) = StartDraft(fixture);

        foreach (UiElement field in new UiElement[] { to, body })
        {
            fixture.Ui(() => fixture.Window.Session.SetFocus(field));
            // ^ then e composes ê. ´ then Space, and ^ then Space, give the accent itself. ^ then x has no
            // composed form: Windows sends the accent and the letter as two characters.
            foreach (var (dead, typed) in new[] { ('^', "ê"), ('´', "´"), ('^', "^"), ('^', "^x") })
            {
                fixture.Post(WmDeadChar, dead, 1);
                foreach (char character in typed) fixture.Post(WmChar, character, 1);
            }
            fixture.Settle();
        }

        Assert.Equal("ê´^^x", fixture.Ui(() => to.Text));
        Assert.Equal("ê´^^x", fixture.Ui(() => body.GetPlainText()));
        Assert.Equal("ê´^^x", fixture.Ui(() => fixture.Window.Model.Composer.To));
        Assert.Equal("ê´^^x", fixture.Ui(() => fixture.Window.Model.Composer.PlainText));
        Assert.False(fixture.Ui(() => fixture.Window.InputBridge!.DeadKeyActive));
    }

    [Fact]
    public void SurrogatePairsAndTheCharactersAroundAndBetweenThemAreTypedExactlyOnce()
    {
        using var fixture = HiddenMailWindow.Start();
        var (to, body) = StartDraft(fixture);

        foreach (UiElement field in new UiElement[] { to, body })
        {
            fixture.Ui(() => fixture.Window.Session.SetFocus(field));
            // Two pairs with a BMP character between them, each UTF-16 unit in its own WM_CHAR.
            fixture.Type("😀a🎉");
            // A BMP character between the halves of a pair: the halves cannot form a character, so only the
            // BMP character is typed, and no lone surrogate reaches the draft.
            fixture.Type("\uD83Db\uDE00");
        }

        Assert.Equal("😀a🎉b", fixture.Ui(() => to.Text));
        Assert.Equal("😀a🎉b", fixture.Ui(() => body.GetPlainText()));
        Assert.Equal("😀a🎉b", fixture.Ui(() => fixture.Window.Model.Composer.To));
        Assert.Equal("😀a🎉b", fixture.Ui(() => fixture.Window.Model.Composer.PlainText));
    }

    private static HiddenMailWindow OpenFixture(DemoScenario scenario)
    {
        var options = new DemoOptions(scenario);
        var fixture = HiddenMailWindow.Start(() => DemoApplication.Create(options), options);
        try
        {
            var inbox = fixture.Window.Model.Inbox;
            fixture.WaitUntil(() => inbox.Body?.Key.Uid == 55 && !inbox.IsBusy, "the newest message is open");
            fixture.Layout();
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>Starts a new message, which focuses To, and returns To and the body.</summary>
    internal static (StandardEdit To, StandardRichEdit Body) StartDraft(HiddenMailWindow fixture)
    {
        Assert.True(fixture.Ui(() => fixture.Window.Model.Compose.StartNew()));
        fixture.Layout();
        var compose = fixture.Ui(() => fixture.Window.Shell.Navigation.Tabs.Single(tab => tab.Id == "compose").Content!);
        var to = fixture.Ui(() => (StandardEdit)HiddenMailWindow.Descendants(compose).OfType<StandardLabel>().Single(label => label.Text == "To").Target!);
        var body = fixture.Ui(() => HiddenMailWindow.Descendants(compose).OfType<StandardRichEdit>().Single());
        Assert.Same(to, fixture.Ui(() => fixture.Window.Session.FocusedElement));
        return (to, body);
    }

    private static void AssertQuarterNotchesAddUp(HiddenMailWindow fixture, UiElement target, Func<double> offset, double notch)
    {
        nint over = Center(fixture, target);
        Assert.Equal(0, fixture.Ui(offset));
        var steps = new List<double>();
        for (int i = 0; i < 4; i++)
        {
            // A precision touchpad reports a fraction of a notch, here a quarter towards the user.
            fixture.Post(WmMouseWheel, Wheel(-30), over);
            fixture.Settle();
            steps.Add(fixture.Ui(offset));
        }
        // Each quarter scrolls at once, by a quarter of a notch, rather than waiting for a whole notch.
        for (int i = 0; i < steps.Count; i++) Assert.Equal(notch * (i + 1) / 4, steps[i], 6);

        // One whole notch moves exactly as far as the four quarters did, and back.
        fixture.Post(WmMouseWheel, Wheel(-120), over);
        fixture.Settle();
        Assert.Equal(2 * notch, fixture.Ui(offset), 6);
        fixture.Post(WmMouseWheel, Wheel(120), over);
        fixture.Settle();
        Assert.Equal(notch, fixture.Ui(offset), 6);
    }

    private static T Find<T>(HiddenMailWindow fixture) where T : UiElement =>
        fixture.Ui(() => HiddenMailWindow.Descendants(fixture.Window.Shell.Window).OfType<T>().Single());

    private static nint Center(HiddenMailWindow fixture, UiElement element)
    {
        var bounds = fixture.Ui(() => element.Bounds);
        Assert.False(bounds.IsEmpty);
        return fixture.ScreenLParam(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    /// <summary>The wParam of a wheel message: the signed delta in the high word, no buttons or keys.</summary>
    private static nint Wheel(int delta) => (nint)((delta & 0xFFFF) << 16);
}
