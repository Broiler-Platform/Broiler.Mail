using System.Collections.Concurrent;
using Broiler.UI;

namespace Broiler.Mail.Tests;

/// <summary>
/// Queues posted callbacks until the test drains them, like the native window's queued dispatcher,
/// but drainable from any thread so tests may await between steps. Unlike ImmediateUiDispatcher,
/// background completions such as draft autosave never run concurrently with the test's own UI calls.
/// </summary>
internal sealed class TestQueueDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _queue = new();

    public bool CheckAccess() => true;
    public void Post(Action action) => _queue.Enqueue(action);

    /// <summary>Runs queued callbacks, including any they post, on the calling thread.</summary>
    public void Drain()
    {
        while (_queue.TryDequeue(out var action)) action();
    }

    /// <summary>Drains until background work has posted its result and <paramref name="settled"/> holds.</summary>
    public void DrainUntil(Func<bool> settled)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            Drain();
            if (settled()) return;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Background work did not settle.");
            Thread.Sleep(5);
        }
    }
}
