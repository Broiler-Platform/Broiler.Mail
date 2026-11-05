using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>Trusted consent controls for the current message; never starts a fetch on attachment.</summary>
public sealed class RemoteImageActions : UiElement
{
    private readonly IInlineRemoteImages _images;
    private readonly Action _focusFallback;
    private readonly StandardPanel _panel = new() { Spacing = 4 };
    private readonly StandardButton _load = new() { Text = "Load remote images" };
    private readonly StandardButton _block = new() { Text = "Block remote images again" };
    private readonly StandardButton _cancel = new() { Text = "Cancel image loading" };
    private readonly StandardLabel _status = new() { UseMnemonic = false, Wrapping = UiTextWrapping.Wrap };
    private bool _htmlVisible = true;
    private bool _attempted;

    public RemoteImageActions(IInlineRemoteImages images, Action focusFallback)
    {
        (_images, _focusFallback) = (images, focusFallback);
        _panel.AddChild(new StandardLabel
        {
            Text = "Loading images contacts external servers and may reveal that you opened this message. Permission applies only to this open message. Blocking again removes downloaded remote images from this preview; earlier requests cannot be undone. Inline images stay.",
            UseMnemonic = false, Wrapping = UiTextWrapping.Wrap,
        });
        var row = new StandardToolbar { Overflow = UiToolbarOverflow.Wrap, Padding = 6, Spacing = 8 };
        row.AddChild(_load); row.AddChild(_cancel); row.AddChild(_block); _panel.AddChild(row); _panel.AddChild(_status); AddChild(_panel);
        _load.Clicked += async (_, _) =>
        {
            _attempted = true;
            try { await _images.LoadRemoteImagesAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!IsDisposed) _status.Text = "Remote images could not be loaded. The message remains available.";
            }
        };
        _block.Clicked += (_, _) =>
        {
            if (_images is not IInlineRemoteImageReset reset || !_block.IsEnabled) return;
            _attempted = false;
            try { reset.BlockRemoteImages(); Update(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!IsDisposed) _status.Text = "Remote-image blocking did not finish. Switch to the text reader."; }
        };
        _cancel.Clicked += (_, _) => _images.CancelRemoteImages();
        _images.RemoteImagesChanged += OnChanged;
        Update();
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
        _load.Text = state.IsLoading ? "Loading images…" : state.Loaded == state.Total ? "Remote images loaded"
            : _attempted ? "Retry unavailable images" : "Load remote images";
        _load.IsEnabled = _htmlVisible && state.CanLoad && !state.IsLoading;
        _cancel.Visibility = state.IsLoading ? UiVisibility.Visible : UiVisibility.Collapsed;
        _cancel.IsEnabled = state.IsLoading;
        _block.Visibility = _images is IInlineRemoteImageReset ? UiVisibility.Visible : UiVisibility.Collapsed;
        _block.IsEnabled = _htmlVisible && _images is IInlineRemoteImageReset && (state.IsLoading || state.Loaded > 0);
        _status.Text = state.Status;
        if (Session?.FocusedElement == _block && !_block.IsEnabled)
        {
            if (_load.IsEnabled) Session.SetFocus(_load); else _focusFallback();
        }
        else if (Session?.FocusedElement == _load && state.IsLoading) Session.SetFocus(_cancel);
        else if (Session?.FocusedElement == _cancel && !state.IsLoading)
        {
            if (_load.IsEnabled) Session.SetFocus(_load); else _focusFallback();
        }
        else if (Session?.FocusedElement == _load && !_load.IsEnabled) _focusFallback();
    }

    protected override BSize MeasureCore(BSize availableSize) => _panel.Measure(availableSize);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _images.RemoteImagesChanged -= OnChanged; _images.CancelRemoteImages(); }
        base.Dispose(disposing);
    }
}
