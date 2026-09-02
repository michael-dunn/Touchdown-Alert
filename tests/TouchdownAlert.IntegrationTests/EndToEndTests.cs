// End-to-end test: hosts the real App in-process against the real Simulator in-process, and verifies
// touchdowns scored in the simulator produce the right alerts (or no alert) through the App's public API.
extern alias AppAssembly;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TouchdownAlert.Core.Abstractions;
using AppProgram = AppAssembly::Program;

namespace TouchdownAlert.IntegrationTests;

/// <summary>Records every sound the App tries to play, in enqueue order, instead of actually playing audio.</summary>
public sealed class RecordingSoundPlayer : ISoundPlayer
{
    private readonly List<string> _enqueued = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> Enqueued
    {
        get { lock (_lock) { return _enqueued.ToList(); } }
    }

    public int QueueLength => Enqueued.Count;

    public void Enqueue(string soundFilePath)
    {
        lock (_lock) { _enqueued.Add(soundFilePath); }
    }
}

public sealed class EndToEndTests : IAsyncLifetime
{
    private SimulatorHostFixture _simulator = null!;
    private WebApplicationFactory<AppProgram> _appFactory = null!;
    private HttpClient _appClient = null!;
    private RecordingSoundPlayer _soundPlayer = null!;
    private string _soundsDir = null!;

    public async Task InitializeAsync()
    {
        _simulator = new SimulatorHostFixture();
        await _simulator.InitializeAsync();

        _soundPlayer = new RecordingSoundPlayer();

        // The App only enqueues sounds whose file actually exists, so give it a throwaway sounds folder.
        _soundsDir = Path.Combine(Path.GetTempPath(), "TouchdownAlert.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_soundsDir);
        foreach (var name in new[] { "a.mp3", "b.mp3", "c.mp3" })
        {
            File.WriteAllBytes(Path.Combine(_soundsDir, name), Array.Empty<byte>());
        }

        Environment.SetEnvironmentVariable("TOUCHDOWNALERT_SILENT", "1");

        _appFactory = new WebApplicationFactory<AppProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Espn:BaseUrl", _simulator.Server.BaseAddress.ToString());
                builder.UseSetting("Polling:IntervalSeconds", "1");
                builder.UseSetting("Sounds:Enabled", "false");
                builder.UseSetting("Sounds:Directory", _soundsDir);
                builder.UseSetting("Alerts:WatchedTeams:0:TeamId", "1");
                builder.UseSetting("Alerts:WatchedTeams:0:SoundFile", "a.mp3");
                builder.UseSetting("Alerts:WatchedTeams:1:TeamId", "3");
                builder.UseSetting("Alerts:WatchedTeams:1:SoundFile", "b.mp3");
                builder.UseSetting("Alerts:WatchedTeams:2:TeamId", "5");
                builder.UseSetting("Alerts:WatchedTeams:2:SoundFile", "c.mp3");

                builder.ConfigureServices(services =>
                {
                    // Route every HttpClient (including the App's typed ESPN client) at the in-memory
                    // simulator TestServer instead of a real socket.
                    var handler = _simulator.Server.CreateHandler();
                    services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

                    services.RemoveAll<ISoundPlayer>();
                    services.AddSingleton<ISoundPlayer>(_soundPlayer);
                });
            });

        _appClient = _appFactory.CreateClient();

        await _simulator.Client.PostAsync("/sim/reset?week=1", content: null);

        // Wait for the App to have polled at least once and seeded its detector.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var state = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");
            if (state.TryGetProperty("detectorSeeded", out var seeded) && seeded.GetBoolean())
            {
                return;
            }

            await Task.Delay(250);
        }

        var last = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");
        throw new TimeoutException("App never seeded its detector against the simulator. Last state: " + last);
    }

    public async Task DisposeAsync()
    {
        _appClient.Dispose();
        await _appFactory.DisposeAsync();
        await _simulator.DisposeAsync();
        try { Directory.Delete(_soundsDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Touchdowns_alert_starting_watched_teams_but_not_a_benched_one()
    {
        // Team 1's starting QB throws a passing TD; Ja'Marr Chase (starter on team 3, benched on team 5)
        // catches a receiving TD.
        var team1QbId = await FindTeam1StarterQbIdAsync();

        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = team1QbId, type = "Passing", count = 1 })).EnsureSuccessStatusCode();
        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 4362628, type = "Receiving", count = 1 })).EnsureSuccessStatusCode();

        (await _appClient.PostAsync("/api/poll", content: null)).EnsureSuccessStatusCode();

        // Poll for the alerts to land (the poll may complete asynchronously).
        List<JsonElement> recentAlerts = new();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var state = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");
            recentAlerts = state.GetProperty("recentAlerts").EnumerateArray().ToList();
            if (recentAlerts.Count >= 2)
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.Contains(recentAlerts, a => a.GetProperty("teamId").GetInt32() == 1 && a.GetProperty("touchdownType").GetString() == "Passing");
        Assert.Contains(recentAlerts, a => a.GetProperty("teamId").GetInt32() == 3 && a.GetProperty("touchdownType").GetString() == "Receiving");
        Assert.DoesNotContain(recentAlerts, a => a.GetProperty("teamId").GetInt32() == 5);

        // Sounds enqueue (as resolved absolute paths) in configured watched-team order: team 1 before team 3.
        var names = _soundPlayer.Enqueued.Select(Path.GetFileName).ToList();
        var aIndex = names.IndexOf("a.mp3");
        var bIndex = names.IndexOf("b.mp3");
        Assert.True(aIndex >= 0 && bIndex >= 0 && aIndex < bIndex, $"Unexpected sound order: {string.Join(", ", names)}");
        Assert.DoesNotContain("c.mp3", names);
    }

    [Fact]
    public async Task Test_endpoint_plays_configured_sound_for_watched_team_and_rejects_others()
    {
        var ok = await _appClient.PostAsync("/api/test/3", content: null);
        ok.EnsureSuccessStatusCode();

        var bad = await _appClient.PostAsync("/api/test/9", content: null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);

        var names = _soundPlayer.Enqueued.Select(Path.GetFileName).ToList();
        Assert.Contains("b.mp3", names);

        var state = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");
        var alerts = state.GetProperty("recentAlerts").EnumerateArray().ToList();
        Assert.Contains(alerts, a => a.GetProperty("teamId").GetInt32() == 3 && a.GetProperty("isTest").GetBoolean());
    }

    private async Task<long> FindTeam1StarterQbIdAsync()
    {
        var players = await _simulator.Client.GetFromJsonAsync<JsonElement>("/sim/players");
        foreach (var p in players.EnumerateArray())
        {
            if (p.GetProperty("position").GetString() != "QB")
            {
                continue;
            }

            foreach (var r in p.GetProperty("rosters").EnumerateArray())
            {
                if (r.GetProperty("teamId").GetInt32() == 1 && r.GetProperty("slot").GetString() == "QB")
                {
                    return p.GetProperty("id").GetInt64();
                }
            }
        }

        throw new InvalidOperationException("Could not find team 1's starting QB.");
    }
}
