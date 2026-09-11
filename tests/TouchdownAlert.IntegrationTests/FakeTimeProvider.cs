namespace TouchdownAlert.IntegrationTests;

/// <summary>
/// Minimal manual-advance TimeProvider (same shape as the one in TouchdownAlert.Overlay.Tests): Advance(TimeSpan)
/// moves the clock forward and synchronously fires any one-shot timers whose due time has elapsed, including
/// timers re-armed from within a callback.
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

    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        _now = target;

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
                _dueAt = null;
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
