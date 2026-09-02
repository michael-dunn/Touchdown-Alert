using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TouchdownAlert.App.Hubs;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.App.Services;

/// <summary>
/// Polls every configured league concurrently on an interval (configurable live via IOptionsMonitor), runs
/// the touchdown detector, routes any events through IAlertRouter/AlertDispatcher, updates DashboardState,
/// and broadcasts the refreshed view model over SignalR. A failure in one league's poll is recorded against
/// that league only and never stops the others. Backoff is global and applies only when every configured
/// league fails in the same cycle: it doubles (capped at 5 minutes) and resets to the configured interval
/// as soon as at least one league polls successfully.
/// </summary>
public sealed class PollingService : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly IEnumerable<ILeagueSource> _leagueSources;
    private readonly ITouchdownDetector _detector;
    private readonly IAlertRouter _alertRouter;
    private readonly AlertDispatcher _dispatcher;
    private readonly DashboardState _state;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly IOptionsMonitor<PollingOptions> _pollingOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PollingService> _logger;

    private readonly SemaphoreSlim _pokeSignal = new(0, int.MaxValue);
    private TimeSpan? _currentBackoff;

    public PollingService(
        IEnumerable<ILeagueSource> leagueSources,
        ITouchdownDetector detector,
        IAlertRouter alertRouter,
        AlertDispatcher dispatcher,
        DashboardState state,
        IHubContext<DashboardHub> hub,
        IOptionsMonitor<PollingOptions> pollingOptions,
        TimeProvider timeProvider,
        ILogger<PollingService> logger)
    {
        _leagueSources = leagueSources;
        _detector = detector;
        _alertRouter = alertRouter;
        _dispatcher = dispatcher;
        _state = state;
        _hub = hub;
        _pollingOptions = pollingOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Wakes the loop immediately instead of waiting for the current delay to elapse.</summary>
    public Task TriggerNowAsync()
    {
        _pokeSignal.Release();
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay;
            try
            {
                await PollOnceAsync(stoppingToken);
                _currentBackoff = null;
                nextDelay = ConfiguredInterval();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Poll failed for every configured league: {Message}", ex.Message);
                var baseInterval = ConfiguredInterval();
                _currentBackoff = _currentBackoff is null
                    ? baseInterval
                    : TimeSpanMin(TimeSpan.FromTicks(_currentBackoff.Value.Ticks * 2), MaxBackoff);
                nextDelay = _currentBackoff.Value;
            }

            _state.RecordPollCycle();
            _state.SetNextPollAt(_timeProvider.GetUtcNow() + nextDelay);
            await BroadcastStateAsync();
            await WaitAsync(nextDelay, stoppingToken);
        }
    }

    private TimeSpan ConfiguredInterval() =>
        TimeSpan.FromSeconds(Math.Max(1, _pollingOptions.CurrentValue.IntervalSeconds));

    private static TimeSpan TimeSpanMin(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>
    /// Polls every configured league concurrently. Per-league failures are recorded on <see cref="DashboardState"/>
    /// and do not stop other leagues; this method only throws when every league failed, so the caller's
    /// exponential backoff kicks in solely for a total outage.
    /// </summary>
    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var tasks = _leagueSources.Select(source => PollLeagueAsync(source, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks);

        if (results.Length > 0 && results.All(r => !r.Success))
        {
            var firstError = results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? "All league polls failed.";
            throw new InvalidOperationException(firstError);
        }
    }

    private async Task<(bool Success, string? Error)> PollLeagueAsync(ILeagueSource source, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await source.GetSnapshotAsync(cancellationToken);
            var events = _detector.Update(snapshot);

            _state.RecordSnapshot(snapshot, _timeProvider.GetUtcNow(), _detector.IsSeeded(source.League.Key));

            foreach (var touchdown in events)
            {
                var alerts = _alertRouter.Route(touchdown, snapshot);
                foreach (var alert in alerts)
                {
                    await _dispatcher.DispatchAsync(alert);
                }
            }

            _logger.LogInformation(
                "Poll ok [{League}]: week {Week}, {TeamCount} teams, {EventCount} touchdown event(s)",
                source.League.Key, snapshot.ScoringPeriodId, snapshot.Teams.Count, events.Count);

            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Poll failed for league {League}: {Message}", source.League.Key, ex.Message);
            _state.RecordError(source.League.Key, ex.Message);
            return (false, ex.Message);
        }
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var delayTask = Task.Delay(delay, _timeProvider, linkedCts.Token);
        var pokeTask = _pokeSignal.WaitAsync(linkedCts.Token);

        try
        {
            await Task.WhenAny(delayTask, pokeTask);
        }
        finally
        {
            linkedCts.Cancel();
            // Observe whichever task didn't win, so a cancellation exception never goes unhandled.
            await SwallowAsync(delayTask);
            await SwallowAsync(pokeTask);
        }
    }

    private static async Task SwallowAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task BroadcastStateAsync() => _hub.Clients.All.SendAsync("state", _state.ToViewModel());
}
