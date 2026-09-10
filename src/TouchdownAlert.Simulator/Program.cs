using TouchdownAlert.Core.Espn.Wire;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Simulator.Simulation;

var builder = WebApplication.CreateBuilder(args);

var urls = builder.Configuration["Urls"];
if (string.IsNullOrWhiteSpace(urls) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5199");
}

builder.Services.AddSingleton<SimulatedLeague>();
builder.Services.AddSingleton<AutoPlayService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoPlayService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ---- The real ESPN-shaped endpoint ----
app.MapGet("/apis/v3/games/ffl/seasons/{seasonId:int}/segments/0/leagues/{leagueId:int}",
    (int seasonId, int leagueId, int? scoringPeriodId, SimulatedLeague league) =>
    {
        if (leagueId != SimulatedLeague.LeagueId)
        {
            return Results.NotFound(new { message = $"League {leagueId} not found." });
        }

        var response = league.ToEspnResponse();
        return Results.Json(response, EspnLeagueResponse.JsonOptions);
    });

// ---- Fake Yahoo OAuth2 token endpoint: accepts any code/refresh token, hands back an incrementing fake token ----
var yahooTokenCounter = 0;
app.MapPost("/oauth2/get_token", () =>
{
    var n = Interlocked.Increment(ref yahooTokenCounter);
    return Results.Json(new
    {
        access_token = $"sim-token-{n}",
        refresh_token = "sim-refresh",
        expires_in = 3600,
        token_type = "bearer",
        xoauth_yahoo_guid = "sim-guid",
    });
});

// ---- Yahoo-shaped XML endpoints over the SAME simulated league, for a two-source end-to-end test ----
// Segments like "scoreboard;week=2" contain a literal ';', which ASP.NET routing treats as an ordinary path
// character (no built-in matrix-parameter support), so a catch-all route parses the path by hand.
app.MapGet("/fantasy/v2/{**path}", (HttpRequest request, string path, SimulatedLeague league) =>
{
    if (!request.Headers.ContainsKey("Authorization") ||
        !request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var (segments, query) = ParseYahooPath(path);

    // league/{key}
    if (segments.Length == 2 && segments[0] == "league")
    {
        return XmlResult(YahooEmulation.BuildLeague(league, segments[1]));
    }

    // league/{key}/settings
    if (segments.Length == 3 && segments[0] == "league" && segments[2] == "settings")
    {
        return XmlResult(YahooEmulation.BuildSettings(segments[1]));
    }

    // league/{key}/scoreboard;week=N
    if (segments.Length == 3 && segments[0] == "league" && segments[2] == "scoreboard")
    {
        var week = query.TryGetValue("week", out var w) && int.TryParse(w, out var wk) ? wk : league.Week;
        return XmlResult(YahooEmulation.BuildScoreboard(league, segments[1], week));
    }

    // team/{team_key}/roster;week=N/players/stats;type=week;week=N
    if (segments.Length == 5 && segments[0] == "team" && segments[2] == "roster" && segments[3] == "players" && segments[4] == "stats")
    {
        var teamKey = segments[1];
        var tIdx = teamKey.LastIndexOf(".t.", StringComparison.Ordinal);
        if (tIdx < 0 || !int.TryParse(teamKey[(tIdx + 3)..], out var teamId))
        {
            return Results.NotFound(new { message = $"Malformed team_key '{teamKey}'." });
        }

        var leagueKey = teamKey[..tIdx];
        try
        {
            return XmlResult(YahooEmulation.BuildRoster(league, leagueKey, teamId));
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
    }

    return Results.NotFound(new { message = $"No Yahoo emulation route for '{path}'." });
});

static (string[] Segments, Dictionary<string, string> Query) ParseYahooPath(string path)
{
    var segments = new List<string>();
    var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var rawSegment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = rawSegment.Split(';');
        segments.Add(parts[0]);
        for (var i = 1; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=', 2);
            if (kv.Length == 2)
            {
                query[kv[0]] = kv[1];
            }
        }
    }

    return (segments.ToArray(), query);
}

static IResult XmlResult(System.Xml.Linq.XDocument document) =>
    Results.Text(document.ToString(), "application/xml");

// ---- Sleeper-shaped endpoints (https://api.sleeper.app/v1/...) over the SAME simulated league ----
// Sleeper's read API is public and unauthenticated, so unlike the Yahoo routes there is no bearer-token gate.
// Payloads are JsonNode graphs with the real snake_case field names (see SleeperEmulation), so Results.Json
// serializes them verbatim - no naming policy involved.
var sleeper = app.MapGroup("/v1");

sleeper.MapGet("/state/nfl", (SimulatedLeague league) => Results.Json(SleeperEmulation.BuildState(league)));

sleeper.MapGet("/league/{leagueId}", (string leagueId, SimulatedLeague league) =>
    SleeperLeagueResult(leagueId, () => SleeperEmulation.BuildLeague(league)));

sleeper.MapGet("/league/{leagueId}/users", (string leagueId, SimulatedLeague league) =>
    SleeperLeagueResult(leagueId, () => SleeperEmulation.BuildUsers(league)));

sleeper.MapGet("/league/{leagueId}/rosters", (string leagueId, SimulatedLeague league) =>
    SleeperLeagueResult(leagueId, () => SleeperEmulation.BuildRosters(league)));

// The simulator holds one scoring period, so {week} is accepted for route fidelity but not used.
sleeper.MapGet("/league/{leagueId}/matchups/{week:int}", (string leagueId, int week, SimulatedLeague league) =>
    SleeperLeagueResult(leagueId, () => SleeperEmulation.BuildMatchups(league)));

// Likewise {season}/{week}: whatever is asked for, the current period's stats come back.
sleeper.MapGet("/stats/nfl/regular/{season}/{week:int}", (string season, int week, SimulatedLeague league) =>
    Results.Json(SleeperEmulation.BuildStats(league)));

sleeper.MapGet("/players/nfl", (SimulatedLeague league) => Results.Json(SleeperEmulation.BuildPlayers(league)));

// Sleeper league ids are strings; anything but the simulated league's id is a 404, like the ESPN route.
static IResult SleeperLeagueResult(string leagueId, Func<System.Text.Json.Nodes.JsonNode> build) =>
    leagueId == SleeperEmulation.LeagueId
        ? Results.Json(build())
        : Results.NotFound(new { message = $"League {leagueId} not found." });

// ---- Control API ----
var sim = app.MapGroup("/sim");

sim.MapGet("/state", (SimulatedLeague league, AutoPlayService autoplay) => Results.Ok(new
{
    week = league.Week,
    teams = league.GetTeamSummaries(),
    autoplay = new { running = autoplay.IsRunning, intervalSeconds = autoplay.IntervalSeconds, focusTeamIds = autoplay.FocusTeamIds },
    eventLog = league.GetEventLog(),
}));

sim.MapPost("/reset", (int? week, SimulatedLeague league) =>
{
    league.Reset(week ?? 1);
    return Results.Ok(new { week = league.Week });
});

sim.MapPost("/touchdown", (TouchdownRequest body, SimulatedLeague league) =>
{
    if (!Enum.TryParse<TouchdownType>(body.Type, ignoreCase: true, out var type))
    {
        return Results.BadRequest(new { message = $"Unknown touchdown type '{body.Type}'." });
    }

    try
    {
        league.ScoreTouchdown(body.PlayerId, type, body.Count ?? 1);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }

    return Results.Ok(new { ok = true });
});

sim.MapPost("/touchdown/random", (int teamId, SimulatedLeague league) =>
{
    var rng = Random.Shared;
    var starter = league.PickRandomStarter(teamId, rng);
    if (starter is null)
    {
        return Results.NotFound(new { message = $"No team {teamId} (or it has no starters)." });
    }

    var tdType = TouchdownAlert.Simulator.Simulation.GameScript.RandomTdTypeForPosition(starter.Position, rng);
    if (tdType is null)
    {
        return Results.Ok(new { ok = false, message = $"{starter.Name} ({starter.Position}) can't score a TD.", player = starter.Name });
    }

    league.ScoreTouchdown(starter.Id, tdType.Value);
    return Results.Ok(new { ok = true, player = starter.Name, type = tdType.Value.ToString() });
});

sim.MapPost("/points", (PointsRequest body, SimulatedLeague league) =>
{
    try
    {
        league.AddPoints(body.PlayerId, body.Points);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }

    return Results.Ok(new { ok = true });
});

sim.MapPost("/autoplay/start", (int? intervalSeconds, string? focusTeamIds, AutoPlayService autoplay) =>
{
    var focus = string.IsNullOrWhiteSpace(focusTeamIds)
        ? new List<int>()
        : focusTeamIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToList();
    autoplay.Start(intervalSeconds ?? 20, focus);
    return Results.Ok(new { running = true, intervalSeconds = autoplay.IntervalSeconds, focusTeamIds = autoplay.FocusTeamIds });
});

sim.MapPost("/autoplay/stop", (AutoPlayService autoplay) =>
{
    autoplay.Stop();
    return Results.Ok(new { running = false });
});

sim.MapGet("/players", (SimulatedLeague league) => Results.Ok(league.GetAllPlayers()));

app.Run();

/// <summary>Enables the ASP.NET Core minimal API top-level program to be hosted by WebApplicationFactory in tests.</summary>
public partial class Program;

public sealed record TouchdownRequest(long PlayerId, string Type, int? Count);

public sealed record PointsRequest(long PlayerId, double Points);
