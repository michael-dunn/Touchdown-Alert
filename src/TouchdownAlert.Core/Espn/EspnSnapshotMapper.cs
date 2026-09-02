using TouchdownAlert.Core.Espn.Wire;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Espn;

/// <summary>Maps the ESPN wire response into the domain <see cref="LeagueSnapshot"/>.</summary>
public static class EspnSnapshotMapper
{
    /// <summary>
    /// Builds a <see cref="LeagueSnapshot"/> from an ESPN league response. Never throws for the shapes ESPN
    /// actually sends, including the preseason shape where rosters are empty and scoringPeriodId is 0.
    /// </summary>
    public static LeagueSnapshot Map(EspnLeagueResponse response, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        var matchupPeriodId = response.Status?.CurrentMatchupPeriod ?? 0;
        var scoringPeriodId = ResolveScoringPeriodId(response, matchupPeriodId);

        // Index the live roster side for each team from the current matchup period's schedule entry.
        var rosterSides = new Dictionary<int, EspnMatchupSide>();
        foreach (var matchup in response.Schedule)
        {
            if (matchup.MatchupPeriodId != matchupPeriodId)
            {
                continue;
            }

            if (matchup.Home is { } home)
            {
                rosterSides[home.TeamId] = home;
            }

            if (matchup.Away is { } away)
            {
                rosterSides[away.TeamId] = away;
            }
        }

        var teams = new List<TeamSnapshot>(response.Teams.Count);
        foreach (var team in response.Teams)
        {
            teams.Add(MapTeam(team, rosterSides.GetValueOrDefault(team.Id), scoringPeriodId));
        }

        var matchups = new List<MatchupSnapshot>();
        foreach (var matchup in response.Schedule)
        {
            if (matchup.MatchupPeriodId != matchupPeriodId || matchup.Home is null)
            {
                continue;
            }

            var homePoints = ResolveTeamPoints(matchup.Home, teams);
            var awayTeamId = matchup.Away?.TeamId ?? 0;
            var awayPoints = matchup.Away is { } away ? ResolveTeamPoints(away, teams) : 0d;
            matchups.Add(new MatchupSnapshot(matchup.Home.TeamId, homePoints, awayTeamId, awayPoints));
        }

        return new LeagueSnapshot(
            LeagueId: response.Id,
            LeagueName: response.Settings?.Name ?? $"League {response.Id}",
            SeasonId: response.SeasonId,
            ScoringPeriodId: scoringPeriodId,
            MatchupPeriodId: matchupPeriodId,
            FetchedAt: fetchedAt,
            Teams: teams,
            Matchups: matchups);
    }

    private static int ResolveScoringPeriodId(EspnLeagueResponse response, int matchupPeriodId)
    {
        if (response.ScoringPeriodId != 0)
        {
            return response.ScoringPeriodId;
        }

        var latest = response.Status?.LatestScoringPeriod ?? 0;
        return latest != 0 ? latest : matchupPeriodId;
    }

    private static TeamSnapshot MapTeam(EspnTeam team, EspnMatchupSide? side, int scoringPeriodId)
    {
        var roster = new List<RosteredPlayer>();
        if (side?.RosterForCurrentScoringPeriod?.Entries is { Count: > 0 } entries)
        {
            foreach (var entry in entries)
            {
                roster.Add(MapPlayer(entry, scoringPeriodId));
            }
        }

        var points = side is null ? 0d : ResolveTeamPointsFromSide(side, roster);

        return new TeamSnapshot(
            TeamId: team.Id,
            Name: !string.IsNullOrWhiteSpace(team.Name)
                ? team.Name
                : (!string.IsNullOrWhiteSpace(team.Location) || !string.IsNullOrWhiteSpace(team.Nickname))
                    ? string.Join(' ', new[] { team.Location, team.Nickname }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : $"Team {team.Id}",
            Abbreviation: team.Abbrev ?? string.Empty,
            Points: points,
            Roster: roster);
    }

    private static double ResolveTeamPointsFromSide(EspnMatchupSide side, IReadOnlyList<RosteredPlayer> roster)
    {
        var points = side.TotalPointsLive ?? side.TotalPoints;
        if (points == 0)
        {
            var starterPoints = roster.Where(p => p.IsStarter).Sum(p => p.Points);
            if (starterPoints != 0)
            {
                return starterPoints;
            }
        }

        return points;
    }

    private static double ResolveTeamPoints(EspnMatchupSide side, IReadOnlyList<TeamSnapshot> teams)
    {
        var team = teams.FirstOrDefault(t => t.TeamId == side.TeamId);
        return team?.Points ?? (side.TotalPointsLive ?? side.TotalPoints);
    }

    private static RosteredPlayer MapPlayer(EspnRosterEntry entry, int scoringPeriodId)
    {
        var player = entry.PlayerPoolEntry?.Player;
        var fullName = player?.FullName
            ?? (player is not null ? string.Join(' ', new[] { player.FirstName, player.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) : null);
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = $"Player {entry.PlayerId}";
        }

        var liveStats = player?.Stats.FirstOrDefault(s => s.IsActual && s.ScoringPeriodId == scoringPeriodId);

        var points = liveStats?.AppliedTotal ?? entry.PlayerPoolEntry?.AppliedStatTotal ?? 0d;
        var touchdowns = liveStats is not null
            ? TouchdownCounts.FromEspnStats(liveStats.Stats)
            : TouchdownCounts.Zero;

        return new RosteredPlayer(
            PlayerId: entry.PlayerId,
            FullName: fullName,
            Position: EspnPositions.Name(player?.DefaultPositionId ?? 0),
            LineupSlotId: entry.LineupSlotId,
            LineupSlot: EspnLineupSlots.Name(entry.LineupSlotId),
            ProTeamId: player?.ProTeamId ?? 0,
            Points: points,
            Touchdowns: touchdowns);
    }
}
