namespace TouchdownAlert.Overlay.Tests;

/// <summary>
/// Minimal manual-advance TimeProvider for testing OverlayViewModel's banner-queue timing without real
/// delays: Advance(TimeSpan) moves the clock forward and synchronously fires any timers whose due time has
/// elapsed (including periodic re-arming for one-shot timers rescheduled from within their own callback).
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly List<FakeTimer> _timers = new();

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward by the given amount, firing any timers that come due along the way.</summary>
    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        _now = target;

        // Snapshot: a callback may create/dispose timers, so iterate a copy.
        foreach (var timer in _timers.ToArray())
        {
            timer.FireIfDue(target);
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _dueAt;

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _owner._timers.Add(this);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            if (_dueAt.HasValue && now >= _dueAt.Value)
            {
                _dueAt = null; // one-shot: the viewmodel re-arms a new timer for the next banner.
                _callback(_state);
            }
        }

        public void Dispose() => _owner._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
