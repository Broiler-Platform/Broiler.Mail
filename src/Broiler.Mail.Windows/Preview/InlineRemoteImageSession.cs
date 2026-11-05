using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Infrastructure.Preview;
using Broiler.UI;

namespace Broiler.Mail.Windows.Preview;

/// <summary>UI-thread-owned consent state; background results commit only to the same live surface.</summary>
internal sealed class InlineRemoteImageSession : IDisposable
{
    private readonly ScrollableHtmlView _surface;
    private readonly string _rawHtml;
    private readonly IReadOnlyDictionary<string, MailEmbeddedImage> _embedded;
    private readonly Func<RemoteImageBroker> _createBroker;
    private Dictionary<string, MailEmbeddedImage> _approved = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _active;
    private int _generation;
    private bool _disposed;
    internal event EventHandler? Changed;
    internal RemoteImageState State { get; private set; }

    internal InlineRemoteImageSession(ScrollableHtmlView surface, string rawHtml,
        IReadOnlyDictionary<string, MailEmbeddedImage> embedded, int total, Func<RemoteImageBroker> createBroker)
    {
        (_surface, _rawHtml, _embedded, _createBroker) = (surface, rawHtml, embedded, createBroker);
        State = new(total, 0, false, total is > 0 and <= RemoteImageBroker.MaximumImages,
            total > RemoteImageBroker.MaximumImages ? "This message exceeds the 16 remote-image limit." : "Remote images are blocked.");
    }

    internal async Task LoadAsync()
    {
        if (_disposed || !State.CanLoad || State.IsLoading) return;
        int generation = ++_generation;
        var cancellation = _active = new CancellationTokenSource();
        var dispatcher = _surface.Session?.Dispatcher;
        var approved = _approved;
        State = State with { IsLoading = true, CanLoad = false, Status = "Loading remote images for this message…" };
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            using var broker = _createBroker();
            var batch = await Task.Run(() => broker.LoadBatchAsync(_rawHtml, _embedded, approved, cancellation.Token), cancellation.Token).ConfigureAwait(false);
            Post(() =>
            {
                if (!Current()) return;
                try
                {
                    _surface.UpdateHtml(batch.Document.Html, preserveReadingPosition: true);
                    _approved = batch.Images;
                    int loaded = batch.Result.TotalSucceeded;
                    State = new(State.Total, loaded, false, loaded < State.Total,
                        loaded == State.Total ? $"Loaded {loaded} remote images for this message. Permission is not saved."
                        : $"Loaded {loaded} of {State.Total} remote images. Unavailable images can be retried.");
                    _active = null;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception error) when (error is not OutOfMemoryException) { Failed(); }
            });
        }
        catch (OperationCanceledException)
        {
            Post(() => { if (Current()) Failed("Remote image loading timed out or was canceled. Loaded images are kept."); });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Post(() => { if (Current()) Failed(); });
        }
        finally { cancellation.Dispose(); }

        bool Current() => !_disposed && !_surface.IsDisposed && generation == _generation && !cancellation.IsCancellationRequested;
        void Failed(string status = "Remote images could not be loaded. The current message remains available; you can retry.")
        {
            _active = null;
            State = State with { IsLoading = false, CanLoad = true, Status = status };
            Changed?.Invoke(this, EventArgs.Empty);
        }
        void Post(Action action)
        {
            try { if (dispatcher is null) action(); else dispatcher.Post(action); }
            catch (ObjectDisposedException) { }
        }
    }

    internal void Cancel()
    {
        if (_disposed || !State.IsLoading) return;
        ++_generation;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        _active = null;
        State = State with { IsLoading = false, CanLoad = true, Status = "Remote image loading canceled. Previously loaded images are kept." };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Block()
    {
        if (_disposed || _surface.IsDisposed) return;
        ++_generation;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        _active = null; _approved = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Rebuild from original HTML and original embedded resources, with no remote approval map.
            _surface.UpdateHtml(HtmlPreviewPolicy.Create(_rawHtml, _embedded).Html, preserveReadingPosition: true);
            State = new(State.Total, 0, false, State.Total is > 0 and <= RemoteImageBroker.MaximumImages,
                "Remote images are blocked again. Previously sent requests cannot be undone. Load explicitly to contact servers again.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            State = new(State.Total, 0, false, false, "The blocked preview could not be rebuilt. Use the text reader; remote permission was cleared.");
            _surface.HideAfterResourceResetFailure();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        _active = null;
        _approved = new(StringComparer.OrdinalIgnoreCase);
    }
}
