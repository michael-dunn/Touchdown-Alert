using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TouchdownAlert.App.Contracts;
using TouchdownAlert.App.Hubs;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.App.Services;

/// <summary>Sends the "alert" hub event to every connected client (overlay, dashboard).</summary>
public interface IAlertBroadcaster
{
    Task BroadcastAsync(AlertLogEntryViewModel alert);
}

public sealed class HubAlertBroadcaster : IAlertBroadcaster
{
    private readonly IHubContext<DashboardHub> _hub;

    public HubAlertBroadcaster(IHubContext<DashboardHub> hub)
    {
        _hub = hub;
    }

    public Task BroadcastAsync(AlertLogEntryViewModel alert) => _hub.Clients.All.SendAsync("alert", alert);
}

/// <summary>
/// The single timeline for presenting alerts: each alert's sound is enqueued and its "alert" hub event is sent
/// at the same instant, and the next alert is held back until <see cref="AlertOptions.BannerSeconds"/> have
/// passed. Clients show each banner for that same duration, so consecutive touchdowns play out as
/// banner 1 + sound 1, then banner 2 + sound 2 - never one team's sound under another team's banner
/// (sounds are capped by Sounds:MaxDurationSeconds, expected to be shorter than the banner).
/// Thread-safe; alerts arrive from the polling loop and the test endpoints.
/// </summary>
public sealed class AlertPresenter : IDisposable
{
    private readonly ISoundPlayer _soundPlayer;
    private readonly IAlertBroadcaster _broadcaster;
    private readonly IOptionsMonitor<AlertOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertPresenter> _logger;
    private readonly object _lock = new();
    private readonly Queue<(AlertLogEntryViewModel Alert, string? SoundPath)> _queue = new();
    private ITimer? _timer;
    private bool _presenting;

    public AlertPresenter(
        ISoundPlayer soundPlayer,
        IAlertBroadcaster broadcaster,
        IOptionsMonitor<AlertOptions> options,
        TimeProvider timeProvider,
        ILogger<AlertPresenter> logger)
    {
        _soundPlayer = soundPlayer;
        _broadcaster = broadcaster;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Alerts waiting behind the one currently on screen (diagnostics/tests).</summary>
    public int QueuedCount
    {
        get { lock (_lock) { return _queue.Count; } }
    }

    /// <summary>Presents the alert now if nothing is showing, otherwise queues it behind the current banner.</summary>
    public void Enqueue(AlertLogEntryViewModel alert, string? soundPath)
    {
        lock (_lock)
        {
            if (_presenting)
            {
                _queue.Enqueue((alert, soundPath));
                _logger.LogInformation(
                    "Alert for {TeamLabel} queued behind the current banner ({QueuedCount} waiting)",
                    alert.TeamLabel, _queue.Count);
                return;
            }

            Present(alert, soundPath);
        }
    }

    // Caller holds _lock.
    private void Present(AlertLogEntryViewModel alert, string? soundPath)
    {
        var duration = BannerDuration;
        var payload = alert with { DisplaySeconds = duration.TotalSeconds };

        if (soundPath is not null)
        {
            _soundPlayer.Enqueue(soundPath);
        }

        _ = BroadcastSafelyAsync(payload);

        if (duration <= TimeSpan.Zero)
        {
            // No pacing configured: every alert goes straight out.
            return;
        }

        _presenting = true;
        _timer?.Dispose();
        _timer = _timeProvider.CreateTimer(_ => OnBannerElapsed(), null, duration, Timeout.InfiniteTimeSpan);
    }

    private void OnBannerElapsed()
    {
        lock (_lock)
        {
            _presenting = false;
            if (_queue.Count == 0)
            {
                return;
            }

            var (alert, soundPath) = _queue.Dequeue();
            Present(alert, soundPath);
        }
    }

    private TimeSpan BannerDuration
    {
        get
        {
            var seconds = _options.CurrentValue.BannerSeconds;
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }
    }

    private async Task BroadcastSafelyAsync(AlertLogEntryViewModel payload)
    {
        try
        {
            await _broadcaster.BroadcastAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast alert for {TeamLabel}", payload.TeamLabel);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
