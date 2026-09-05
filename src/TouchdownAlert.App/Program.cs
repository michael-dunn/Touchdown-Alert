using TouchdownAlert.App.Audio;
using TouchdownAlert.App.Hubs;
using TouchdownAlert.App.Services;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.DependencyInjection;
using TouchdownAlert.Core.Yahoo;

var builder = WebApplication.CreateBuilder(args);

// Optional, git-ignored personal overrides layered after appsettings.json / appsettings.{Environment}.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddTouchdownAlertCore(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddSingleton<DashboardState>();
builder.Services.AddSingleton<AlertDispatcher>();
builder.Services.AddSingleton<PollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PollingService>());

var silent = builder.Configuration.GetValue<bool?>("Sounds:Enabled") == false
    || Environment.GetEnvironmentVariable("TOUCHDOWNALERT_SILENT") == "1";
if (silent)
{
    builder.Services.AddSingleton<ISoundPlayer, NullSoundPlayer>();
}
else
{
    builder.Services.AddSingleton<ISoundPlayer, NAudioSoundPlayer>();
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<DashboardHub>("/hub");

app.MapGet("/setup/yahoo", async (HttpContext context) =>
{
    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "setup", "yahoo.html"));
});

app.MapGet("/api/yahoo/status", async (IYahooAuthService auth) => Results.Ok(await auth.GetStatusAsync()));

app.MapGet("/api/yahoo/auth-url", (IYahooAuthService auth) =>
{
    try
    {
        return Results.Ok(new { url = auth.GetAuthorizationUrl().ToString() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/yahoo/code", async (YahooCodeRequest body, IYahooAuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(body.Code))
    {
        return Results.BadRequest(new { error = "Code must not be blank." });
    }

    try
    {
        await auth.ExchangeCodeAsync(body.Code, CancellationToken.None);
        return Results.Ok(await auth.GetStatusAsync());
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/yahoo/logout", (IYahooTokenStore tokenStore) =>
{
    tokenStore.Delete();
    return Results.Ok();
});

app.MapGet("/api/state", (DashboardState state) => Results.Ok(state.ToViewModel()));

app.MapGet("/api/health", (DashboardState state) =>
{
    var vm = state.ToViewModel();
    return Results.Ok(new { ok = vm.Poll.Ok, lastPollAt = vm.Poll.LastPollAt, lastError = vm.Poll.LastError });
});

app.MapPost("/api/poll", async (PollingService polling) =>
{
    await polling.TriggerNowAsync();
    return Results.Ok();
});

app.MapPost("/api/detector/reset", (ITouchdownDetector detector, DashboardState state) =>
{
    detector.Reset();
    state.ResetDetectorSeeded();
    return Results.Ok();
});

app.MapPost("/api/detector/reset/{leagueKey}", (string leagueKey, ITouchdownDetector detector, DashboardState state) =>
{
    detector.Reset(leagueKey);
    state.ResetDetectorSeeded(leagueKey);
    return Results.Ok();
});

app.MapPost("/api/test/{leagueKey}/{teamId:int}", async (string leagueKey, int teamId, IAlertRouter router, DashboardState state, AlertDispatcher dispatcher) =>
{
    var isWatched = router.WatchedTeams.Any(w => w.TeamId == teamId && string.Equals(w.League, leagueKey, StringComparison.OrdinalIgnoreCase));
    if (!isWatched)
    {
        return Results.BadRequest(new { error = $"Team {teamId} is not a watched team in league \"{leagueKey}\"" });
    }

    var alert = router.CreateTestAlert(leagueKey, teamId, state.GetSnapshot(leagueKey));
    await dispatcher.DispatchAsync(alert);
    return Results.Ok(alert);
});

// Legacy endpoint kept for back compat: works when the team id is unambiguous across watched teams.
app.MapPost("/api/test/{teamId:int}", async (int teamId, IAlertRouter router, DashboardState state, AlertDispatcher dispatcher) =>
{
    var matches = router.WatchedTeams.Where(w => w.TeamId == teamId).ToList();
    if (matches.Count == 0)
    {
        return Results.BadRequest(new { error = $"Team {teamId} is not a watched team" });
    }

    if (matches.Count > 1)
    {
        return Results.BadRequest(new
        {
            error = $"Team {teamId} is watched in multiple leagues ({string.Join(", ", matches.Select(m => m.League))}); " +
                     $"use POST /api/test/{{leagueKey}}/{teamId} instead",
        });
    }

    var leagueKey = matches[0].League!;
    var alert = router.CreateTestAlert(leagueKey, teamId, state.GetSnapshot(leagueKey));
    await dispatcher.DispatchAsync(alert);
    return Results.Ok(alert);
});

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;

public sealed record YahooCodeRequest(string Code);
