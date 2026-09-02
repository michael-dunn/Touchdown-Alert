using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TouchdownAlert.IntegrationTests;

/// <summary>Verifies the simulator's own /sim control API (used by the manual console and by test setup).</summary>
public sealed class SimulatorControlApiTests : IClassFixture<SimulatorHostFixture>
{
    private readonly SimulatorHostFixture _fixture;

    public SimulatorControlApiTests(SimulatorHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Reset_returns_the_requested_week()
    {
        var response = await _fixture.Client.PostAsync("/sim/reset?week=3", content: null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, json.GetProperty("week").GetInt32());

        // Restore week 1 for other tests in this fixture's shared server.
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
    }

    [Fact]
    public async Task State_lists_10_teams_with_starters_and_bench()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
        var json = await _fixture.Client.GetFromJsonAsync<JsonElement>("/sim/state");

        Assert.Equal(1, json.GetProperty("week").GetInt32());
        var teams = json.GetProperty("teams");
        Assert.Equal(10, teams.GetArrayLength());

        foreach (var team in teams.EnumerateArray())
        {
            Assert.True(team.GetProperty("starters").GetArrayLength() > 0);
            Assert.True(team.GetProperty("bench").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Touchdown_endpoint_rejects_unknown_type()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 4362628, type = "NotARealType", count = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Touchdown_endpoint_404s_for_unknown_player()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 999999999L, type = "Rushing", count = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Random_touchdown_scores_a_starter_on_the_requested_team()
    {
        // A starter is picked uniformly at random, and the team-1 lineup includes a kicker who can't score
        // a TD (ok:false with an explanatory message in that case) - so try a few times to also see an
        // actual scoring outcome (ok:true with a player name) rather than asserting every single call scores.
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);

        var sawScore = false;
        for (var i = 0; i < 15 && !sawScore; i++)
        {
            var response = await _fixture.Client.PostAsync("/sim/touchdown/random?teamId=1", content: null);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (json.GetProperty("ok").GetBoolean())
            {
                sawScore = true;
                Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("player").GetString()));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
            }
        }

        Assert.True(sawScore, "Expected at least one of 15 random-TD attempts on team 1 to score (only the kicker can't).");
    }

    [Fact]
    public async Task Random_touchdown_404s_for_unknown_team()
    {
        var response = await _fixture.Client.PostAsync("/sim/touchdown/random?teamId=999", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Points_endpoint_adds_points_without_touching_stats()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
        var response = await _fixture.Client.PostAsJsonAsync("/sim/points", new { playerId = 4362628, points = 2.5 });
        response.EnsureSuccessStatusCode();

        var state = await _fixture.Client.GetFromJsonAsync<JsonElement>("/sim/state");
        var chase = FindPlayerInState(state, 4362628);
        Assert.Equal(2.5, chase.GetProperty("points").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task Players_endpoint_lists_chase_on_both_team_3_and_team_5()
    {
        var players = await _fixture.Client.GetFromJsonAsync<JsonElement>("/sim/players");
        var chase = players.EnumerateArray().Single(p => p.GetProperty("id").GetInt64() == 4362628);
        var rosters = chase.GetProperty("rosters").EnumerateArray().Select(r => r.GetProperty("teamId").GetInt32()).ToList();
        Assert.Contains(3, rosters);
        Assert.Contains(5, rosters);
    }

    [Fact]
    public async Task Autoplay_start_and_stop_report_running_state()
    {
        var start = await _fixture.Client.PostAsync("/sim/autoplay/start?intervalSeconds=30&focusTeamIds=1,3", content: null);
        start.EnsureSuccessStatusCode();
        var startJson = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(startJson.GetProperty("running").GetBoolean());
        Assert.Equal(30, startJson.GetProperty("intervalSeconds").GetInt32());

        var state = await _fixture.Client.GetFromJsonAsync<JsonElement>("/sim/state");
        Assert.True(state.GetProperty("autoplay").GetProperty("running").GetBoolean());

        var stop = await _fixture.Client.PostAsync("/sim/autoplay/stop", content: null);
        stop.EnsureSuccessStatusCode();
        var stopJson = await stop.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(stopJson.GetProperty("running").GetBoolean());
    }

    private static JsonElement FindPlayerInState(JsonElement state, long playerId)
    {
        foreach (var team in state.GetProperty("teams").EnumerateArray())
        {
            foreach (var listName in new[] { "starters", "bench" })
            {
                foreach (var p in team.GetProperty(listName).EnumerateArray())
                {
                    if (p.GetProperty("id").GetInt64() == playerId)
                    {
                        return p;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Player {playerId} not found in /sim/state.");
    }
}
