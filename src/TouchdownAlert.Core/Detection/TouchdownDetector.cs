using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Detection;

/// <summary>
/// Stateful diff-based touchdown detector. Feed it every <see cref="LeagueSnapshot"/> in polling order.
/// State is partitioned per league (keyed by <see cref="LeagueRef.Key"/>) so multiple leagues never interfere
/// with each other, even if the same player id appears in more than one. Thread-safe: <see cref="Update"/> and
/// <see cref="Reset()"/> are guarded by an internal lock.
/// </summary>
public sealed class TouchdownDetector : ITouchdownDetector
{
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private readonly Dictionary<string, LeagueState> _leagues = new();

    public TouchdownDetector(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Current time per the injected <see cref="TimeProvider"/>. Exposed for diagnostics/testing.</summary>
    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public bool IsSeeded(string leagueKey)
    {
        lock (_lock)
        {
            return _leagues.TryGetValue(leagueKey, out var state) && state.IsSeeded;
        }
    }

    public bool AllSeeded(IEnumerable<string> leagueKeys)
    {
        ArgumentNullException.ThrowIfNull(leagueKeys);
        lock (_lock)
        {
            foreach (var key in leagueKeys)
            {
                if (!_leagues.TryGetValue(key, out var state) || !state.IsSeeded)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public int? SeededScoringPeriodId(string leagueKey)
    {
        lock (_lock)
        {
            return _leagues.TryGetValue(leagueKey, out var state) ? state.SeededScoringPeriodId : null;
        }
    }

    public IReadOnlyList<TouchdownEvent> Update(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            var leagueKey = snapshot.League.Key;
            if (!_leagues.TryGetValue(leagueKey, out var state))
            {
                state = new LeagueState();
                _leagues[leagueKey] = state;
            }

            var players = CollectPlayers(snapshot);

            if (!state.IsSeeded || state.SeededScoringPeriodId != snapshot.ScoringPeriodId)
            {
                Seed(state, players, snapshot.ScoringPeriodId);
                return Array.Empty<TouchdownEvent>();
            }

            var events = new List<TouchdownEvent>();

            foreach (var (playerId, (player, startingTeamIds, benchedTeamIds)) in players)
            {
                var newCounts = player.Touchdowns;

                if (!state.LastCounts.TryGetValue(playerId, out var oldCounts))
                {
                    // Newly appeared mid-period (e.g. waiver pickup): seed silently, no event.
                    state.LastCounts[playerId] = newCounts;
                    continue;
                }

                foreach (var type in AllTouchdownTypes)
                {
                    var oldValue = oldCounts.Get(type);
                    var newValue = newCounts.Get(type);

                    if (newValue > oldValue)
                    {
                        events.Add(new TouchdownEvent(
                            League: snapshot.League,
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

                state.LastCounts[playerId] = newCounts;
            }

            return events;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _leagues.Clear();
        }
    }

    public void Reset(string leagueKey)
    {
        lock (_lock)
        {
            _leagues.Remove(leagueKey);
        }
    }

    private static void Seed(
        LeagueState state,
        IReadOnlyDictionary<long, (RosteredPlayer Player, IReadOnlyList<int> Starting, IReadOnlyList<int> Benched)> players,
        int scoringPeriodId)
    {
        state.LastCounts.Clear();
        foreach (var (playerId, entry) in players)
        {
            state.LastCounts[playerId] = entry.Player.Touchdowns;
        }

        state.IsSeeded = true;
        state.SeededScoringPeriodId = scoringPeriodId;
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
        TouchdownType.Return,
        TouchdownType.Defensive,
    ];

    private sealed class LeagueState
    {
        public bool IsSeeded { get; set; }
        public int? SeededScoringPeriodId { get; set; }
        public Dictionary<long, TouchdownCounts> LastCounts { get; } = new();
    }
}
