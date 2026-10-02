namespace Broiler.Mail.Tests;

/// <summary>A clock that moves only when the test advances it; its timers fire during <see cref="Advance"/>.</summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.ToArray()) timer.FireIfDue(_now);
    }

    /// <summary>One-shot timers; a period is not repeated.</summary>
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            if (!clock._timers.Contains(this)) clock._timers.Add(this);
            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            if (_due is not { } due || due > now) return;
            _due = null;
            callback(state);
        }

        public void Dispose() => clock._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
