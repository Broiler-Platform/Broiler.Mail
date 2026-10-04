using Broiler.Mail.Windows.Preview;

namespace Broiler.Mail.Windows.Tests;

/// <summary>UI-11: the preview's zoom levels and the Ctrl+wheel step counter.</summary>
public sealed class PreviewZoomTests
{
    [Fact]
    public void LevelsRunFromHalfToThreeTimesAndIncludeTheSystemTextSize()
    {
        Assert.Equal([0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0], PreviewZoom.Levels(1.0));
        // A text size that is not a step becomes one, so stepping passes through it.
        Assert.Equal([0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.15, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0], PreviewZoom.Levels(1.15));
        Assert.Equal(1.15, PreviewZoom.Next(1.1, 1, 1.15));
        Assert.Equal(1.25, PreviewZoom.Next(1.15, 1, 1.15));
        Assert.Equal(1.1, PreviewZoom.Next(1.15, -1, 1.15));
        Assert.Equal(PreviewZoom.Levels(2.25), PreviewZoom.Levels(2.25).Order());
        Assert.Contains(2.25, PreviewZoom.Levels(2.25));
    }

    [Theory]
    [InlineData(1.0, 1, 1.1)]
    [InlineData(1.0, -1, 0.9)]
    [InlineData(0.67, -1, 0.5)]
    [InlineData(2.5, 1, 3.0)]
    [InlineData(3.0, 1, 3.0)] // the limits hold
    [InlineData(0.5, -1, 0.5)]
    [InlineData(1.2, 1, 1.25)] // a level between steps moves to the next one
    [InlineData(1.2, -1, 1.1)]
    [InlineData(9.0, 1, 3.0)]
    public void NextStepsOneLevelAndStopsAtTheLimits(double current, int direction, double expected) =>
        Assert.Equal(expected, PreviewZoom.Next(current, direction, 1.0));

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.1, 0.5)]
    [InlineData(-2, 0.5)]
    [InlineData(4.0, 3.0)]
    [InlineData(1.5, 1.5)]
    public void ClampKeepsAUsableZoom(double zoom, double expected) => Assert.Equal(expected, PreviewZoom.Clamp(zoom));

    [Theory]
    [InlineData(0.67, "67 %")]
    [InlineData(1.0, "100 %")]
    [InlineData(1.15, "115 %")]
    [InlineData(2.25, "225 %")]
    [InlineData(3.0, "300 %")]
    public void LevelsShowAsWholePercentages(double zoom, string expected) => Assert.Equal(expected, PreviewZoom.Format(zoom));

    [Fact]
    public void WheelFractionsAddUpToStepsAndATurnTheOtherWayStartsAgain()
    {
        var wheel = new PreviewZoomWheel();
        Assert.Equal(0, wheel.Add(0.4));
        Assert.Equal(0, wheel.Add(0.4));
        Assert.Equal(1, wheel.Add(0.4));
        // The 0.2 left over is dropped when the wheel turns back.
        Assert.Equal(0, wheel.Add(-0.4));
        Assert.Equal(0, wheel.Add(-0.4));
        Assert.Equal(-1, wheel.Add(-0.4));
        Assert.Equal(3, wheel.Add(3));
        Assert.Equal(0, wheel.Add(double.NaN));
        var touchpad = new PreviewZoomWheel();
        Assert.Equal(1, Enumerable.Range(0, 10).Sum(_ => touchpad.Add(0.1)));
    }
}
