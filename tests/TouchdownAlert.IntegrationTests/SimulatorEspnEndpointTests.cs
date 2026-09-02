using System.Net;
using System.Net.Http.Json;
using TouchdownAlert.Core.Espn.Wire;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.IntegrationTests;

/// <summary>
/// Verifies the simulator's ESPN-shaped endpoint round-trips correctly through the SAME DTOs/JSON options
/// the real App's parser uses (<see cref="EspnLeagueResponse.JsonOptions"/>), and that reset/touchdown
/// control operations are visible in that JSON.
/// </summary>
public sealed class SimulatorEspnEndpointTests : IClassFixture<SimulatorHostFixture>
{
    private const string EspnPath = "/apis/v3/games/ffl/seasons/2026/segments/0/leagues/998946988";
    private readonly SimulatorHostFixture _fixture;

    public SimulatorEspnEndpointTests(SimulatorHostFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<EspnLeagueResponse> GetLeagueAsync(string query = "?view=mBoxscore&view=mMatchupScore&view=mTeam&view=mSettings")
    {
        var response = await _fixture.Client.GetAsync(EspnPath + query);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var league = System.Text.Json.JsonSerializer.Deserialize<EspnLeagueResponse>(json, EspnLeagueResponse.JsonOptions);
        Assert.NotNull(league);
        return league!;
    }

    [Fact]
    public async Task League_endpoint_returns_ok_and_ignores_view_params()
    {
        var response = await _fixture.Client.GetAsync(EspnPath + "?view=mBoxscore&view=mSomethingUnknown");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_league_id_returns_404()
    {
        var response = await _fixture.Client.GetAsync("/apis/v3/games/ffl/seasons/2026/segments/0/leagues/1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task League_has_expected_shape()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
        var league = await GetLeagueAsync();

        Assert.Equal(998946988, league.Id);
        Assert.Equal(2026, league.SeasonId);
        Assert.Equal(1, league.ScoringPeriodId);
        Assert.Equal("Trelipe Takedown", league.Settings?.Name);
        Assert.Equal(1, league.Status?.CurrentMatchupPeriod);
        Assert.Equal(1, league.Status?.LatestScoringPeriod);
        Assert.Equal(10, league.Teams.Count);
        Assert.Equal(5, league.Schedule.Count);

        foreach (var matchup in league.Schedule)
        {
            Assert.NotNull(matchup.Home);
            Assert.NotNull(matchup.Away);
            AssertSideHasRosterWithOneActualStatLinePerPlayer(matchup.Home!, league.ScoringPeriodId, league.SeasonId);
            AssertSideHasRosterWithOneActualStatLinePerPlayer(matchup.Away!, league.ScoringPeriodId, league.SeasonId);
        }
    }

    private static void AssertSideHasRosterWithOneActualStatLinePerPlayer(EspnMatchupSide side, int scoringPeriodId, int seasonId)
    {
        Assert.NotNull(side.RosterForCurrentScoringPeriod);
        var entries = side.RosterForCurrentScoringPeriod!.Entries;
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            Assert.NotNull(entry.PlayerPoolEntry);
            Assert.NotNull(entry.PlayerPoolEntry!.Player);
            var stats = entry.PlayerPoolEntry.Player!.Stats;
            var actual = stats.Where(s => s.IsActual).ToList();
            Assert.Single(actual);
            var line = actual[0];
            Assert.Equal(scoringPeriodId, line.ScoringPeriodId);
            Assert.Equal(seasonId, line.SeasonId);
            Assert.Equal(1, line.StatSplitTypeId);
            Assert.Equal(entry.PlayerPoolEntry.AppliedStatTotal, line.AppliedTotal);
        }
    }

    [Fact]
    public async Task ScoreTouchdown_increments_stat_and_points_and_side_total()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
        var before = await GetLeagueAsync();
        var (beforeEntry, beforeSide) = FindPlayer(before, 4362628);
        var beforePoints = beforeEntry.PlayerPoolEntry!.AppliedStatTotal;
        var beforeSideTotal = beforeSide.TotalPointsLive ?? beforeSide.TotalPoints;

        var response = await _fixture.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 4362628, type = "Receiving", count = 1 });
        response.EnsureSuccessStatusCode();

        var after = await GetLeagueAsync();
        var (afterEntry, afterSide) = FindPlayer(after, 4362628);
        var afterPoints = afterEntry.PlayerPoolEntry!.AppliedStatTotal;
        var afterSideTotal = afterSide.TotalPointsLive ?? afterSide.TotalPoints;

        var actualLine = afterEntry.PlayerPoolEntry.Player!.Stats.Single(s => s.IsActual);
        Assert.Equal(1, actualLine.Stats[EspnStatIds.ReceivingTd.ToString()]);
        Assert.Equal(beforePoints + 6, afterPoints);

        // Only assert the side total moved if Chase was a STARTER on that side (team 3); on team 5 he's benched.
        if (EspnLineupSlots.IsStarter(afterEntry.LineupSlotId))
        {
            Assert.Equal(beforeSideTotal + 6, afterSideTotal);
        }
    }

    private static (EspnRosterEntry Entry, EspnMatchupSide Side) FindPlayer(EspnLeagueResponse league, long playerId)
    {
        foreach (var matchup in league.Schedule)
        {
            foreach (var side in new[] { matchup.Home, matchup.Away })
            {
                var entry = side?.RosterForCurrentScoringPeriod?.Entries.FirstOrDefault(e => e.PlayerId == playerId);
                if (entry is not null)
                {
                    return (entry, side!);
                }
            }
        }

        throw new InvalidOperationException($"Player {playerId} not found in any roster.");
    }

    [Fact]
    public async Task Chase_starts_on_team_3_and_is_benched_on_team_5()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
        var league = await GetLeagueAsync();

        var team3Side = SideForTeam(league, 3);
        var team5Side = SideForTeam(league, 5);

        var onTeam3 = team3Side.RosterForCurrentScoringPeriod!.Entries.Single(e => e.PlayerId == 4362628);
        var onTeam5 = team5Side.RosterForCurrentScoringPeriod!.Entries.Single(e => e.PlayerId == 4362628);

        Assert.True(EspnLineupSlots.IsStarter(onTeam3.LineupSlotId), "Chase should be a starter on team 3.");
        Assert.False(EspnLineupSlots.IsStarter(onTeam5.LineupSlotId), "Chase should be benched on team 5.");
    }

    private static EspnMatchupSide SideForTeam(EspnLeagueResponse league, int teamId)
    {
        foreach (var matchup in league.Schedule)
        {
            if (matchup.Home?.TeamId == teamId)
            {
                return matchup.Home;
            }

            if (matchup.Away?.TeamId == teamId)
            {
                return matchup.Away;
            }
        }

        throw new InvalidOperationException($"Team {teamId} not found in schedule.");
    }
}
