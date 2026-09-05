using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TouchdownAlert.App.Audio;
using TouchdownAlert.App.Contracts;
using TouchdownAlert.App.Hubs;
using TouchdownAlert.App.Services;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.DependencyInjection;
using TouchdownAlert.Core.Yahoo;

var builder = WebApplication.CreateBuilder(args);

// Optional, git-ignored personal overrides layered after appsettings.json / appsettings.{Environment}.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// config/settings.json is the ONLY place Leagues, Alerts:WatchedTeams, Polling:IntervalSeconds,
// Sounds:Volume/MaxDurationSeconds and Overlay live - everything else (Urls, Yahoo credentials,
// Sounds:Directory/Enabled, logging) stays in appsettings*.json. Tests point this at a temp file via
// "Settings:FilePath"; UseSetting overrides (as EndToEndTests uses) still win over whatever's in the file.
var settingsFileSetting = builder.Configuration["Settings:FilePath"] ?? "config/settings.json";
var settingsFilePath = RepoPaths.Resolve(settingsFileSetting);
var settingsFileCreated = EnsureSettingsFileExists(settingsFilePath);

var settingsDirectory = Path.GetDirectoryName(settingsFilePath)!;
Directory.CreateDirectory(settingsDirectory);

// Inserted at the very front of the configuration source chain (rather than appended, which is what
// AddJsonFile does) so it acts like a lowest-priority "base layer": WebApplicationFactory's UseSetting-based
// test overrides (and command-line args, env vars, appsettings.json, etc.) are already present in
// builder.Configuration by this point and must keep winning over whatever this file contains.
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    FileProvider = new PhysicalFileProvider(settingsDirectory),
    Path = Path.GetFileName(settingsFilePath),
    Optional = true,
    ReloadOnChange = true,
});

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddTouchdownAlertCore(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<DashboardState>();
builder.Services.AddSingleton<AlertDispatcher>();
builder.Services.AddSingleton<PollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PollingService>());

// Captures the leagues (Key/Provider/LeagueId) the process started with, so the settings API can tell the
// control page when adding/removing a league or changing its provider/id requires a restart to take effect.
builder.Services.AddSingleton(sp =>
{
    var startupLeagues = sp.GetRequiredService<IOptionsMonitor<LeaguesOptions>>().CurrentValue.Items;
    return new SettingsStore(settingsFilePath, startupLeagues, sp.GetRequiredService<IOptionsMonitor<YahooOptions>>());
});
builder.Services.AddSingleton<SettingsResponseBuilder>();

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

if (settingsFileCreated)
{
    app.Logger.LogWarning(
        "No settings file found at {SettingsFilePath}; created one with no leagues or watched teams configured. " +
        "Open the control page (/control) to add a league and watched teams.",
        settingsFilePath);
}

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

app.MapGet("/api/settings", async (SettingsResponseBuilder responseBuilder) => Results.Ok(await responseBuilder.BuildAsync()));

app.MapPut("/api/settings", (SettingsBodyDto body, SettingsStore store) =>
{
    var doc = new SettingsDocument(
        body.Leagues.Select(l => l.ToLeagueOptions()).ToList(),
        body.WatchedTeams,
        body.Polling,
        body.Sounds,
        body.Overlay);

    var (ok, errors, restartRequired) = store.Write(doc);
    if (!ok)
    {
        return Results.BadRequest(new SettingsErrorResponse(errors));
    }

    return Results.Ok(new SettingsPutResponse(true, restartRequired));
});

app.MapPut("/api/settings/overlay", (TouchdownAlert.Core.Configuration.OverlayOptions overlay, SettingsStore store) =>
{
    var (ok, errors, saved) = store.WriteOverlay(overlay);
    if (!ok)
    {
        return Results.BadRequest(new SettingsErrorResponse(errors));
    }

    return Results.Ok(new OverlayPutResponse(true, saved));
});

app.MapPost("/api/restart", (IHostApplicationLifetime lifetime, ILogger<Program> logger) =>
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(200);

        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && !exePath.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var extraArgs = Environment.GetCommandLineArgs().Skip(1).Select(a => $"\"{a}\"");
                var argString = string.Join(' ', extraArgs);
                var relaunch = new ProcessStartInfo("cmd.exe", $"/c \"timeout /t 2 >nul & start \"\" \"{exePath}\" {argString}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                };
                Process.Start(relaunch);
            }
            else
            {
                logger.LogInformation("Restart requested while running under `dotnet run`; the process will stop but won't relaunch itself. Start it again manually.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to relaunch the app after a restart request.");
        }

        lifetime.StopApplication();
    });

    return Results.Accepted();
});

// Push the settings document to every connected client whenever the live-reloadable parts of
// config/settings.json change (overlay position/look, watched teams, polling interval, sound volume/duration).
// Debounced 300ms so a burst of edits (e.g. dragging a slider) doesn't spam a message per keystroke.
var overlayMonitor = app.Services.GetRequiredService<IOptionsMonitor<OverlayOptions>>();
var alertMonitor = app.Services.GetRequiredService<IOptionsMonitor<AlertOptions>>();
var pollingMonitor = app.Services.GetRequiredService<IOptionsMonitor<PollingOptions>>();
var soundMonitor = app.Services.GetRequiredService<IOptionsMonitor<SoundOptions>>();

Timer? settingsPushTimer = null;
settingsPushTimer = new Timer(_ => _ = PushSettingsAsync(), null, Timeout.Infinite, Timeout.Infinite);

void ScheduleSettingsPush() => settingsPushTimer?.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

overlayMonitor.OnChange(_ => ScheduleSettingsPush());
alertMonitor.OnChange(_ => ScheduleSettingsPush());
pollingMonitor.OnChange(_ => ScheduleSettingsPush());
soundMonitor.OnChange(_ => ScheduleSettingsPush());

async Task PushSettingsAsync()
{
    try
    {
        var responseBuilder = app.Services.GetRequiredService<SettingsResponseBuilder>();
        var hub = app.Services.GetRequiredService<IHubContext<DashboardHub>>();
        var response = await responseBuilder.BuildAsync();
        await hub.Clients.All.SendAsync("settings", response);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to push a settings update to connected clients.");
    }
}

app.Run();

/// <summary>
/// Creates <paramref name="settingsFilePath"/> from config/settings.example.json (found via
/// <see cref="RepoPaths"/>, independent of any Settings:FilePath override) with Leagues and
/// Alerts:WatchedTeams cleared, when no settings file exists yet. Returns true if it created one.
/// </summary>
static bool EnsureSettingsFileExists(string settingsFilePath)
{
    if (File.Exists(settingsFilePath))
    {
        return false;
    }

    var exampleFilePath = RepoPaths.Resolve("config/settings.example.json");
    JsonNode root;
    if (File.Exists(exampleFilePath))
    {
        root = JsonNode.Parse(File.ReadAllText(exampleFilePath)) ?? new JsonObject();
    }
    else
    {
        root = new JsonObject();
    }

    root["Leagues"] = new JsonArray();
    root["Alerts"] = new JsonObject { ["WatchedTeams"] = new JsonArray() };

    var dir = Path.GetDirectoryName(settingsFilePath);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }

    File.WriteAllText(settingsFilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return true;
}

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;

public sealed record YahooCodeRequest(string Code);
