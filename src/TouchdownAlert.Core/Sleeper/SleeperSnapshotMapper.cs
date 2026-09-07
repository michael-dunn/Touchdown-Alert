using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Maps the five Sleeper responses for one week (league, users, rosters, matchups, weekly stats) plus the player
/// directory into the domain <see cref="LeagueSnapshot"/>. Pure and static so it can be unit-tested straight
/// from captured fixtures. Every roster becomes a team even without a matchup row (bye week), because watched
/// teams are keyed by roster_id and the dashboard must still show them.
/// </summary>
public static class SleeperSnapshotMapper
{
    public static LeagueSnapshot Map(
        SleeperLeague league,
        IReadOnlyList<SleeperUser> users,
        IReadOnlyList<SleeperRoster> rosters,
        IReadOnlyList<SleeperMatchup> matchups,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> stats,
        IReadOnlyDictionary<string, SleeperPlayerInfo> players,
        int week,
        LeagueRef leagueRef,
        DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(rosters);
        ArgumentNullException.ThrowIfNull(matchups);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(leagueRef);

        var usersById = new Dictionary<string, SleeperUser>(StringComparer.Ordinal);
        foreach (var user in users)
        {
            if (!string.IsNullOrEmpty(user.UserId))
            {
                usersById[user.UserId] = user;
            }
        }

        var matchupsByRoster = new Dictionary<int, SleeperMatchup>();
        foreach (var matchup in matchups)
        {
            matchupsByRoster[matchup.RosterId] = matchup;
        }

        var startingSlots = SleeperLineupSlots.StartingSlots(league.RosterPositions);

        var teams = new List<TeamSnapshot>(rosters.Count);
        foreach (var roster in rosters.OrderBy(r => r.RosterId))
        {
            var owner = roster.OwnerId is not null ? usersById.GetValueOrDefault(roster.OwnerId) : null;
            var matchup = matchupsByRoster.GetValueOrDefault(roster.RosterId);
            teams.Add(new TeamSnapshot(
                TeamId: roster.RosterId,
                Name: ResolveTeamName(roster.RosterId, owner),
                Abbreviation: string.Empty,
                Points: matchup?.Points ?? 0d,
                Roster: MapRoster(roster, matchup, startingSlots, stats, players)));
        }

        var matchupSnapshots = new List<MatchupSnapshot>();
        foreach (var pair in matchups.Where(m => m.MatchupId is not null).GroupBy(m => m.MatchupId!.Value).OrderBy(g => g.Key))
        {
            var sides = pair.OrderBy(m => m.RosterId).ToList();
            var home = sides[0];
            var away = sides.Count > 1 ? sides[1] : null;
            matchupSnapshots.Add(new MatchupSnapshot(home.RosterId, home.Points, away?.RosterId ?? 0, away?.Points ?? 0d));
        }

        return new LeagueSnapshot(
            League: leagueRef,
            LeagueName: !string.IsNullOrWhiteSpace(league.Name) ? league.Name : $"League {leagueRef.LeagueId}",
            SeasonId: int.TryParse(league.Season, out var season) ? season : fetchedAt.Year,
            ScoringPeriodId: week,
            MatchupPeriodId: week,
            FetchedAt: fetchedAt,
            Teams: teams,
            Matchups: matchupSnapshots);
    }

    /// <summary>Owner's custom team name when set, else "Team &lt;display_name&gt;", else "Team &lt;roster_id&gt;" for orphan rosters.</summary>
    public static string ResolveTeamName(int rosterId, SleeperUser? owner)
    {
        var teamName = owner?.Metadata?.TeamName?.Trim();
        if (!string.IsNullOrEmpty(teamName))
        {
            return teamName;
        }

        var displayName = owner?.DisplayName?.Trim();
        return !string.IsNullOrEmpty(displayName) ? $"Team {displayName}" : $"Team {rosterId}";
    }

    /// <summary>
    /// Orders the roster starters-first (in roster_positions order, "0" empty slots skipped), then bench, then
    /// reserve. The matchup row's starters/players win over the roster's when present: they are the lineup
    /// locked for the requested week, while the roster reflects whatever the owner has set right now.
    /// </summary>
    private static IReadOnlyList<RosteredPlayer> MapRoster(
        SleeperRoster roster,
        SleeperMatchup? matchup,
        IReadOnlyList<string> startingSlots,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> stats,
        IReadOnlyDictionary<string, SleeperPlayerInfo> players)
    {
        var starters = matchup?.Starters ?? roster.Starters ?? new List<string>();
        var allPlayers = matchup?.Players ?? roster.Players ?? new List<string>();
        var reserve = roster.Reserve ?? new List<string>();
        var points = matchup?.PlayersPoints;

        var result = new List<RosteredPlayer>(allPlayers.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < starters.Count; i++)
        {
            var id = starters[i];
            if (id == SleeperLineupSlots.EmptySlot || string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                continue;
            }

            var (slotId, slotName) = i < startingSlots.Count
                ? SleeperLineupSlots.Map(startingSlots[i])
                : (EspnLineupSlots.Flex, EspnLineupSlots.Name(EspnLineupSlots.Flex));
            result.Add(MapPlayer(id, slotId, slotName, points, stats, players));
        }

        var reserveSet = new HashSet<string>(reserve, StringComparer.Ordinal);
        foreach (var id in allPlayers)
        {
            if (string.IsNullOrEmpty(id) || reserveSet.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(MapPlayer(id, EspnLineupSlots.Bench, EspnLineupSlots.Name(EspnLineupSlots.Bench), points, stats, players));
        }

        foreach (var id in reserve)
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(MapPlayer(id, EspnLineupSlots.Ir, EspnLineupSlots.Name(EspnLineupSlots.Ir), points, stats, players));
        }

        return result;
    }

    private static RosteredPlayer MapPlayer(
        string sleeperId,
        int slotId,
        string slotName,
        IReadOnlyDictionary<string, double>? playerPoints,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> stats,
        IReadOnlyDictionary<string, SleeperPlayerInfo> players)
    {
        var isTeamDefense = SleeperIds.IsTeamDefense(sleeperId);
        var info = players.GetValueOrDefault(sleeperId);

        var fullName = info?.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = isTeamDefense ? sleeperId : $"Player {sleeperId}";
        }

        var position = info is not null
            ? SleeperLineupSlots.NormalizePosition(info.Position)
            : (isTeamDefense ? SleeperLineupSlots.NormalizePosition(SleeperLineupSlots.TeamDefense) : "?");

        var touchdowns = stats.TryGetValue(sleeperId, out var row)
            ? SleeperStatMap.FromStats(row, isTeamDefense)
            : TouchdownCounts.Zero;

        return new RosteredPlayer(
            PlayerId: SleeperIds.ToPlayerId(sleeperId),
            FullName: fullName,
            Position: position,
            LineupSlotId: slotId,
            LineupSlot: slotName,
            ProTeamId: 0,
            Points: playerPoints is not null && playerPoints.TryGetValue(sleeperId, out var p) ? p : 0d,
            Touchdowns: touchdowns);
    }
}
