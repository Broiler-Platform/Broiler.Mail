// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   11
// Annotated:        0/11
// Exempt:           14
// Human-reviewed:   0/11
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       11
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>
/// Trusted consent controls for the current message, as a one-line banner above the HTML reader; never
/// starts a fetch on attachment. It is shown only while the HTML reader is and the message has remote images.
/// </summary>
public sealed class RemoteImageActions : UiElement
{
    /// <summary>What loading contacts and what blocking again can and cannot undo.</summary>
    public const string PrivacyDetails = "Loading images contacts external servers and may reveal that you opened this message. Permission applies only to this open message. Blocking again removes downloaded remote images from this preview; earlier requests cannot be undone. Inline images stay.";

    private readonly IInlineRemoteImages _images;
    private readonly Action _focusFallback;
    private readonly string _initialStatus;
    private readonly StandardPanel _panel = new() { Spacing = 2 };
    private readonly StandardToolbar _row = new() { Overflow = UiToolbarOverflow.Wrap, Padding = 4, Spacing = 8, AccessibleName = "Remote images" };
    private readonly StandardLabel _summary = new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap, ToolTipText = PrivacyDetails };
    private readonly StandardButton _load = new() { Text = "Load images" };
    private readonly StandardButton _cancel = new() { Text = "Cancel loading", AccessibleName = "Cancel image loading" };
    private readonly StandardButton _block = new() { Text = "Block again", AccessibleName = "Block remote images again" };
    private readonly StandardButton _learnMore = new() { Text = "Learn more", AccessibleName = "Learn more about remote images" };
    private readonly ToolbarDisclosure _details;
    private bool _htmlVisible = true;
    private bool _attempted;

    public RemoteImageActions(IInlineRemoteImages images, Action focusFallback)
    {
        (_images, _focusFallback) = (images, focusFallback);
        _initialStatus = images.RemoteImages.Status;
        _details = new ToolbarDisclosure(_learnMore);
        _details.Content.AddChild(new StandardLabel { Text = PrivacyDetails, UseMnemonic = false, Wrapping = UiTextWrapping.Wrap });
        foreach (var element in new UiElement[] { _summary, _load, _cancel, _block, _learnMore }) _row.AddChild(element);
        _panel.AddChild(_row); _panel.AddChild(_details); AddChild(_panel);
        _load.Clicked += async (_, _) =>
        {
            _attempted = true;
            try { await _images.LoadRemoteImagesAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!IsDisposed) _summary.Text = "Remote images could not be loaded. The message remains available.";
            }
        };
        _block.Clicked += (_, _) => BlockAgain();
        _cancel.Clicked += (_, _) => _images.CancelRemoteImages();
        _images.RemoteImagesChanged += OnChanged;
        Update();
    }

    /// <summary>Raised after the banner's state changes, such as when it is shown or hidden or Block again becomes available.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>Whether remote images were loaded (or are loading) and can be blocked again now.</summary>
    public bool CanBlockAgain => !IsDisposed && _htmlVisible && _images is IInlineRemoteImageReset &&
        (_images.RemoteImages.IsLoading || _images.RemoteImages.Loaded > 0);

    /// <summary>Removes the downloaded remote images from this preview, as Block again does.</summary>
    public void BlockAgain()
    {
        if (!CanBlockAgain || _images is not IInlineRemoteImageReset reset) return;
        _attempted = false;
        try { reset.BlockRemoteImages(); Update(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!IsDisposed) _summary.Text = "Remote-image blocking did not finish. Switch to the text reader."; }
    }

    public void SetHtmlVisible(bool visible)
    {
        _htmlVisible = visible;
        if (!visible) _images.CancelRemoteImages();
        Update();
    }

    private void OnChanged(object? sender, EventArgs args) => Update();
    private void Update()
    {
        if (IsDisposed) return;
        var state = _images.RemoteImages;
        // The blocked state is one templated sentence; every other state shows the surface's own status, which
        // says what was loaded, canceled or could not be undone.
        _summary.Text = !state.IsLoading && state.Loaded == 0 && state.CanLoad && !_attempted && state.Status == _initialStatus
            ? $"Remote images ({state.Total}) are blocked to protect your privacy."
            : state.Status;
        _load.Text = _attempted && state.Loaded < state.Total ? state.Loaded > 0 ? "Retry failed" : "Retry" : "Load images";
        _load.Visibility = state.CanLoad && !state.IsLoading ? UiVisibility.Visible : UiVisibility.Collapsed;
        _load.IsEnabled = _htmlVisible && state.CanLoad && !state.IsLoading;
        _cancel.Visibility = state.IsLoading ? UiVisibility.Visible : UiVisibility.Collapsed;
        _cancel.IsEnabled = state.IsLoading;
        // While loading, Cancel stops the request; Block again (also in More message actions) removes what was loaded.
        bool canBlock = _images is IInlineRemoteImageReset && !state.IsLoading && state.Loaded > 0;
        _block.Visibility = canBlock ? UiVisibility.Visible : UiVisibility.Collapsed;
        _block.IsEnabled = _htmlVisible && canBlock;
        var visibility = _htmlVisible && state.Total > 0 ? UiVisibility.Visible : UiVisibility.Collapsed;
        // Hiding the banner while focus is in it hands focus to the reader, once.
        bool hiding = Visibility == UiVisibility.Visible && visibility == UiVisibility.Collapsed &&
            Session?.FocusedElement is { } inside && inside.IsDescendantOf(this);
        if (Visibility != visibility) Visibility = visibility;
        if (visibility == UiVisibility.Collapsed) _details.Collapse();
        var focused = Session?.FocusedElement;
        if (hiding) _focusFallback();
        else if (visibility == UiVisibility.Collapsed) { }
        else if (focused == _block && !_block.IsEnabled)
        {
            if (_load.IsEnabled) Session!.SetFocus(_load); else _focusFallback();
        }
        else if (focused == _load && state.IsLoading) Session!.SetFocus(_cancel);
        else if (focused == _cancel && !state.IsLoading)
        {
            if (_load.IsEnabled) Session!.SetFocus(_load); else if (_block.IsEnabled) Session!.SetFocus(_block); else _focusFallback();
        }
        else if (focused == _load && !_load.IsEnabled) { if (_block.IsEnabled) Session!.SetFocus(_block); else _focusFallback(); }
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override BSize MeasureCore(BSize availableSize) => _panel.Measure(availableSize);
    protected override void ArrangeCore(BRect finalRect) => _panel.Arrange(finalRect);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _images.RemoteImagesChanged -= OnChanged; _images.CancelRemoteImages(); }
        base.Dispose(disposing);
    }
}
