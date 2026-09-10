using System.Net;
using System.Text;
using System.Text.Json;
using TouchdownAlert.Core.Sleeper;

namespace TouchdownAlert.Core.Tests;

/// <summary>Shared helpers for the Sleeper tests: fixture loading, a path-routing fake HTTP handler, a settable clock.</summary>
internal static class SleeperTestSupport
{
    public const string LeagueId = "1401782105192570880";

    public static T LoadFixture<T>(string fileName) where T : class =>
        JsonSerializer.Deserialize<T>(TestFixtures.ReadText("sleeper/" + fileName), SleeperJson.Options)
        ?? throw new InvalidOperationException($"Fixture {fileName} deserialized to null.");

    public static IReadOnlyDictionary<string, SleeperPlayerInfo> LoadPlayers() =>
        SleeperPlayerDirectory.Trim(LoadFixture<Dictionary<string, SleeperPlayer>>("players-nfl-trimmed.json"));

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> LoadStats(string fileName) =>
        SleeperStatsParser.Parse(TestFixtures.ReadText("sleeper/" + fileName));

    public static HttpResponseMessage JsonOk(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage FixtureOk(string fileName) => JsonOk(TestFixtures.ReadText("sleeper/" + fileName));

    /// <summary>
    /// Serves the captured fixtures for the real league's routes. The stats route ignores season/week (like the
    /// simulator does) and serves the 2025 week 1 sample so rostered players have stat lines.
    /// </summary>
    public static HttpResponseMessage RouteFixtures(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/v1/state/nfl", StringComparison.Ordinal))
        {
            return FixtureOk("state-nfl.json");
        }

        if (path.EndsWith($"/v1/league/{LeagueId}", StringComparison.Ordinal))
        {
            return FixtureOk("league.json");
        }

        if (path.EndsWith($"/v1/league/{LeagueId}/users", StringComparison.Ordinal))
        {
            return FixtureOk("users.json");
        }

        if (path.EndsWith($"/v1/league/{LeagueId}/rosters", StringComparison.Ordinal))
        {
            return FixtureOk("rosters.json");
        }

        if (path.Contains($"/v1/league/{LeagueId}/matchups/", StringComparison.Ordinal))
        {
            return FixtureOk("matchups-week1.json");
        }

        if (path.Contains("/v1/stats/nfl/regular/", StringComparison.Ordinal))
        {
            return FixtureOk("stats-2025-week1-sample.json");
        }

        if (path.EndsWith("/v1/players/nfl", StringComparison.Ordinal))
        {
            return FixtureOk("players-nfl-trimmed.json");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no route for " + path) };
    }

    public sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestPaths)
            {
                RequestPaths.Add(request.RequestUri!.PathAndQuery);
            }

            return Task.FromResult(responder(request));
        }

        public int CountRequests(string pathSuffix)
        {
            lock (RequestPaths)
            {
                return RequestPaths.Count(p => p.EndsWith(pathSuffix, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>Minimal settable clock; Microsoft.Extensions.TimeProvider.Testing isn't referenced by this project.</summary>
    public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    public sealed class InMemoryPlayerDirectory(IReadOnlyDictionary<string, SleeperPlayerInfo> players) : ISleeperPlayerDirectory
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, SleeperPlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(players);
        }
    }

    public static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TouchdownAlert.Tests", "sleeper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
