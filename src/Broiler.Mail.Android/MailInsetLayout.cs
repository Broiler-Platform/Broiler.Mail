using Android.Content;
using Android.Views;
using Android.Widget;

namespace Broiler.Mail.Android;

/// <summary>Keep forms and their actions above the IME under Android's enforced edge-to-edge layout.</summary>
internal sealed class MailInsetLayout(Context context) : FrameLayout(context)
{
    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets is null) return null;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var occupied = insets.GetInsets(WindowInsets.Type.SystemBars() |
                WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
            SetPadding(occupied.Left, occupied.Top, occupied.Right, occupied.Bottom);
        }
        else
        {
#pragma warning disable CS0618, CA1422
            SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop,
                insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CS0618, CA1422
        }
        return insets;
    }
}
