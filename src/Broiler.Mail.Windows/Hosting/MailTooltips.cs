// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   7
// Annotated:        0/7
// Exempt:           6
// Human-reviewed:   0/7
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.Windowing;
using Broiler.UI;
using Broiler.UI.Standard;
using Broiler.UI.Tooltip.Standard;
using Broiler.UI.Window;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>
/// Hover tooltips for one native window: shows a control's <see cref="UiElement.ToolTipText"/> after
/// the tooltip delay. The window ticks only while a tooltip is pending, so an idle window draws nothing.
/// </summary>
internal sealed class MailTooltips
{
    private const double TickMilliseconds = 33;
    private readonly BWindow _window;
    private readonly Action _invalidate;
    private readonly StandardTooltip _tooltip = new();
    private readonly StandardTooltipController _controller;
    private bool _ticking;

    public MailTooltips(BWindow window, UiSession session, UiWindow owner, Action invalidate)
    {
        _window = window;
        _invalidate = invalidate;
        owner.OpenOwnedWindow(_tooltip, new BRect(0, 0, 1, 1), UiWindowKind.Tooltip);
        _tooltip.Deactivate();
        _controller = new StandardTooltipController(_tooltip);
        // Every input the session receives, from whichever native path delivered it.
        session.InputDispatched += (_, input) =>
        {
            bool changed = input.Kind == UiInputEventKind.PointerMove ? _controller.PointerMoved(input.Position) : _controller.Dismiss();
            Sync();
            if (changed) _invalidate();
        };
    }

    /// <summary>The control the tooltip is about, or null.</summary>
    public UiElement? Target => _controller.Target;

    public void ApplyTheme(StandardThemeTokens theme) => _tooltip.ApplyTheme(theme);

    /// <summary>Call from the window's animation tick.</summary>
    public void Tick()
    {
        if (_controller.Tick()) _invalidate();
        Sync();
    }

    /// <summary>Hides the tooltip, for a window that is closing.</summary>
    public void Dismiss()
    {
        _controller.Dismiss();
        Sync();
    }

    private void Sync()
    {
        bool wanted = _controller.IsWaiting;
        if (wanted == _ticking) return;
        _ticking = wanted;
        if (wanted) _window.StartAnimationTimer(TickMilliseconds);
        else _window.StopAnimationTimer();
    }
}
