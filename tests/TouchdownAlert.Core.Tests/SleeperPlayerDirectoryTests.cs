using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Sleeper;
using static TouchdownAlert.Core.Tests.SleeperTestSupport;

namespace TouchdownAlert.Core.Tests;

public class SleeperPlayerDirectoryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _tempDir = NewTempDirectory();
    private readonly string _cachePath;

    public SleeperPlayerDirectoryTests()
    {
        _cachePath = Path.Combine(_tempDir, "players.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private sealed class Monitor(SleeperOptions value) : IOptionsMonitor<SleeperOptions>
    {
        public SleeperOptions CurrentValue { get; } = value;
        public SleeperOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SleeperOptions, string?> listener) => null;
    }

    private SleeperPlayerDirectory Create(HttpMessageHandler handler, TimeProvider time, int maxAgeHours = 24) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/") },
            new Monitor(new SleeperOptions { PlayersCacheFilePath = _cachePath, PlayersCacheMaxAgeHours = maxAgeHours }),
            time,
            NullLogger<SleeperPlayerDirectory>.Instance);

    private static RoutingHandler ServePlayers() => new(request =>
        request.RequestUri!.AbsolutePath == "/v1/players/nfl"
            ? FixtureOk("players-nfl-trimmed.json")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

    [Fact]
    public async Task FirstCall_Downloads_WritesCache_AndTrims()
    {
        var handler = ServePlayers();
        var directory = Create(handler, new FakeTimeProvider(Start));

        var players = await directory.GetPlayersAsync(CancellationToken.None);

        Assert.Equal(1, handler.CountRequests("/v1/players/nfl"));
        Assert.Equal(181, players.Count);
        Assert.Equal("Matthew Stafford", players["421"].FullName);
        Assert.Equal("QB", players["421"].Position);
        Assert.Equal("LAR", players["421"].Team);
        Assert.Equal("Detroit Lions", players["DET"].FullName);
        Assert.Equal("DEF", players["DET"].Position);

        Assert.True(File.Exists(_cachePath));
        using var doc = JsonDocument.Parse(File.ReadAllText(_cachePath));
        Assert.Equal(Start, doc.RootElement.GetProperty("fetchedAtUtc").GetDateTimeOffset());
        Assert.Equal("Detroit Lions", doc.RootElement.GetProperty("players").GetProperty("DET").GetProperty("full_name").GetString());
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public async Task SecondCallWithinTtl_DoesNotHitHttp()
    {
        var handler = ServePlayers();
        var time = new FakeTimeProvider(Start);
        var directory = Create(handler, time);

        var first = await directory.GetPlayersAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(23));
        var second = await directory.GetPlayersAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, handler.CountRequests("/v1/players/nfl"));
    }

    [Fact]
    public async Task NewInstanceWithFreshCacheFile_LoadsFromDisk_WithoutHttp()
    {
        var time = new FakeTimeProvider(Start);
        await Create(ServePlayers(), time).GetPlayersAsync(CancellationToken.None);

        var handler = ServePlayers();
        time.Advance(TimeSpan.FromHours(1));
        var players = await Create(handler, time).GetPlayersAsync(CancellationToken.None);

        Assert.Equal(0, handler.CountRequests("/v1/players/nfl"));
        Assert.Equal(181, players.Count);
        Assert.Equal("Detroit Lions", players["DET"].FullName);
    }

    [Fact]
    public async Task PastTtl_RedownloadsAndRewritesCache()
    {
        var handler = ServePlayers();
        var time = new FakeTimeProvider(Start);
        var directory = Create(handler, time);

        await directory.GetPlayersAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(25));
        await directory.GetPlayersAsync(CancellationToken.None);

        Assert.Equal(2, handler.CountRequests("/v1/players/nfl"));
        using var doc = JsonDocument.Parse(File.ReadAllText(_cachePath));
        Assert.Equal(time.Now, doc.RootElement.GetProperty("fetchedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task FailedDownloadWithStaleCache_ReturnsStaleCache_AndDoesNotHammer()
    {
        var time = new FakeTimeProvider(Start);
        await Create(ServePlayers(), time).GetPlayersAsync(CancellationToken.None);

        var failing = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        time.Advance(TimeSpan.FromDays(2));
        var directory = Create(failing, time);

        var players = await directory.GetPlayersAsync(CancellationToken.None);
        var again = await directory.GetPlayersAsync(CancellationToken.None);

        Assert.Equal(181, players.Count);
        Assert.Same(players, again);
        Assert.Equal(1, failing.CountRequests("/v1/players/nfl"));

        // After the retry delay a new attempt is made.
        time.Advance(TimeSpan.FromMinutes(11));
        await directory.GetPlayersAsync(CancellationToken.None);
        Assert.Equal(2, failing.CountRequests("/v1/players/nfl"));
    }

    [Fact]
    public async Task FailedDownloadWithoutCache_ThrowsSleeperApiException()
    {
        var failing = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var directory = Create(failing, new FakeTimeProvider(Start));

        var ex = await Assert.ThrowsAsync<SleeperApiException>(() => directory.GetPlayersAsync(CancellationToken.None));

        Assert.Contains("players/nfl", ex.Message);
        Assert.False(File.Exists(_cachePath));
    }

    [Fact]
    public async Task ConcurrentFirstCalls_DownloadOnce()
    {
        var handler = ServePlayers();
        var directory = Create(handler, new FakeTimeProvider(Start));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => directory.GetPlayersAsync(CancellationToken.None)));

        Assert.Equal(1, handler.CountRequests("/v1/players/nfl"));
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public void Trim_KeepsFantasyPositions_TolleratesNullTeam_DropsOthers()
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, SleeperPlayer>>("""
            {
              "1": { "player_id": "1", "first_name": "Free", "last_name": "Agent", "full_name": "Free Agent", "position": "WR", "fantasy_positions": ["WR"], "team": null },
              "2": { "player_id": "2", "first_name": "Some", "last_name": "Lineman", "position": "OL", "fantasy_positions": null, "team": "KC" },
              "3": { "player_id": "3", "first_name": "Idp", "last_name": "Backer", "position": "LB", "fantasy_positions": ["LB"], "team": "KC" },
              "4": { "player_id": "4", "first_name": "No", "last_name": "Fullname", "position": "RB", "team": "KC" },
              "NE": { "player_id": "NE", "first_name": "New England", "last_name": "Patriots", "position": "DEF", "fantasy_positions": ["DEF"], "team": "NE" }
            }
            """, SleeperJson.Options)!;

        var trimmed = SleeperPlayerDirectory.Trim(raw);

        Assert.Equal(4, trimmed.Count);
        Assert.Null(trimmed["1"].Team);
        Assert.False(trimmed.ContainsKey("2"));
        Assert.Equal("LB", trimmed["3"].Position);
        Assert.Equal("No Fullname", trimmed["4"].FullName);
        Assert.Equal("New England Patriots", trimmed["NE"].FullName);
    }

    [Fact]
    public async Task CorruptCacheFile_IsIgnored_AndRedownloaded()
    {
        File.WriteAllText(_cachePath, "{ not json");
        var handler = ServePlayers();

        var players = await Create(handler, new FakeTimeProvider(Start)).GetPlayersAsync(CancellationToken.None);

        Assert.Equal(181, players.Count);
        Assert.Equal(1, handler.CountRequests("/v1/players/nfl"));
    }
}
