using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TouchdownAlert.IntegrationTests;

/// <summary>
/// Verifies the simulator's Sleeper-shaped JSON emulation: route shapes, string-typed ids/season, roster_positions
/// contract, and the ESPN-stat-id -> Sleeper-key translation (including the decoy keys a correct parser must ignore).
/// Uses raw JsonElement rather than Core DTOs on purpose: the emulation is the wire contract the Core parser is
/// tested against, so these tests must not share types with it.
/// </summary>
public sealed class SimulatorSleeperEndpointTests : IClassFixture<SimulatorHostFixture>
{
    private const string LeagueId = "998946988";
    private const string LeaguePath = "/v1/league/" + LeagueId;

    // Well-known simulated ids (see RosterBuilder): Josh Allen QB team 1, Ja'Marr Chase WR team 3, Baltimore D/ST team 1.
    private const long JoshAllen = 3918298;
    private const long JaMarrChase = 4362628;
    private const long BaltimoreDst = -16033;
    private const string BaltimoreAbbr = "BAL";

    private static readonly Regex DefIdPattern = new("^[A-Z]{2,3}$");

    private readonly SimulatorHostFixture _fixture;

    public SimulatorSleeperEndpointTests(SimulatorHostFixture fixture)
    {
        _fixture = fixture;
    }

    private Task ResetAsync(int week = 1) => _fixture.Client.PostAsync($"/sim/reset?week={week}", content: null);

    private async Task ScoreAsync(long playerId, string type)
    {
        var response = await _fixture.Client.PostAsJsonAsync("/sim/touchdown", new { playerId, type, count = 1 });
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _fixture.Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<JsonElement> GetStatsAsync() => GetAsync("/v1/stats/nfl/regular/2026/1");

    private static IEnumerable<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!);

    // ---- state ----

    [Fact]
    public async Task State_week_matches_sim_state_after_reset()
    {
        await ResetAsync(week: 3);
        try
        {
            var state = await GetAsync("/v1/state/nfl");
            var simState = await GetAsync("/sim/state");

            Assert.Equal(3, simState.GetProperty("week").GetInt32());
            Assert.Equal(3, state.GetProperty("week").GetInt32());
            Assert.Equal(3, state.GetProperty("display_week").GetInt32());
            Assert.Equal("2026", state.GetProperty("season").GetString());
            Assert.Equal("regular", state.GetProperty("season_type").GetString());
        }
        finally
        {
            await ResetAsync(week: 1);
        }
    }

    // ---- league ----

    [Fact]
    public async Task League_has_string_season_name_team_count_and_roster_positions()
    {
        await ResetAsync();
        var league = await GetAsync(LeaguePath);
        var teamCount = (await GetAsync("/sim/state")).GetProperty("teams").GetArrayLength();

        Assert.Equal(LeagueId, league.GetProperty("league_id").GetString());
        Assert.Equal(JsonValueKind.String, league.GetProperty("season").ValueKind);
        Assert.Equal("2026", league.GetProperty("season").GetString());
        Assert.Equal("Trelipe Takedown", league.GetProperty("name").GetString());
        Assert.Equal(teamCount, league.GetProperty("settings").GetProperty("num_teams").GetInt32());
        Assert.Equal(teamCount, league.GetProperty("total_rosters").GetInt32());

        var positions = Strings(league.GetProperty("roster_positions")).ToList();
        Assert.Equal(new[] { "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "DEF", "K", "BN", "BN", "BN", "BN" }, positions);

        // Scoring settings expose the TD keys the parser may use to discover scored stats.
        var scoring = league.GetProperty("scoring_settings");
        Assert.Equal(4.0, scoring.GetProperty("pass_td").GetDouble());
        Assert.Equal(6.0, scoring.GetProperty("def_td").GetDouble());
    }

    // ---- users / rosters ----

    [Fact]
    public async Task Users_one_per_team_with_null_metadata_on_last_team()
    {
        await ResetAsync();
        var users = await GetAsync(LeaguePath + "/users");
        var teams = (await GetAsync("/sim/state")).GetProperty("teams");

        Assert.Equal(teams.GetArrayLength(), users.GetArrayLength());

        var byId = users.EnumerateArray().ToDictionary(u => u.GetProperty("user_id").GetString()!);
        foreach (var team in teams.EnumerateArray())
        {
            var teamId = team.GetProperty("id").GetInt32();
            var user = byId["100000000000000" + teamId];
            Assert.Equal(LeagueId, user.GetProperty("league_id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(user.GetProperty("display_name").GetString()));

            var metadata = user.GetProperty("metadata");
            if (teamId == 10)
            {
                // Exercises the parser's fallback from metadata.team_name to display_name.
                Assert.Equal(JsonValueKind.Null, metadata.ValueKind);
            }
            else
            {
                Assert.Equal(team.GetProperty("name").GetString(), metadata.GetProperty("team_name").GetString());
            }
        }

        Assert.True(byId["1000000000000001"].GetProperty("is_owner").GetBoolean());
    }

    [Fact]
    public async Task Rosters_one_per_team_with_positional_starters()
    {
        await ResetAsync();
        var league = await GetAsync(LeaguePath);
        var rosters = await GetAsync(LeaguePath + "/rosters");
        var users = await GetAsync(LeaguePath + "/users");

        var starterSlots = Strings(league.GetProperty("roster_positions")).Count(p => p != "BN");
        var userIds = users.EnumerateArray().Select(u => u.GetProperty("user_id").GetString()).ToHashSet();
        var teamCount = league.GetProperty("total_rosters").GetInt32();

        var rosterIds = rosters.EnumerateArray().Select(r => r.GetProperty("roster_id").GetInt32()).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(1, teamCount), rosterIds);

        foreach (var roster in rosters.EnumerateArray())
        {
            Assert.Equal(LeagueId, roster.GetProperty("league_id").GetString());
            Assert.Contains(roster.GetProperty("owner_id").GetString(), userIds);
            Assert.Equal(JsonValueKind.Null, roster.GetProperty("reserve").ValueKind);

            var starters = Strings(roster.GetProperty("starters")).ToList();
            var players = Strings(roster.GetProperty("players")).ToHashSet();
            Assert.Equal(starterSlots, starters.Count);
            Assert.All(starters, id => Assert.Contains(id, players));

            // roster_positions has DEF at index 7, so the DEF starter must be a 2-3 letter abbreviation there.
            Assert.Matches(DefIdPattern, starters[7]);
            // ...and every other starter is a numeric id string.
            foreach (var (id, index) in starters.Select((id, i) => (id, i)).Where(x => x.i != 7))
            {
                Assert.True(long.TryParse(id, out _), $"starter[{index}] '{id}' should be a numeric id");
            }
        }
    }

    // ---- matchups ----

    [Fact]
    public async Task Matchups_cover_players_and_reflect_touchdowns()
    {
        await ResetAsync();
        var before = (await GetAsync(LeaguePath + "/matchups/1")).EnumerateArray().ToDictionary(m => m.GetProperty("roster_id").GetInt32());
        var rosters = (await GetAsync(LeaguePath + "/rosters")).EnumerateArray().ToDictionary(r => r.GetProperty("roster_id").GetInt32());

        Assert.Equal(rosters.Count, before.Count);
        foreach (var (rosterId, matchup) in before)
        {
            var players = Strings(rosters[rosterId].GetProperty("players")).ToHashSet();
            var pointKeys = matchup.GetProperty("players_points").EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.Equal(players, pointKeys);
            Assert.Equal(Strings(rosters[rosterId].GetProperty("starters")), Strings(matchup.GetProperty("starters")));
            Assert.Equal(matchup.GetProperty("starters").GetArrayLength(), matchup.GetProperty("starters_points").GetArrayLength());
            Assert.Equal(JsonValueKind.Number, matchup.GetProperty("matchup_id").ValueKind);
        }

        // Opponents share a matchup_id, two per id.
        var pairs = before.Values.GroupBy(m => m.GetProperty("matchup_id").GetInt32()).ToList();
        Assert.Equal(rosters.Count / 2, pairs.Count);
        Assert.All(pairs, g => Assert.Equal(2, g.Count()));

        // Josh Allen starts at QB for team 1; a passing TD is +4 for him and his team.
        await ScoreAsync(JoshAllen, "Passing");
        var after = (await GetAsync(LeaguePath + "/matchups/1")).EnumerateArray().First(m => m.GetProperty("roster_id").GetInt32() == 1);

        var allenBefore = before[1].GetProperty("players_points").GetProperty(JoshAllen.ToString()).GetDouble();
        var allenAfter = after.GetProperty("players_points").GetProperty(JoshAllen.ToString()).GetDouble();
        Assert.Equal(allenBefore + 4.0, allenAfter);
        Assert.Equal(before[1].GetProperty("points").GetDouble() + 4.0, after.GetProperty("points").GetDouble());
        Assert.Equal(allenAfter, after.GetProperty("starters_points")[0].GetDouble());
    }

    // ---- stats ----

    [Fact]
    public async Task Stats_passing_touchdown_becomes_pass_td()
    {
        await ResetAsync();
        await ScoreAsync(JoshAllen, "Passing");

        var row = (await GetStatsAsync()).GetProperty(JoshAllen.ToString());
        Assert.Equal(1, row.GetProperty("pass_td").GetDouble());
        Assert.Equal(4.0, row.GetProperty("pts_std").GetDouble());
        Assert.Equal(1, row.GetProperty("gp").GetInt32());
        Assert.False(row.TryGetProperty("rush_td", out _));
        Assert.False(row.TryGetProperty("anytime_tds", out _), "a passing TD is not an anytime TD");
    }

    [Fact]
    public async Task Stats_receiving_touchdown_becomes_rec_td_with_anytime_decoy()
    {
        await ResetAsync();
        await ScoreAsync(JaMarrChase, "Receiving");

        var row = (await GetStatsAsync()).GetProperty(JaMarrChase.ToString());
        Assert.Equal(1, row.GetProperty("rec_td").GetDouble());
        Assert.Equal(1, row.GetProperty("anytime_tds").GetDouble());
        Assert.Equal(6.0, row.GetProperty("pts_std").GetDouble());
    }

    [Fact]
    public async Task Stats_kick_return_touchdown_on_offensive_player_becomes_st_td_and_kr_td()
    {
        await ResetAsync();
        await ScoreAsync(JaMarrChase, "KickReturn");

        var row = (await GetStatsAsync()).GetProperty(JaMarrChase.ToString());
        Assert.Equal(1, row.GetProperty("st_td").GetDouble());
        Assert.Equal(1, row.GetProperty("kr_td").GetDouble());
        Assert.False(row.TryGetProperty("pr_td", out _));
        Assert.False(row.TryGetProperty("def_st_td", out _), "def_st_td is a team-defense key");
    }

    [Fact]
    public async Task Stats_dst_interception_return_becomes_def_td_on_abbreviation_row_and_every_def_row_has_td_decoy()
    {
        await ResetAsync();
        await ScoreAsync(BaltimoreDst, "InterceptionReturn");

        var stats = await GetStatsAsync();
        Assert.False(stats.TryGetProperty(BaltimoreDst.ToString(), out _), "D/ST rows are keyed by abbreviation, not ESPN id");

        var ravens = stats.GetProperty(BaltimoreAbbr);
        Assert.Equal(1, ravens.GetProperty("def_td").GetDouble());
        Assert.False(ravens.TryGetProperty("idp_def_td", out _), "idp_def_td is an individual-player key");
        Assert.False(ravens.TryGetProperty("def_st_td", out _));
        Assert.Equal(6.0, ravens.GetProperty("pts_std").GetDouble());

        var players = await GetAsync("/v1/players/nfl");
        var defRows = stats.EnumerateObject()
            .Where(p => players.GetProperty(p.Name).GetProperty("position").GetString() == "DEF")
            .ToList();
        Assert.Equal(10, defRows.Count);
        Assert.All(defRows, p => Assert.Equal(3, p.Value.GetProperty("td").GetInt32()));
    }

    [Fact]
    public async Task Stats_omit_offensive_players_with_nothing_to_report()
    {
        await ResetAsync();
        var stats = await GetStatsAsync();
        Assert.False(stats.TryGetProperty(JoshAllen.ToString(), out _));
    }

    // ---- players ----

    [Fact]
    public async Task Players_include_every_rostered_id_with_def_units_named_city_nickname()
    {
        await ResetAsync();
        var players = await GetAsync("/v1/players/nfl");
        var rosters = await GetAsync(LeaguePath + "/rosters");

        foreach (var roster in rosters.EnumerateArray())
        {
            foreach (var id in Strings(roster.GetProperty("players")))
            {
                Assert.True(players.TryGetProperty(id, out var entry), $"players/nfl is missing rostered id {id}");
                Assert.Equal(id, entry.GetProperty("player_id").GetString());
                Assert.Equal("nfl", entry.GetProperty("sport").GetString());
            }
        }

        var ravens = players.GetProperty(BaltimoreAbbr);
        Assert.Equal("DEF", ravens.GetProperty("position").GetString());
        Assert.Equal("Baltimore", ravens.GetProperty("first_name").GetString());
        Assert.Equal("Ravens", ravens.GetProperty("last_name").GetString());
        Assert.Equal("Baltimore Ravens", ravens.GetProperty("full_name").GetString());
        Assert.Equal(BaltimoreAbbr, ravens.GetProperty("team").GetString());
        Assert.Equal(new[] { "DEF" }, Strings(ravens.GetProperty("fantasy_positions")));

        var allen = players.GetProperty(JoshAllen.ToString());
        Assert.Equal("QB", allen.GetProperty("position").GetString());
        Assert.Equal("Josh Allen", allen.GetProperty("full_name").GetString());
        Assert.Equal("BUF", allen.GetProperty("team").GetString());
    }

    // ---- errors ----

    [Theory]
    [InlineData("/v1/league/1")]
    [InlineData("/v1/league/1/users")]
    [InlineData("/v1/league/1/rosters")]
    [InlineData("/v1/league/1/matchups/1")]
    public async Task Unknown_league_id_returns_404(string path)
    {
        var response = await _fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
