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
