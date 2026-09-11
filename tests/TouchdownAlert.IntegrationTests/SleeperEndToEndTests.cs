// End-to-end test: hosts the real App in-process against the real Simulator in-process, with two leagues -
// one ESPN, one Sleeper - both backed by the SAME simulated league, and verifies each league's watched team
// gets exactly its own alert, that D/ST touchdowns survive the abbreviation-keyed Sleeper stats row, and that
// the Sleeper player directory + snapshot mapper produce a fully named dashboard.
extern alias AppAssembly;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TouchdownAlert.Core.Abstractions;
using AppProgram = AppAssembly::Program;

namespace TouchdownAlert.IntegrationTests;

public sealed class SleeperEndToEndTests : IAsyncLifetime
{
    // Watched teams: team 1 in "main" (Josh Allen at QB), team 3 in "sleeper" (Ja'Marr Chase at WR).
    private const int MainTeamId = 1;
    private const int SleeperTeamId = 3;
    private const long JoshAllen = 3918298;
    private const long JaMarrChase = 4362628;

    private SimulatorHostFixture _simulator = null!;
    private WebApplicationFactory<AppProgram> _appFactory = null!;
    private HttpClient _appClient = null!;
    private RecordingSoundPlayer _soundPlayer = null!;
    private string _tempRoot = null!;
    private string _soundsDir = null!;
    private string _playersCachePath = null!;
    private string _settingsFilePath = null!;

    public async Task InitializeAsync()
    {
        _simulator = new SimulatorHostFixture();
        await _simulator.InitializeAsync();

        _soundPlayer = new RecordingSoundPlayer();

        _tempRoot = Path.Combine(Path.GetTempPath(), "TouchdownAlert.SleeperE2E", Guid.NewGuid().ToString("N"));
        _soundsDir = Path.Combine(_tempRoot, "sounds");
        Directory.CreateDirectory(_soundsDir);
        foreach (var name in new[] { "a.mp3", "b.mp3" })
        {
            File.WriteAllBytes(Path.Combine(_soundsDir, name), Array.Empty<byte>());
        }

        // The Sleeper player directory persists its trimmed cache to disk; keep that (and the settings file the
        // App creates on startup) out of the repo's config/ folder.
        _playersCachePath = Path.Combine(_tempRoot, "sleeper-players.json");
        _settingsFilePath = Path.Combine(_tempRoot, "settings.json");

        Environment.SetEnvironmentVariable("TOUCHDOWNALERT_SILENT", "1");

        var simulatorBase = _simulator.Server.BaseAddress.ToString();

        _appFactory = new WebApplicationFactory<AppProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Settings:FilePath", _settingsFilePath);

                builder.UseSetting("Leagues:0:Key", "main");
                builder.UseSetting("Leagues:0:Provider", "Espn");
                builder.UseSetting("Leagues:0:LeagueId", "998946988");
                builder.UseSetting("Leagues:0:BaseUrl", simulatorBase);

                builder.UseSetting("Leagues:1:Key", "sleeper");
                builder.UseSetting("Leagues:1:Provider", "Sleeper");
                builder.UseSetting("Leagues:1:LeagueId", "998946988");
                builder.UseSetting("Leagues:1:BaseUrl", simulatorBase);

                // The shared player-directory client uses Sleeper:ApiBaseUrl (not the league's BaseUrl).
                builder.UseSetting("Sleeper:ApiBaseUrl", simulatorBase);
                builder.UseSetting("Sleeper:PlayersCacheFilePath", _playersCachePath);

                builder.UseSetting("Polling:IntervalSeconds", "1");
                builder.UseSetting("Sounds:Enabled", "false");
                builder.UseSetting("Alerts:BannerSeconds", "0"); // present every alert immediately - no banner pacing in tests
                builder.UseSetting("Sounds:Directory", _soundsDir);

                builder.UseSetting("Alerts:WatchedTeams:0:TeamId", MainTeamId.ToString());
                builder.UseSetting("Alerts:WatchedTeams:0:League", "main");
                builder.UseSetting("Alerts:WatchedTeams:0:SoundFile", "a.mp3");
                builder.UseSetting("Alerts:WatchedTeams:1:TeamId", SleeperTeamId.ToString());
                builder.UseSetting("Alerts:WatchedTeams:1:League", "sleeper");
                builder.UseSetting("Alerts:WatchedTeams:1:SoundFile", "b.mp3");

                builder.ConfigureServices(services =>
                {
                    // Route every HttpClient (App's ESPN client, the Sleeper league client and the Sleeper
                    // player-directory client) at the in-memory simulator TestServer instead of a real socket.
                    var handler = _simulator.Server.CreateHandler();
                    services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

                    services.RemoveAll<ISoundPlayer>();
                    services.AddSingleton<ISoundPlayer>(_soundPlayer);
                });
            });

        _appClient = _appFactory.CreateClient();

        await _simulator.Client.PostAsync("/sim/reset?week=1", content: null);

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
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Two_sources_espn_and_sleeper_from_one_simulator()
    {
        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = JoshAllen, type = "Passing", count = 1 })).EnsureSuccessStatusCode();
        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = JaMarrChase, type = "Receiving", count = 1 })).EnsureSuccessStatusCode();

        (await _appClient.PostAsync("/api/poll", content: null)).EnsureSuccessStatusCode();

        var recentAlerts = await WaitForAlertsAsync(minimumCount: 2);

        Assert.Equal(2, recentAlerts.Count);

        var mainAlert = Assert.Single(recentAlerts, a => a.GetProperty("leagueKey").GetString() == "main");
        Assert.Equal(MainTeamId, mainAlert.GetProperty("teamId").GetInt32());
        Assert.Equal("Passing", mainAlert.GetProperty("touchdownType").GetString());
        Assert.Equal("a.mp3", mainAlert.GetProperty("soundFile").GetString());

        var sleeperAlert = Assert.Single(recentAlerts, a => a.GetProperty("leagueKey").GetString() == "sleeper");
        Assert.Equal(SleeperTeamId, sleeperAlert.GetProperty("teamId").GetInt32());
        Assert.Equal("Receiving", sleeperAlert.GetProperty("touchdownType").GetString());
        Assert.Equal("Ja'Marr Chase", sleeperAlert.GetProperty("playerName").GetString());
        Assert.Equal("b.mp3", sleeperAlert.GetProperty("soundFile").GetString());

        // Team 3 is a starter for Chase on "main" too, but is only watched in "sleeper" - no cross-league leak.
        Assert.DoesNotContain(recentAlerts, a =>
            a.GetProperty("leagueKey").GetString() == "main" && a.GetProperty("teamId").GetInt32() == SleeperTeamId);

        var sounds = _soundPlayer.Enqueued.Select(Path.GetFileName).ToList();
        Assert.Equal(1, sounds.Count(n => n == "a.mp3"));
        Assert.Equal(1, sounds.Count(n => n == "b.mp3"));
    }

    [Fact]
    public async Task Dst_interception_return_alerts_as_defensive_in_the_sleeper_league()
    {
        // The simulator keys the Sleeper stats row for a D/ST by NFL abbreviation with def_td (and a "td" decoy on
        // every DEF row), so this proves the id translation + stat map end to end, not just the ESPN path.
        var dstId = await FindStarterIdAsync(SleeperTeamId, position: "D/ST", slot: "D/ST");

        (await _simulator.Client.PostAsJsonAsync("/sim/touchdown", new { playerId = dstId, type = "InterceptionReturn", count = 1 })).EnsureSuccessStatusCode();

        (await _appClient.PostAsync("/api/poll", content: null)).EnsureSuccessStatusCode();

        var recentAlerts = await WaitForAlertsAsync(minimumCount: 1);

        var alert = Assert.Single(recentAlerts);
        Assert.Equal("sleeper", alert.GetProperty("leagueKey").GetString());
        Assert.Equal(SleeperTeamId, alert.GetProperty("teamId").GetInt32());
        Assert.Equal("Defensive", alert.GetProperty("touchdownType").GetString());
        Assert.Equal(1, alert.GetProperty("count").GetInt32());
        Assert.Equal("b.mp3", alert.GetProperty("soundFile").GetString());
    }

    [Fact]
    public async Task Sleeper_league_dashboard_has_named_teams_and_named_starters()
    {
        var state = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");

        var league = Assert.Single(state.GetProperty("leagues").EnumerateArray(), l => l.GetProperty("key").GetString() == "sleeper");
        Assert.Equal("Sleeper", league.GetProperty("provider").GetString());
        Assert.Equal("Trelipe Takedown", league.GetProperty("name").GetString());
        Assert.Equal(1, league.GetProperty("week").GetInt32());
        Assert.Equal(JsonValueKind.Null, league.GetProperty("lastError").ValueKind);

        // All 10 rosters surface as named teams (the control page's team picker reads these).
        var settings = await _appClient.GetFromJsonAsync<JsonElement>("/api/settings");
        var teams = settings.GetProperty("meta").GetProperty("leagueTeams").GetProperty("sleeper").EnumerateArray().ToList();
        Assert.Equal(10, teams.Count);
        Assert.Equal(Enumerable.Range(1, 10), teams.Select(t => t.GetProperty("teamId").GetInt32()).OrderBy(x => x));
        Assert.All(teams, t => Assert.False(string.IsNullOrWhiteSpace(t.GetProperty("name").GetString())));

        // The watched Sleeper team has a full, named starting lineup: roster_positions minus the four BN slots.
        var watched = Assert.Single(state.GetProperty("watchedTeams").EnumerateArray(), t => t.GetProperty("leagueKey").GetString() == "sleeper");
        Assert.Equal(SleeperTeamId, watched.GetProperty("teamId").GetInt32());
        Assert.True(watched.GetProperty("hasRoster").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(watched.GetProperty("espnTeamName").GetString()));

        var starters = watched.GetProperty("starters").EnumerateArray().ToList();
        Assert.Equal(9, starters.Count);
        Assert.Equal(new[] { "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "D/ST", "K" }, starters.Select(s => s.GetProperty("slot").GetString()));
        Assert.All(starters, s =>
        {
            var name = s.GetProperty("name").GetString();
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.DoesNotMatch("^Player -?\\d+$", name);
        });
        Assert.Contains(starters, s => s.GetProperty("name").GetString() == "Ja'Marr Chase" && s.GetProperty("position").GetString() == "WR");
        // Sleeper names a team defense "City Nickname" (first_name/last_name of the DEF row) and the mapper renders its
        // position the ESPN way; the name must have come from the directory, not the abbreviation fallback.
        var dst = Assert.Single(starters, s => s.GetProperty("slot").GetString() == "D/ST");
        Assert.Equal("D/ST", dst.GetProperty("position").GetString());
        Assert.Contains(' ', dst.GetProperty("name").GetString()!);
        Assert.Equal(4, watched.GetProperty("bench").GetArrayLength());

        // The player-directory cache landed in the temp path, not in the repo's config/ folder.
        Assert.True(File.Exists(_playersCachePath), $"Expected the Sleeper players cache at {_playersCachePath}");
    }

    private async Task<List<JsonElement>> WaitForAlertsAsync(int minimumCount)
    {
        List<JsonElement> recentAlerts = new();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var state = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");
            recentAlerts = state.GetProperty("recentAlerts").EnumerateArray().ToList();
            if (recentAlerts.Count >= minimumCount)
            {
                break;
            }

            await Task.Delay(250);
        }

        // One more poll cycle's worth of settling so a late duplicate would be caught by the exact-count asserts.
        await Task.Delay(1250);
        var final = await _appClient.GetFromJsonAsync<JsonElement>("/api/state");
        return final.GetProperty("recentAlerts").EnumerateArray().ToList();
    }

    private async Task<long> FindStarterIdAsync(int teamId, string position, string slot)
    {
        var players = await _simulator.Client.GetFromJsonAsync<JsonElement>("/sim/players");
        foreach (var p in players.EnumerateArray())
        {
            if (p.GetProperty("position").GetString() != position)
            {
                continue;
            }

            foreach (var r in p.GetProperty("rosters").EnumerateArray())
            {
                if (r.GetProperty("teamId").GetInt32() == teamId && r.GetProperty("slot").GetString() == slot)
                {
                    return p.GetProperty("id").GetInt64();
                }
            }
        }

        throw new InvalidOperationException($"Could not find team {teamId}'s starting {position}.");
    }
}
