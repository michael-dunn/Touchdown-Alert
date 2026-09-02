using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Detection;

/// <summary>
/// Stateful diff-based touchdown detector. Feed it every <see cref="LeagueSnapshot"/> in polling order.
/// Thread-safe: <see cref="Update"/> and <see cref="Reset"/> are guarded by an internal lock.
/// </summary>
public sealed class TouchdownDetector : ITouchdownDetector
{
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private readonly Dictionary<long, TouchdownCounts> _lastCounts = new();

    public TouchdownDetector(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsSeeded { get; private set; }

    public int? SeededScoringPeriodId { get; private set; }

    /// <summary>Current time per the injected <see cref="TimeProvider"/>. Exposed for diagnostics/testing.</summary>
    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public IReadOnlyList<TouchdownEvent> Update(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            var players = CollectPlayers(snapshot);

            if (!IsSeeded || SeededScoringPeriodId != snapshot.ScoringPeriodId)
            {
                Seed(players, snapshot.ScoringPeriodId);
                return Array.Empty<TouchdownEvent>();
            }

            var events = new List<TouchdownEvent>();

            foreach (var (playerId, (player, startingTeamIds, benchedTeamIds)) in players)
            {
                var newCounts = player.Touchdowns;

                if (!_lastCounts.TryGetValue(playerId, out var oldCounts))
                {
                    // Newly appeared mid-period (e.g. waiver pickup): seed silently, no event.
                    _lastCounts[playerId] = newCounts;
                    continue;
                }

                foreach (var type in AllTouchdownTypes)
                {
                    var oldValue = oldCounts.Get(type);
                    var newValue = newCounts.Get(type);

                    if (newValue > oldValue)
                    {
                        events.Add(new TouchdownEvent(
                            DetectedAt: snapshot.FetchedAt,
                            ScoringPeriodId: snapshot.ScoringPeriodId,
                            PlayerId: playerId,
                            PlayerName: player.FullName,
                            Position: player.Position,
                            Type: type,
                            Count: newValue - oldValue,
                            StartingTeamIds: startingTeamIds,
                            BenchedTeamIds: benchedTeamIds));
                    }
                }

                _lastCounts[playerId] = newCounts;
            }

            return events;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _lastCounts.Clear();
            IsSeeded = false;
            SeededScoringPeriodId = null;
        }
    }

    private void Seed(IReadOnlyDictionary<long, (RosteredPlayer Player, IReadOnlyList<int> Starting, IReadOnlyList<int> Benched)> players, int scoringPeriodId)
    {
        _lastCounts.Clear();
        foreach (var (playerId, entry) in players)
        {
            _lastCounts[playerId] = entry.Player.Touchdowns;
        }

        IsSeeded = true;
        SeededScoringPeriodId = scoringPeriodId;
    }

    /// <summary>
    /// Builds a stable-ordered map of playerId -> (one representative RosteredPlayer, starting team ids, benched team ids),
    /// covering every rostered player (starters and bench) across all teams, in snapshot roster order.
    /// </summary>
    private static Dictionary<long, (RosteredPlayer Player, IReadOnlyList<int> Starting, IReadOnlyList<int> Benched)> CollectPlayers(LeagueSnapshot snapshot)
    {
        var result = new Dictionary<long, (RosteredPlayer Player, List<int> Starting, List<int> Benched)>();
        var order = new List<long>();

        foreach (var team in snapshot.Teams)
        {
            foreach (var player in team.Roster)
            {
                if (!result.TryGetValue(player.PlayerId, out var entry))
                {
                    entry = (player, new List<int>(), new List<int>());
                    result[player.PlayerId] = entry;
                    order.Add(player.PlayerId);
                }

                if (player.IsStarter)
                {
                    entry.Starting.Add(team.TeamId);
                }
                else
                {
                    entry.Benched.Add(team.TeamId);
                }
            }
        }

        var ordered = new Dictionary<long, (RosteredPlayer, IReadOnlyList<int>, IReadOnlyList<int>)>();
        foreach (var playerId in order)
        {
            var (player, starting, benched) = result[playerId];
            starting.Sort();
            benched.Sort();
            ordered[playerId] = (player, starting, benched);
        }

        return ordered;
    }

    private static readonly TouchdownType[] AllTouchdownTypes =
    [
        TouchdownType.Passing,
        TouchdownType.Rushing,
        TouchdownType.Receiving,
        TouchdownType.KickReturn,
        TouchdownType.PuntReturn,
        TouchdownType.FumbleReturn,
        TouchdownType.InterceptionReturn,
        TouchdownType.BlockedKickReturn,
    ];
}
