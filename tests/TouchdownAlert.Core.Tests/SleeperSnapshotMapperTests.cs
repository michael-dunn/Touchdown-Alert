using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Sleeper;

namespace TouchdownAlert.Core.Tests;

public class SleeperSnapshotMapperTests
{
    private static readonly LeagueRef Ref = new("sleeper", LeagueProvider.Sleeper, SleeperTestSupport.LeagueId);
    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 13, 20, 0, 0, TimeSpan.Zero);

    private static SleeperLeague League() => SleeperTestSupport.LoadFixture<SleeperLeague>("league.json");
    private static List<SleeperUser> Users() => SleeperTestSupport.LoadFixture<List<SleeperUser>>("users.json");
    private static List<SleeperRoster> Rosters() => SleeperTestSupport.LoadFixture<List<SleeperRoster>>("rosters.json");
    private static List<SleeperMatchup> Matchups() => SleeperTestSupport.LoadFixture<List<SleeperMatchup>>("matchups-week1.json");

    private static LeagueSnapshot MapFixtures(
        SleeperLeague? league = null,
        List<SleeperRoster>? rosters = null,
        List<SleeperMatchup>? matchups = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? stats = null)
        => SleeperSnapshotMapper.Map(
            league ?? League(),
            Users(),
            rosters ?? Rosters(),
            matchups ?? Matchups(),
            stats ?? SleeperTestSupport.LoadStats("stats-2025-week1-sample.json"),
            SleeperTestSupport.LoadPlayers(),
            week: 1,
            Ref,
            FetchedAt);

    [Fact]
    public void Map_RealFixtures_LeagueHeader()
    {
        var snapshot = MapFixtures();

        Assert.Equal("Blood, Sweat and Beers", snapshot.LeagueName);
        Assert.Equal(2026, snapshot.SeasonId);
        Assert.Equal(1, snapshot.ScoringPeriodId);
        Assert.Equal(1, snapshot.MatchupPeriodId);
        Assert.Equal(FetchedAt, snapshot.FetchedAt);
        Assert.Same(Ref, snapshot.League);
        Assert.Equal(12, snapshot.Teams.Count);
    }

    [Fact]
    public void Map_TeamNames_UseMetadataTeamName_ThenDisplayName()
    {
        var snapshot = MapFixtures();

        Assert.Equal("Collusion Course", snapshot.FindTeam(1)!.Name);   // RillyBoss
        Assert.Equal("Hail Mary John", snapshot.FindTeam(5)!.Name);     // Drjay1840
        Assert.Equal("Team nruegs", snapshot.FindTeam(2)!.Name);        // no team_name in metadata
        Assert.Equal("Dimes n’ Dollas", snapshot.FindTeam(8)!.Name);
        Assert.All(snapshot.Teams, t => Assert.Equal("", t.Abbreviation));
    }

    [Fact]
    public void Map_Roster1_StartersInRosterPositionOrder_WithDetAsDst()
    {
        var team = MapFixtures().FindTeam(1)!;
        var starters = team.Starters.ToList();

        Assert.Equal(10, starters.Count);
        Assert.Equal(15, team.Roster.Count);

        Assert.Equal(4881, starters[0].PlayerId);
        Assert.Equal(EspnLineupSlots.Qb, starters[0].LineupSlotId);
        Assert.Equal("QB", starters[0].LineupSlot);
        Assert.Equal("QB", starters[0].Position);

        Assert.Equal(new[] { "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "FLEX", "K", "D/ST" }, starters.Select(s => s.LineupSlot));

        var dst = starters[9];
        Assert.Equal(SleeperIds.ToPlayerId("DET"), dst.PlayerId);
        Assert.Equal("Detroit Lions", dst.FullName);
        Assert.Equal("D/ST", dst.Position);
        Assert.Equal(EspnLineupSlots.Dst, dst.LineupSlotId);
        Assert.True(dst.IsStarter);
    }

    [Fact]
    public void Map_BenchPlayers_AreNotStarters_AndFollowStarters()
    {
        var team = MapFixtures().FindTeam(1)!;
        var bench = team.Roster.Skip(10).ToList();

        Assert.Equal(5, bench.Count);
        Assert.All(bench, p => Assert.False(p.IsStarter));
        Assert.All(bench, p => Assert.Equal(EspnLineupSlots.Bench, p.LineupSlotId));
        Assert.All(bench, p => Assert.Equal("BE", p.LineupSlot));
        Assert.All(team.Roster.Take(10), p => Assert.True(p.IsStarter));
    }

    [Fact]
    public void Map_ReservePlayers_GetIrSlot()
    {
        var team = MapFixtures().FindTeam(6)!;
        var ir = team.Roster.Single(p => p.PlayerId == 9753);

        Assert.Equal(EspnLineupSlots.Ir, ir.LineupSlotId);
        Assert.Equal("IR", ir.LineupSlot);
        Assert.False(ir.IsStarter);
        Assert.Same(ir, team.Roster[^1]);
    }

    [Fact]
    public void Map_Matchups_PairByMatchupId_LowerRosterIdIsHome()
    {
        var snapshot = MapFixtures();

        Assert.Equal(6, snapshot.Matchups.Count);
        var m5 = snapshot.Matchups.Single(m => m.HomeTeamId == 1);
        Assert.Equal(5, m5.AwayTeamId);
        Assert.Contains(snapshot.Matchups, m => m.HomeTeamId == 2 && m.AwayTeamId == 7);
        Assert.Contains(snapshot.Matchups, m => m.HomeTeamId == 10 && m.AwayTeamId == 11);
    }

    [Fact]
    public void Map_StatsApplied_RosteredPlayerWithRushTd_ShowsRushingCount()
    {
        var snapshot = MapFixtures();

        var qb = snapshot.FindTeam(1)!.Roster.Single(p => p.PlayerId == 4881);
        Assert.Equal(2, qb.Touchdowns.Passing);
        Assert.Equal(1, qb.Touchdowns.Rushing);

        var rb = snapshot.FindTeam(1)!.Roster.Single(p => p.PlayerId == 12527);
        Assert.Equal(1, rb.Touchdowns.Rushing);
        Assert.Equal(1, rb.Touchdowns.Total);

        var lamar = snapshot.FindTeam(8)!.Roster.Single(p => p.PlayerId == 4984);
        Assert.Equal(2, lamar.Touchdowns.Passing);
        Assert.Equal(2, lamar.Touchdowns.Rushing);
        Assert.True(lamar.IsStarter);

        // Every DEF starter in the fixture only has the aggregate "td" (TDs allowed) -> zero.
        Assert.All(snapshot.Teams.Select(t => t.Roster.Single(p => p.Position == "D/ST")), d => Assert.Equal(0, d.Touchdowns.Total));
    }

    [Fact]
    public void Map_PointsComeFromMatchupRow()
    {
        var matchups = Matchups();
        var row = matchups.Single(m => m.RosterId == 1);
        row.Points = 87.5;
        row.PlayersPoints!["4881"] = 21.3;

        var snapshot = MapFixtures(matchups: matchups);
        var team = snapshot.FindTeam(1)!;

        Assert.Equal(87.5, team.Points);
        Assert.Equal(21.3, team.Roster.Single(p => p.PlayerId == 4881).Points);
        Assert.Equal(0, team.Roster.Single(p => p.PlayerId == 12527).Points);
        Assert.Equal(87.5, snapshot.Matchups.Single(m => m.HomeTeamId == 1).HomePoints);
    }

    [Fact]
    public void Map_RosterWithoutMatchupRow_StillAppears_UsingRosterStarters()
    {
        var matchups = Matchups().Where(m => m.RosterId != 1 && m.RosterId != 5).ToList();

        var snapshot = MapFixtures(matchups: matchups);

        Assert.Equal(12, snapshot.Teams.Count);
        Assert.Equal(5, snapshot.Matchups.Count);
        var bye = snapshot.FindTeam(1)!;
        Assert.Equal(0, bye.Points);
        Assert.Equal(10, bye.Starters.Count());
        Assert.Equal(15, bye.Roster.Count);
    }

    [Fact]
    public void Map_EmptyStarterSlot_IsSkipped_WithoutShiftingLaterSlots()
    {
        var rosters = Rosters();
        var matchups = Matchups();
        var roster = rosters.Single(r => r.RosterId == 1);
        var row = matchups.Single(m => m.RosterId == 1);
        // Empty the QB slot: the player drops to the bench, and 12527 must still be the first RB.
        roster.Starters![0] = "0";
        row.Starters![0] = "0";

        var team = MapFixtures(rosters: rosters, matchups: matchups).FindTeam(1)!;
        var starters = team.Starters.ToList();

        Assert.Equal(9, starters.Count);
        Assert.Equal(12527, starters[0].PlayerId);
        Assert.Equal("RB", starters[0].LineupSlot);
        Assert.Equal(15, team.Roster.Count);
        Assert.False(team.Roster.Single(p => p.PlayerId == 4881).IsStarter);
        Assert.DoesNotContain(team.Roster, p => p.PlayerId == 0);
    }

    [Fact]
    public void Map_SlotsFollowLeagueRosterPositions_NotAHardcodedOrder()
    {
        // The simulator's league puts DEF before K; the real league has K before DEF.
        var league = League();
        league.RosterPositions = new List<string> { "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "DEF", "K", "BN", "BN", "BN", "BN" };
        var rosters = Rosters();
        var matchups = Matchups();
        var roster = rosters.Single(r => r.RosterId == 1);
        var row = matchups.Single(m => m.RosterId == 1);
        var starters = new List<string> { "4881", "12527", "8155", "6786", "11620", "1466", "6790", "DET", "2747" };
        roster.Starters = starters;
        row.Starters = new List<string>(starters);

        var team = MapFixtures(league: league, rosters: rosters, matchups: matchups).FindTeam(1)!;
        var mapped = team.Starters.ToList();

        Assert.Equal(9, mapped.Count);
        Assert.Equal("D/ST", mapped[7].LineupSlot);
        Assert.Equal(SleeperIds.ToPlayerId("DET"), mapped[7].PlayerId);
        Assert.Equal("K", mapped[8].LineupSlot);
        Assert.Equal(2747, mapped[8].PlayerId);
    }

    [Fact]
    public void Map_UnknownIds_FallBackToPlaceholderNames()
    {
        var rosters = Rosters();
        var matchups = Matchups();
        var roster = rosters.Single(r => r.RosterId == 1);
        var row = matchups.Single(m => m.RosterId == 1);
        roster.Starters![0] = "999999";
        roster.Starters[9] = "OAK";
        roster.Players!.AddRange(new[] { "999999", "OAK" });
        row.Starters = new List<string>(roster.Starters);
        row.Players = new List<string>(roster.Players);

        var team = MapFixtures(rosters: rosters, matchups: matchups).FindTeam(1)!;

        var unknown = team.Roster.Single(p => p.PlayerId == 999999);
        Assert.Equal("Player 999999", unknown.FullName);
        Assert.Equal("?", unknown.Position);

        var oak = team.Roster.Single(p => p.PlayerId == SleeperIds.ToPlayerId("OAK"));
        Assert.Equal("OAK", oak.FullName);
        Assert.Equal("D/ST", oak.Position);
        Assert.Equal("D/ST", oak.LineupSlot);
    }

    [Fact]
    public void Map_OrphanRoster_NamedByRosterId()
    {
        var rosters = Rosters();
        rosters.Single(r => r.RosterId == 3).OwnerId = null;

        Assert.Equal("Team 3", MapFixtures(rosters: rosters).FindTeam(3)!.Name);
    }

    [Fact]
    public void Map_UnparseableSeason_FallsBackToFetchedYear()
    {
        var league = League();
        league.Season = null;

        Assert.Equal(FetchedAt.Year, MapFixtures(league: league).SeasonId);
    }
}
