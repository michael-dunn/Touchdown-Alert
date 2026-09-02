using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Tests;

public class EspnSnapshotMapperTests
{
    [Fact]
    public void Map_PreseasonFixture_DoesNotThrowAndHasTenTeamsWithEmptyRosters()
    {
        var response = TestFixtures.LoadResponse("league-2026-preseason.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        Assert.Equal(10, snapshot.Teams.Count);
        Assert.All(snapshot.Teams, t => Assert.Empty(t.Roster));
        // scoringPeriodId (0) and status.latestScoringPeriod (0) both fall back to currentMatchupPeriod (1).
        Assert.Equal(1, snapshot.ScoringPeriodId);
        Assert.Equal(1, snapshot.MatchupPeriodId);
        Assert.Equal(998946988, snapshot.LeagueId);
        Assert.Equal(2026, snapshot.SeasonId);
    }

    [Fact]
    public void Map_PreseasonFixture_TeamNamesResolveFromEspnNameField()
    {
        var response = TestFixtures.LoadResponse("league-2026-preseason.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        var team1 = snapshot.FindTeam(1);
        Assert.NotNull(team1);
        Assert.Equal("Michael's Magnificent Team", team1!.Name);
        Assert.Equal("MMT", team1.Abbreviation);
    }

    [Fact]
    public void Map_Week1Sample_HasCorrectStartersAndBench()
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        var team1 = snapshot.FindTeam(1)!;
        Assert.Equal(13, team1.Roster.Count);
        Assert.Contains(team1.Starters, p => p.PlayerId == 3918298 && p.LineupSlot == "QB");

        var team3 = snapshot.FindTeam(3)!;
        var chaseOnTeam3 = team3.Roster.Single(p => p.PlayerId == 4362628);
        Assert.True(chaseOnTeam3.IsStarter);
        Assert.Equal("WR", chaseOnTeam3.LineupSlot);

        var team5 = snapshot.FindTeam(5)!;
        var chaseOnTeam5 = team5.Roster.Single(p => p.PlayerId == 4362628);
        Assert.False(chaseOnTeam5.IsStarter);
        Assert.Equal("BE", chaseOnTeam5.LineupSlot);
    }

    [Fact]
    public void Map_Week1Sample_TouchdownCountsComeFromLiveStatLine()
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        var joshAllen = snapshot.FindTeam(1)!.Roster.Single(p => p.PlayerId == 3918298);
        Assert.Equal(1, joshAllen.Touchdowns.Passing);

        var chase = snapshot.FindTeam(3)!.Roster.Single(p => p.PlayerId == 4362628);
        Assert.Equal(0, chase.Touchdowns.Receiving);
    }

    [Fact]
    public void Map_Week1SampleAfterTd_ShowsIncrementedCounts()
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample-after-td.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        var joshAllen = snapshot.FindTeam(1)!.Roster.Single(p => p.PlayerId == 3918298);
        Assert.Equal(2, joshAllen.Touchdowns.Passing);

        var chase = snapshot.FindTeam(3)!.Roster.Single(p => p.PlayerId == 4362628);
        Assert.Equal(1, chase.Touchdowns.Receiving);
    }

    [Fact]
    public void Map_Week1Sample_HasFiveMatchupsCoveringAllTenTeams()
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        Assert.Equal(5, snapshot.Matchups.Count);
        var teamIds = snapshot.Matchups.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(1, 10), teamIds);
    }

    [Fact]
    public void Map_Week1Sample_TeamPointsArePositive()
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");

        var snapshot = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);

        Assert.All(snapshot.Teams, t => Assert.True(t.Points > 0));
    }
}
