// End-to-end test: hosts the real App in-process against the real Simulator in-process, with two leagues -
// one ESPN, one Yahoo - both backed by the SAME simulated league, and verifies each league's watched team
// gets exactly its own alert.
extern alias AppAssembly;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TouchdownAlert.Core.Abstractions;
using AppProgram = AppAssembly::Program;

namespace TouchdownAlert.IntegrationTests;

public sealed class YahooEndToEndTests : IAsyncLifetime
{
    private SimulatorHostFixture _simulator = null!;
    private WebApplicationFactory<AppProgram> _appFactory = null!;
    private HttpClient _appClient = null!;
    private RecordingSoundPlayer _soundPlayer = null!;
    private string _soundsDir = null!;
    private string _tokenFilePath = null!;

    public async Task InitializeAsync()
    {
        _simulator = new SimulatorHostFixture();
        await _simulator.InitializeAsync();

        _soundPlayer = new RecordingSoundPlayer();

        _soundsDir = Path.Combine(Path.GetTempPath(), "TouchdownAlert.YahooE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_soundsDir);
        foreach (var name in new[] { "a.mp3", "b.mp3" })
        {
            File.WriteAllBytes(Path.Combine(_soundsDir, name), Array.Empty<byte>());
        }

        _tokenFilePath = Path.Combine(Path.GetTempPath(), "TouchdownAlert.YahooE2E", Guid.NewGuid().ToString("N"), "yahoo-token.json");

        Environment.SetEnvironmentVariable("TOUCHDOWNALERT_SILENT", "1");

        var simulatorBase = _simulator.Server.BaseAddress.ToString();

        _appFactory = new WebApplicationFactory<AppProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Leagues:0:Key", "main");
                builder.UseSetting("Leagues:0:Provider", "Espn");
                builder.UseSetting("Leagues:0:LeagueId", "998946988");
                builder.UseSetting("Leagues:0:BaseUrl", simulatorBase);

                builder.UseSetting("Leagues:1:Key", "yahoo");
                builder.UseSetting("Leagues:1:Provider", "Yahoo");
                builder.UseSetting("Leagues:1:LeagueId", "nfl.l.998946988");
                builder.UseSetting("Leagues:1:BaseUrl", simulatorBase.TrimEnd('/') + "/fantasy/v2/");

                builder.UseSetting("Yahoo:ClientId", "sim");
                builder.UseSetting("Yahoo:ClientSecret", "sim");
                builder.UseSetting("Yahoo:TokenUrl", simulatorBase.TrimEnd('/') + "/oauth2/get_token");
                builder.UseSetting("Yahoo:TokenFilePath", _tokenFilePath);

                builder.UseSetting("Polling:IntervalSeconds", "1");
                builder.UseSetting("Sounds:Enabled", "false");
                builder.UseSetting("Sounds:Directory", _soundsDir);

                builder.UseSetting("Alerts:WatchedTeams:0:TeamId", "1");
                builder.UseSetting("Alerts:WatchedTeams:0:League", "main");
                builder.UseSetting("Alerts:WatchedTeams:0:SoundFile", "a.mp3");
                builder.UseSetting("Alerts:WatchedTeams:1:TeamId", "3");
                builder.UseSetting("Alerts:WatchedTeams:1:League", "yahoo");
                builder.UseSetting("Alerts:WatchedTeams:1:SoundFile", "b.mp3");
                // Blank out the App's own appsettings.json entries (indices 2-3, both "main") so they can't
                // leak in and double-alert - team 3 is a starter on "main" too, and we only want it watched
                // in "yahoo" for this test.
                builder.UseSetting("Alerts:WatchedTeams:2:TeamId", "-1");
                builder.UseSetting("Alerts:WatchedTeams:2:League", "main");
                builder.UseSetting("Alerts:WatchedTeams:2:SoundFile", "");
                builder.UseSetting("Alerts:WatchedTeams:3:TeamId", "-1");
                builder.UseSetting("Alerts:WatchedTeams:3:League", "main");
                builder.UseSetting("Alerts:WatchedTeams:3:SoundFile", "");

                builder.ConfigureServices(services =>
                {
                    // Route every HttpClient (App's ESPN client, Yahoo league client, and Yahoo auth client)
                    // at the in-memory simulator TestServer instead of a real socket.
                    var handler = _simulator.Server.CreateHandler();
                    services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

                    services.RemoveAll<ISoundPlayer>();
                    services.AddSingleton<ISoundPlayer>(_soundPlayer);
                });
            });

        _appClient = _appFactory.CreateClient();

        await _simulator.Client.PostAsync("/sim/reset?week=1", content: null);

        // Log in to Yahoo before waiting for the poller to seed - otherwise every Yahoo poll throws
        // YahooAuthException until this happens (that's by design; see YahooAuthService).
        var codeResponse = await _appClient.PostAsJsonAsync("/api/yahoo/code", new { code = "sim" });
        codeResponse.EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow.AddSeconds(20);
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
        throw new TimeoutException("App never seeded both leagues' detectors. Last state: " + last);
    }

    public async Task DisposeAsync()
    {
        _appClient.Dispose();
        await _appFactory.DisposeAsync();
        await _simulator.DisposeAsync();
        try { Directory.Delete(_soundsDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(Path.GetDirectoryName(_tokenFilePath)!, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Two_sources_espn_and_yahoo_from_one_simulator()
    {
        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 3918298, type = "Passing", count = 1 })).EnsureSuccessStatusCode();
        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = 4362628, type = "Receiving", count = 1 })).EnsureSuccessStatusCode();

        (await _appClient.PostAsync("/api/poll", content: null)).EnsureSuccessStatusCode();

        List<JsonElement> recentAlerts = new();
        var deadline = DateTime.UtcNow.AddSeconds(15);
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

        Assert.Contains(recentAlerts, a =>
            a.GetProperty("leagueKey").GetString() == "main" &&
            a.GetProperty("teamId").GetInt32() == 1 &&
            a.GetProperty("touchdownType").GetString() == "Passing");

        Assert.Contains(recentAlerts, a =>
            a.GetProperty("leagueKey").GetString() == "yahoo" &&
            a.GetProperty("teamId").GetInt32() == 3 &&
            a.GetProperty("touchdownType").GetString() == "Receiving");

        Assert.DoesNotContain(recentAlerts, a =>
            a.GetProperty("leagueKey").GetString() == "main" && a.GetProperty("teamId").GetInt32() == 3);
    }
}
