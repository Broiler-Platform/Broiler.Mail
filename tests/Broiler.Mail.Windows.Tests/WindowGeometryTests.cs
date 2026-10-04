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
    public void Restore_On_A_Monitor_With_Another_Scale_Keeps_The_Dip_Client_Size()
    {
        // The system scale is the 150 % primary's; the window was last on a 100 % monitor to its right.
        var right = new PixelRect(1920, 0, 3840, 1040);
        var settings = new ApplicationSettings { Window = At(2100, 100) };
        var plan = WindowRestorePlan.For(settings, [Primary, right], 1.5, [1.5, 1.0]);
        // The window converts option DIPs at 150 % but renders at 100 % there: 787 x 493 option DIPs
        // become about 1180 x 740 pixels, which are the remembered 1180 x 740 DIPs at 100 %.
        Assert.Equal((787, 493), (plan.ClientWidth, plan.ClientHeight));
        Assert.InRange(Math.Round(plan.ClientWidth * 1.5), 1179, 1181);
        Assert.Equal((1400, 100 / 1.5), (plan.Left, plan.Top));
        Assert.Equal(At(2100, 100), plan.Normal);

        // At the system scale, or with unknown monitor scales, the remembered DIPs are already right.
        Assert.Equal((1180, 740), Size(WindowRestorePlan.For(settings, [Primary, right], 1.5, [1.5, 1.5])));
        Assert.Equal((1180, 740), Size(WindowRestorePlan.For(settings, [Primary, right], 1.5)));
        Assert.Equal((1180, 740), Size(WindowRestorePlan.For(settings, [Primary, right], 1.5, [1.5, 0])));
        Assert.Equal((1573, 987), Size(WindowRestorePlan.For(settings, [Primary, right], 1.5, [1.5, 2.0])));
        // A monitor left of the primary one is reached by a move after Show, whose WM_DPICHANGED keeps the DIP size.
        var left = WindowRestorePlan.For(new ApplicationSettings { Window = At(-2000, -100) }, [Primary, LeftMonitor], 1.5, [1.5, 1.0]);
        Assert.Equal((1180, 740), Size(left));
        Assert.Equal((-2000, -100), left.MoveAfterShow);

        static (int, int) Size(WindowRestorePlan plan) => (plan.ClientWidth, plan.ClientHeight);
    }

    [Fact]
    public void The_Monitor_Scale_Is_The_One_The_Window_Overlaps_Most()
    {
        var right = new PixelRect(1920, 0, 3840, 1040);
        Assert.Equal(1.0, WindowGeometry.ScaleAt(At(1500, 100), [Primary, right], [1.5, 1.0]));
        Assert.Equal(1.5, WindowGeometry.ScaleAt(At(1000, 100), [Primary, right], [1.5, 1.0]));
        Assert.Null(WindowGeometry.ScaleAt(At(5000, 5000), [Primary, right], [1.5, 1.0]));
        Assert.Null(WindowGeometry.ScaleAt(At(1500, 100), [Primary, right], [1.5]));
    }
}
