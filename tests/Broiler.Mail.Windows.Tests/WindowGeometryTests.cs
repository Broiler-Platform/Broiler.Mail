using Broiler.Mail.Core.Settings;
using Broiler.Mail.Windows.Hosting;

namespace Broiler.Mail.Windows.Tests;

public sealed class WindowGeometryTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);
    private static readonly PixelRect LeftMonitor = new(-2560, -200, 0, 1240);

    private static WindowPlacement At(int left, int top, int width = 1200, int height = 800) =>
        new() { Left = left, Top = top, Width = width, Height = height, ClientWidth = width - 20, ClientHeight = height - 60 };

    [Fact]
    public void Window_With_A_Reachable_Title_Bar_Keeps_Its_Position()
    {
        Assert.Equal((100, 50), WindowGeometry.Restore(At(100, 50), [Primary]));
        // Mostly off the right edge, but enough caption remains to drag it back.
        Assert.Equal((1700, 50), WindowGeometry.Restore(At(1700, 50), [Primary]));
        // On a monitor with negative coordinates.
        Assert.Equal((-2000, -100), WindowGeometry.Restore(At(-2000, -100), [Primary, LeftMonitor]));
    }

    [Fact]
    public void Caption_Under_The_Top_Edge_Is_Pulled_Into_View()
    {
        Assert.Equal((100, 0), WindowGeometry.Restore(At(100, -10), [Primary]));
    }

    [Fact]
    public void Window_On_A_Removed_Monitor_Moves_To_The_Primary_Work_Area()
    {
        var position = WindowGeometry.Restore(At(-2000, -100), [Primary]);
        Assert.Equal(((1920 - 1200) / 2, (1040 - 800) / 2), position);
    }

    [Fact]
    public void Unreachable_Caption_Moves_To_The_Monitor_The_Window_Overlaps_Most()
    {
        // The caption is above the left monitor's top edge, the body mostly on it.
        var position = WindowGeometry.Restore(At(-1800, -400), [Primary, LeftMonitor]);
        Assert.Equal((-2560 + (2560 - 1200) / 2, -200 + (1440 - 800) / 2), position);
    }

    [Fact]
    public void A_Window_Larger_Than_The_Work_Area_Starts_At_Its_Corner()
    {
        Assert.Equal((0, 0), WindowGeometry.Restore(At(5000, 5000, 2400, 1400), [Primary]));
    }

    [Fact]
    public void No_Known_Monitor_Lets_The_Window_Center_Itself() =>
        Assert.Null(WindowGeometry.Restore(At(100, 100), []));

    [Fact]
    public void Plan_Uses_Initial_Size_Without_A_Remembered_Window()
    {
        var plan = WindowRestorePlan.For(new ApplicationSettings { WindowWidth = 1000, WindowHeight = 700 }, [Primary], 1.5);
        Assert.Equal(new WindowRestorePlan(1000, 700, null, null, null, false, null), plan);
    }

    [Fact]
    public void Plan_Converts_Positive_Positions_To_Option_Dips_And_Moves_Negative_Ones_Later()
    {
        var positive = WindowRestorePlan.For(new ApplicationSettings { Window = At(300, 150) with { Maximized = true } }, [Primary], 1.5);
        Assert.Equal((1180, 740), (positive.ClientWidth, positive.ClientHeight));
        Assert.Equal(200, positive.Left);
        Assert.Equal(100, positive.Top);
        Assert.Null(positive.MoveAfterShow);
        Assert.True(positive.Maximized);
        Assert.Equal(At(300, 150) with { Maximized = true }, positive.Normal);

        var negative = WindowRestorePlan.For(new ApplicationSettings { Window = At(-2000, -100) }, [Primary, LeftMonitor], 1.0);
        Assert.Null(negative.Left);
        Assert.Equal((-2000, -100), negative.MoveAfterShow);

        // A window from a removed monitor is clamped before it is planned.
        var moved = WindowRestorePlan.For(new ApplicationSettings { Window = At(-2000, -100) }, [Primary], 1.0);
        Assert.Equal((360, 120), (moved.Normal!.Left, moved.Normal.Top));
        Assert.Equal(360, moved.Left);
    }

    [Fact]
    public void A_Window_On_A_Monitor_With_Another_Scale_Reopens_At_Its_Remembered_Dip_Size_Every_Time()
    {
        // The system scale is the 150 % primary's; the window was last on a monitor to its right. Frames
        // are AdjustWindowRectExForDpi's for the main window's style at 96, 144, and 192 DPI.
        var right = new PixelRect(1920, 0, 3840, 1040);
        var frames = new Dictionary<double, (int Width, int Height)> { [1.0] = (16, 39), [1.5] = (22, 56), [2.0] = (26, 71) };
        foreach (double monitorScale in new[] { 1.0, 2.0 })
        {
            var saved = At(2100, 100);
            for (int start = 0; start < 3; start++)
            {
                var plan = WindowRestorePlan.For(new ApplicationSettings { Window = saved }, [Primary, right], 1.5);
                Assert.Equal((1180, 740), (plan.ClientWidth, plan.ClientHeight));
                Assert.Equal(1400, plan.Left);
                // Direct2DWindow converts the option DIPs at the system scale and adds the system frame,
                // but the window renders at the monitor's scale and draws the monitor's frame.
                var outer = new PixelRect(2100, 100, 2100 + (int)Math.Round(plan.ClientWidth * 1.5) + frames[1.5].Width,
                    100 + (int)Math.Round(plan.ClientHeight * 1.5) + frames[1.5].Height);
                var client = (outer.Width - frames[monitorScale].Width, outer.Height - frames[monitorScale].Height);
                var fitted = WindowGeometry.FitClient(outer, client, (int)Math.Round(plan.ClientWidth * monitorScale),
                    (int)Math.Round(plan.ClientHeight * monitorScale), centered: plan.Left is null);
                Assert.NotNull(fitted);
                Assert.Equal((2100, 100), (fitted.Value.Left, fitted.Value.Top));
                // The client the window ends up with is the remembered DIP size at the monitor's scale, so
                // the size remembered at close is the same and nothing drifts from one start to the next.
                var fittedClient = (fitted.Value.Width - frames[monitorScale].Width, fitted.Value.Height - frames[monitorScale].Height);
                Assert.Equal(((int)(1180 * monitorScale), (int)(740 * monitorScale)), fittedClient);
                saved = saved with
                {
                    Width = fitted.Value.Width, Height = fitted.Value.Height,
                    ClientWidth = (int)Math.Round(fittedClient.Item1 / monitorScale), ClientHeight = (int)Math.Round(fittedClient.Item2 / monitorScale),
                };
                Assert.Equal((1180, 740), (saved.ClientWidth, saved.ClientHeight));
            }
        }
    }

    [Fact]
    public void Fitting_The_Client_Keeps_The_Frame_And_A_Centered_Window_Centered()
    {
        var outer = new PixelRect(400, 200, 1516, 959);
        Assert.Null(WindowGeometry.FitClient(outer, (1100, 720), 1100, 720, centered: true));
        Assert.Equal(new PixelRect(400, 200, 2066, 1319), WindowGeometry.FitClient(outer, (1100, 720), 1650, 1080, centered: false));
        Assert.Equal(new PixelRect(125, 20, 1791, 1139), WindowGeometry.FitClient(outer, (1100, 720), 1650, 1080, centered: true));
        // A centered window larger than the screen starts at its origin, as Windows placed it.
        Assert.Equal(new PixelRect(0, 0, 3316, 2199), WindowGeometry.FitClient(outer, (1100, 720), 3300, 2160, centered: true));
        Assert.Equal(new PixelRect(675, 380, 1241, 779), WindowGeometry.FitClient(outer, (1100, 720), 550, 360, centered: true));
    }
}
