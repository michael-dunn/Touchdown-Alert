using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Simulator.Simulation;

/// <summary>
/// Background "live game" driver. When started, ticks every N seconds: most ticks apply a small point
/// bump to a random starter (mostly focus-team-weighted), and roughly every 3rd tick scores a touchdown
/// instead (occasionally a passing+receiving pair on the same tick). Fully controllable at runtime via
/// the /sim/autoplay/* control endpoints; safe to leave running indefinitely.
/// </summary>
public sealed class AutoPlayService : BackgroundService
{
    private readonly SimulatedLeague _league;
    private readonly Random _rng = new();
    private volatile bool _running;
    private int _intervalSeconds = 20;
    private List<int> _focusTeamIds = new();
    private int _tickCount;

    public AutoPlayService(SimulatedLeague league)
    {
        _league = league;
    }

    public bool IsRunning => _running;

    public int IntervalSeconds => _intervalSeconds;

    public IReadOnlyList<int> FocusTeamIds => _focusTeamIds;

    public void Start(int intervalSeconds, IEnumerable<int>? focusTeamIds)
    {
        _intervalSeconds = Math.Max(2, intervalSeconds);
        _focusTeamIds = focusTeamIds?.ToList() ?? new List<int>();
        _running = true;
        _league.LogEvent($"Autoplay started (interval={_intervalSeconds}s, focus=[{string.Join(",", _focusTeamIds)}])");
    }

    public void Stop()
    {
        _running = false;
        _league.LogEvent("Autoplay stopped");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        double elapsedSeconds = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_running)
            {
                elapsedSeconds = 0;
                continue;
            }

            elapsedSeconds += 1;
            if (elapsedSeconds < _intervalSeconds)
            {
                continue;
            }

            elapsedSeconds = 0;
            try
            {
                Tick();
            }
            catch
            {
                // Never let a bad tick kill the background service.
            }
        }
    }

    private void Tick()
    {
        _tickCount++;
        var starters = _league.GetAllStarters();
        if (starters.Count == 0)
        {
            return;
        }

        var isTouchdownTick = _tickCount % 3 == 0;
        if (!isTouchdownTick)
        {
            // Small yardage-style point bump(s) to 1-2 random starters.
            var bumps = _rng.Next(1, 3);
            for (var i = 0; i < bumps; i++)
            {
                var pick = GameScript.PickWeightedStarter(starters, _focusTeamIds, _rng);
                if (pick is null)
                {
                    continue;
                }

                var points = Math.Round(_rng.NextDouble() * 2.4 + 0.1, 1);
                _league.AddPoints(pick.Value.Player.Id, points);
            }

            return;
        }

        // Occasionally do a passing+receiving pair: a QB and a receiver (prefer same pro team).
        if (_rng.NextDouble() < 0.35)
        {
            var qbs = starters.Where(s => s.Player.Position == "QB").ToList();
            var receivers = starters.Where(s => s.Player.Position is "WR" or "TE").ToList();
            if (qbs.Count > 0 && receivers.Count > 0)
            {
                var qbPick = GameScript.PickWeightedStarter(qbs, _focusTeamIds, _rng)!.Value;
                var sameProTeam = receivers.Where(r => r.Player.ProTeamId == qbPick.Player.ProTeamId).ToList();
                var receiverPool = sameProTeam.Count > 0 ? sameProTeam : receivers;
                var receiverPick = GameScript.PickWeightedStarter(receiverPool, _focusTeamIds, _rng)!.Value;

                _league.ScoreTouchdown(qbPick.Player.Id, TouchdownType.Passing);
                _league.ScoreTouchdown(receiverPick.Player.Id, TouchdownType.Receiving);
                return;
            }
        }

        var single = GameScript.PickWeightedStarter(starters, _focusTeamIds, _rng);
        if (single is null)
        {
            return;
        }

        var tdType = GameScript.RandomTdTypeForPosition(single.Value.Player.Position, _rng);
        if (tdType is null)
        {
            // Picked a kicker or similar - just give a small point bump instead.
            _league.AddPoints(single.Value.Player.Id, 1.0);
            return;
        }

        _league.ScoreTouchdown(single.Value.Player.Id, tdType.Value);
    }
}
