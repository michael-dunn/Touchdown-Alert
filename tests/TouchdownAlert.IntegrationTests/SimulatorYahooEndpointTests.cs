using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;

namespace TouchdownAlert.IntegrationTests;

/// <summary>Verifies the simulator's Yahoo-shaped XML emulation: shape, auth gate, and the fake token endpoint.</summary>
public sealed class SimulatorYahooEndpointTests : IClassFixture<SimulatorHostFixture>
{
    private readonly SimulatorHostFixture _fixture;

    public SimulatorYahooEndpointTests(SimulatorHostFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage Authed(string path) =>
        new(HttpMethod.Get, path) { Headers = { { "Authorization", "Bearer sim-token" } } };

    [Fact]
    public async Task League_endpoint_requires_bearer_token()
    {
        var response = await _fixture.Client.GetAsync("/fantasy/v2/league/nfl.l.998946988");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_endpoint_returns_fake_token_for_any_code()
    {
        var response = await _fixture.Client.PostAsync("/oauth2/get_token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "whatever",
        }));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.StartsWith("sim-token-", json.GetProperty("access_token").GetString());
        Assert.Equal("sim-refresh", json.GetProperty("refresh_token").GetString());
        Assert.Equal(3600, json.GetProperty("expires_in").GetInt32());
    }

    [Fact]
    public async Task League_endpoint_has_expected_shape()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);

        var response = await _fixture.Client.SendAsync(Authed("/fantasy/v2/league/nfl.l.998946988"));
        response.EnsureSuccessStatusCode();
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("nfl.l.998946988", Value(doc, "league_key"));
        Assert.Equal("998946988", Value(doc, "league_id"));
        Assert.Equal("Trelipe Takedown", Value(doc, "name"));
        Assert.Equal("1", Value(doc, "current_week"));
        Assert.Equal("10", Value(doc, "num_teams"));
    }

    [Fact]
    public async Task Settings_endpoint_has_expected_stat_categories()
    {
        var response = await _fixture.Client.SendAsync(Authed("/fantasy/v2/league/nfl.l.998946988/settings"));
        response.EnsureSuccessStatusCode();
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());

        var statIds = doc.Descendants().Where(e => e.Name.LocalName == "stat_id").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "5", "10", "13", "35", "49" }, statIds);

        var defTd = doc.Descendants().First(e => e.Name.LocalName == "stat" &&
            e.Elements().First(c => c.Name.LocalName == "stat_id").Value == "35");
        Assert.Equal("DT", defTd.Elements().First(c => c.Name.LocalName == "position_type").Value);
    }

    [Fact]
    public async Task Scoreboard_endpoint_has_two_teams_per_matchup()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);

        var response = await _fixture.Client.SendAsync(Authed("/fantasy/v2/league/nfl.l.998946988/scoreboard;week=1"));
        response.EnsureSuccessStatusCode();
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());

        var matchups = doc.Descendants().Where(e => e.Name.LocalName == "matchup").ToList();
        Assert.Equal(5, matchups.Count);
        foreach (var matchup in matchups)
        {
            var teams = matchup.Descendants().Where(e => e.Name.LocalName == "team").ToList();
            Assert.Equal(2, teams.Count);
        }
    }

    [Fact]
    public async Task Roster_endpoint_reflects_scored_touchdown()
    {
        await _fixture.Client.PostAsync("/sim/reset?week=1", content: null);
        (await _fixture.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 3918298, type = "Passing", count = 1 })).EnsureSuccessStatusCode();

        var response = await _fixture.Client.SendAsync(Authed("/fantasy/v2/team/nfl.l.998946988.t.1/roster;week=1/players/stats;type=week;week=1"));
        response.EnsureSuccessStatusCode();
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());

        var player = doc.Descendants().First(e => e.Name.LocalName == "player" &&
            e.Elements().First(c => c.Name.LocalName == "player_id").Value == "3918298");

        var selectedPosition = player.Descendants().First(e => e.Name.LocalName == "selected_position")
            .Elements().First(c => c.Name.LocalName == "position").Value;
        Assert.Equal("QB", selectedPosition);

        var stat = player.Descendants().First(e => e.Name.LocalName == "player_stats")
            .Descendants().First(e => e.Name.LocalName == "stat");
        Assert.Equal("5", stat.Elements().First(c => c.Name.LocalName == "stat_id").Value);
        Assert.Equal("1", stat.Elements().First(c => c.Name.LocalName == "value").Value);
    }

    [Fact]
    public async Task Unknown_route_under_fantasy_v2_returns_404()
    {
        var response = await _fixture.Client.SendAsync(Authed("/fantasy/v2/nonsense"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string? Value(XDocument doc, string localName) =>
        doc.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
}
