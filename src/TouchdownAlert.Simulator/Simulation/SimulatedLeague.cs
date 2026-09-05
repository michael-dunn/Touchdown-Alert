using TouchdownAlert.Core.Espn.Wire;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Simulator.Simulation;

/// <summary>One rostered slot: a player id sitting in a lineup slot on a team.</summary>
public sealed record SimRosterSlot(long PlayerId, int LineupSlotId);

/// <summary>A fantasy team in the simulated league.</summary>
public sealed class SimTeam
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Abbrev { get; init; }
    public List<SimRosterSlot> Roster { get; } = new();
}

/// <summary>
/// A player in the global player catalog. Stats/points are per-current-scoring-period and reset on <see cref="SimulatedLeague.Reset"/>.
/// The SAME player instance can be referenced from multiple teams' rosters (see the Ja'Marr Chase dual-rostering
/// scenario below) -- in real ESPN a player is only ever on one team, but the simulator deliberately allows this
/// so integration tests can exercise "started on team A, benched on team B" alert-routing behavior.
/// </summary>
public sealed class SimPlayer
{
    public required long Id { get; init; }
    public required string FullName { get; init; }
    /// <summary>"QB", "RB", "WR", "TE", "K", or "D/ST".</summary>
    public required string Position { get; init; }
    public required int ProTeamId { get; init; }
    public double Points { get; set; }
    public Dictionary<int, double> Stats { get; } = new();
}

/// <summary>A week-1-style matchup pairing two team ids.</summary>
public sealed record SimMatchup(int Id, int HomeTeamId, int AwayTeamId);

/// <summary>
/// In-memory, thread-safe (single lock) model of a simulated ESPN fantasy league. Deterministically seeded
/// so the same 10 teams/rosters appear every run. Call <see cref="ToEspnResponse"/> to get the wire shape
/// the real App's parser expects (serialize it with <see cref="EspnLeagueResponse.JsonOptions"/>).
/// </summary>
public sealed class SimulatedLeague
{
    public const int LeagueId = 998946988;
    public const int SeasonId = 2026;
    public const string LeagueName = "Trelipe Takedown";

    private readonly object _lock = new();
    private readonly Dictionary<long, SimPlayer> _players = new();
    private readonly List<SimTeam> _teams = new();
    private readonly List<SimMatchup> _matchups = new();
    private readonly List<string> _eventLog = new();
    private int _week;

    public SimulatedLeague()
    {
        Reset(1);
    }

    public int Week
    {
        get { lock (_lock) { return _week; } }
    }

    /// <summary>Rebuilds the league deterministically for the given week. All stats/points reset to zero.</summary>
    public void Reset(int week)
    {
        lock (_lock)
        {
            _players.Clear();
            _teams.Clear();
            _matchups.Clear();
            _week = week;
            RosterBuilder.Build(_players, _teams, _matchups);
            LogEventUnlocked($"Reset to week {week} ({_teams.Count} teams, {_players.Count} players)");
        }
    }

    /// <summary>Increments the TD stat counter for a player and adds the corresponding points.</summary>
    public void ScoreTouchdown(long playerId, TouchdownType type, int count = 1)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "count must be positive");
        }

        lock (_lock)
        {
            var player = GetPlayerUnlocked(playerId);
            var statId = type.ToStatId();
            player.Stats[statId] = player.Stats.GetValueOrDefault(statId) + count;
            var pointsPerTd = type == TouchdownType.Passing ? 4.0 : 6.0;
            player.Points += pointsPerTd * count;
            LogEventUnlocked($"TD: {player.FullName} ({type}) x{count} -> {player.Points:0.#} pts");
        }
    }

    /// <summary>Adds arbitrary points to a player (e.g. small yardage bumps during autoplay).</summary>
    public void AddPoints(long playerId, double points)
    {
        lock (_lock)
        {
            var player = GetPlayerUnlocked(playerId);
            player.Points += points;
            LogEventUnlocked($"+{points:0.#} pts: {player.FullName} -> {player.Points:0.#} pts");
        }
    }

    /// <summary>Directly sets a raw ESPN stat counter for a player (does not touch points).</summary>
    public void SetPlayerStat(long playerId, int statId, double value)
    {
        lock (_lock)
        {
            var player = GetPlayerUnlocked(playerId);
            player.Stats[statId] = value;
            LogEventUnlocked($"Stat set: {player.FullName} stat {statId} = {value}");
        }
    }

    public void LogEvent(string message)
    {
        lock (_lock) { LogEventUnlocked(message); }
    }

    private void LogEventUnlocked(string message)
    {
        _eventLog.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        while (_eventLog.Count > 200)
        {
            _eventLog.RemoveAt(0);
        }
    }

    private SimPlayer GetPlayerUnlocked(long playerId)
    {
        if (!_players.TryGetValue(playerId, out var player))
        {
            throw new KeyNotFoundException($"No player with id {playerId} in the simulated league.");
        }

        return player;
    }

    /// <summary>Builds the ESPN wire-format response for the current state. Callers should serialize this
    /// with <see cref="EspnLeagueResponse.JsonOptions"/>.</summary>
    public EspnLeagueResponse ToEspnResponse()
    {
        lock (_lock)
        {
            var response = new EspnLeagueResponse
            {
                Id = LeagueId,
                GameId = 1,
                SeasonId = SeasonId,
                ScoringPeriodId = _week,
                Settings = new EspnSettings { Name = LeagueName },
                Status = new EspnStatus
                {
                    CurrentMatchupPeriod = _week,
                    LatestScoringPeriod = _week,
                    FirstScoringPeriod = 1,
                    FinalScoringPeriod = 17,
                    IsActive = true,
                },
            };

            foreach (var team in _teams)
            {
                response.Teams.Add(new EspnTeam
                {
                    Id = team.Id,
                    Name = team.Name,
                    Abbrev = team.Abbrev,
                });
            }

            foreach (var matchup in _matchups)
            {
                response.Schedule.Add(new EspnMatchup
                {
                    Id = matchup.Id,
                    MatchupPeriodId = _week,
                    PlayoffTierType = "NONE",
                    Winner = "UNDECIDED",
                    Home = BuildSideUnlocked(matchup.HomeTeamId),
                    Away = BuildSideUnlocked(matchup.AwayTeamId),
                });
            }

            return response;
        }
    }

    private EspnMatchupSide BuildSideUnlocked(int teamId)
    {
        var team = _teams.First(t => t.Id == teamId);
        var entries = new List<EspnRosterEntry>();
        double starterTotal = 0;

        foreach (var slot in team.Roster)
        {
            var player = _players[slot.PlayerId];
            var isStarter = EspnLineupSlots.IsStarter(slot.LineupSlotId);
            if (isStarter)
            {
                starterTotal += player.Points;
            }

            var statLine = new EspnPlayerStats
            {
                Id = $"{_week}_0_1_{player.Id}",
                SeasonId = SeasonId,
                ScoringPeriodId = _week,
                StatSourceId = 0,
                StatSplitTypeId = 1,
                AppliedTotal = player.Points,
            };
            foreach (var (statId, value) in player.Stats)
            {
                statLine.Stats[statId.ToString()] = value;
            }

            entries.Add(new EspnRosterEntry
            {
                PlayerId = player.Id,
                LineupSlotId = slot.LineupSlotId,
                PlayerPoolEntry = new EspnPlayerPoolEntry
                {
                    Id = player.Id,
                    AppliedStatTotal = player.Points,
                    Player = new EspnPlayer
                    {
                        Id = player.Id,
                        FullName = player.FullName,
                        DefaultPositionId = PositionToDefaultPositionId(player.Position),
                        ProTeamId = player.ProTeamId,
                        Stats = new List<EspnPlayerStats> { statLine },
                    },
                },
            });
        }

        return new EspnMatchupSide
        {
            TeamId = teamId,
            TotalPoints = starterTotal,
            TotalPointsLive = starterTotal,
            RosterForCurrentScoringPeriod = new EspnRoster
            {
                AppliedStatTotal = starterTotal,
                Entries = entries,
            },
        };
    }

    private static int PositionToDefaultPositionId(string position) => position switch
    {
        "QB" => 1,
        "RB" => 2,
        "WR" => 3,
        "TE" => 4,
        "K" => 5,
        "D/ST" => 16,
        _ => 0,
    };

    // ---- Read-only views for the control API ----

    public IReadOnlyList<SimTeamSummary> GetTeamSummaries()
    {
        lock (_lock)
        {
            var result = new List<SimTeamSummary>();
            foreach (var team in _teams)
            {
                var starters = new List<SimPlayerSummary>();
                var bench = new List<SimPlayerSummary>();
                foreach (var slot in team.Roster)
                {
                    var player = _players[slot.PlayerId];
                    var summary = new SimPlayerSummary(
                        player.Id, player.FullName, player.Position, player.ProTeamId,
                        EspnLineupSlots.Name(slot.LineupSlotId), slot.LineupSlotId, player.Points,
                        new Dictionary<string, double>(player.Stats.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)));
                    if (EspnLineupSlots.IsStarter(slot.LineupSlotId))
                    {
                        starters.Add(summary);
                    }
                    else
                    {
                        bench.Add(summary);
                    }
                }

                result.Add(new SimTeamSummary(team.Id, team.Name, team.Abbrev, starters.Sum(s => s.Points), starters, bench));
            }

            return result;
        }
    }

    /// <summary>Current week's matchup pairings, for the Yahoo scoreboard emulation.</summary>
    public IReadOnlyList<SimMatchup> GetMatchups()
    {
        lock (_lock)
        {
            return _matchups.ToList();
        }
    }

    public IReadOnlyList<string> GetEventLog(int last = 50)
    {
        lock (_lock)
        {
            return _eventLog.Skip(Math.Max(0, _eventLog.Count - last)).ToList();
        }
    }

    /// <summary>All players with the team(s)/slot(s) they're rostered in (a player can be on more than one team; see class docs).</summary>
    public IReadOnlyList<SimPlayerListing> GetAllPlayers()
    {
        lock (_lock)
        {
            var byId = new Dictionary<long, List<(int TeamId, string Slot)>>();
            foreach (var team in _teams)
            {
                foreach (var slot in team.Roster)
                {
                    if (!byId.TryGetValue(slot.PlayerId, out var list))
                    {
                        list = new List<(int, string)>();
                        byId[slot.PlayerId] = list;
                    }

                    list.Add((team.Id, EspnLineupSlots.Name(slot.LineupSlotId)));
                }
            }

            return _players.Values
                .OrderBy(p => p.Id)
                .Select(p => new SimPlayerListing(
                    p.Id, p.FullName, p.Position, p.ProTeamId,
                    byId.TryGetValue(p.Id, out var rosters) ? rosters.Select(r => new SimPlayerRosterRef(r.TeamId, r.Slot)).ToList() : new List<SimPlayerRosterRef>()))
                .ToList();
        }
    }

    /// <summary>Picks a random starter on the given team, for the "random touchdown" control endpoint.</summary>
    public SimPlayerSummary? PickRandomStarter(int teamId, Random rng)
    {
        lock (_lock)
        {
            var team = _teams.FirstOrDefault(t => t.Id == teamId);
            if (team is null)
            {
                return null;
            }

            var starters = team.Roster.Where(s => EspnLineupSlots.IsStarter(s.LineupSlotId)).ToList();
            if (starters.Count == 0)
            {
                return null;
            }

            var slot = starters[rng.Next(starters.Count)];
            var player = _players[slot.PlayerId];
            return new SimPlayerSummary(player.Id, player.FullName, player.Position, player.ProTeamId,
                EspnLineupSlots.Name(slot.LineupSlotId), slot.LineupSlotId, player.Points, new Dictionary<string, double>());
        }
    }

    /// <summary>All starters across all teams, optionally weighted toward focus teams, for autoplay.</summary>
    public IReadOnlyList<(int TeamId, SimPlayerSummary Player)> GetAllStarters()
    {
        lock (_lock)
        {
            var result = new List<(int, SimPlayerSummary)>();
            foreach (var team in _teams)
            {
                foreach (var slot in team.Roster.Where(s => EspnLineupSlots.IsStarter(s.LineupSlotId)))
                {
                    var player = _players[slot.PlayerId];
                    result.Add((team.Id, new SimPlayerSummary(player.Id, player.FullName, player.Position, player.ProTeamId,
                        EspnLineupSlots.Name(slot.LineupSlotId), slot.LineupSlotId, player.Points, new Dictionary<string, double>())));
                }
            }

            return result;
        }
    }
}

public sealed record SimPlayerSummary(long Id, string Name, string Position, int ProTeamId, string Slot, int LineupSlotId, double Points, Dictionary<string, double> Stats);

public sealed record SimTeamSummary(int Id, string Name, string Abbrev, double Points, List<SimPlayerSummary> Starters, List<SimPlayerSummary> Bench);

public sealed record SimPlayerRosterRef(int TeamId, string Slot);

public sealed record SimPlayerListing(long Id, string Name, string Position, int ProTeamId, List<SimPlayerRosterRef> Rosters);
