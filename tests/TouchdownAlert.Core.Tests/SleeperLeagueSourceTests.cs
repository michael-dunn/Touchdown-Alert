using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Sleeper;
using static TouchdownAlert.Core.Tests.SleeperTestSupport;

namespace TouchdownAlert.Core.Tests;

public class SleeperLeagueSourceTests
{
    private static SleeperLeagueSource CreateSource(
        RoutingHandler handler,
        LeagueOptions? options = null,
        InMemoryPlayerDirectory? directory = null,
        string baseAddress = "https://api.sleeper.app/")
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
        options ??= new LeagueOptions { Key = "sleeper", Provider = LeagueProvider.Sleeper, LeagueId = LeagueId };
        directory ??= new InMemoryPlayerDirectory(LoadPlayers());
        return new SleeperLeagueSource(httpClient, options, directory, TimeProvider.System, NullLogger<SleeperLeagueSource>.Instance);
    }

    [Fact]
    public async Task GetSnapshotAsync_FixtureRoutes_MapsSnapshot()
    {
        var handler = new RoutingHandler(RouteFixtures);
        var source = CreateSource(handler);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("sleeper", snapshot.League.Key);
        Assert.Equal(LeagueProvider.Sleeper, snapshot.League.Provider);
        Assert.Equal(LeagueId, snapshot.League.LeagueId);
        Assert.Equal("Blood, Sweat and Beers", snapshot.LeagueName);
        Assert.Equal(2026, snapshot.SeasonId);
        Assert.Equal(1, snapshot.ScoringPeriodId);
        Assert.Equal(12, snapshot.Teams.Count);
        Assert.Equal(6, snapshot.Matchups.Count);
        Assert.Equal("Collusion Course", snapshot.FindTeam(1)!.Name);
        Assert.Equal(10, snapshot.FindTeam(1)!.Starters.Count());
        Assert.Equal(2, snapshot.FindTeam(8)!.Roster.Single(p => p.PlayerId == 4984).Touchdowns.Rushing);
    }

    [Fact]
    public async Task GetSnapshotAsync_RequestsExpectedRoutes()
    {
        var handler = new RoutingHandler(RouteFixtures);
        var source = CreateSource(handler);

        await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Contains("/v1/state/nfl", handler.RequestPaths);
        Assert.Contains($"/v1/league/{LeagueId}", handler.RequestPaths);
        Assert.Contains($"/v1/league/{LeagueId}/users", handler.RequestPaths);
        Assert.Contains($"/v1/league/{LeagueId}/rosters", handler.RequestPaths);
        Assert.Contains($"/v1/league/{LeagueId}/matchups/1", handler.RequestPaths);
        Assert.Contains("/v1/stats/nfl/regular/2026/1", handler.RequestPaths);
        Assert.Equal(6, handler.RequestPaths.Count);
        // The player dictionary is the directory's job, never this source's.
        Assert.DoesNotContain(handler.RequestPaths, p => p.Contains("/v1/players/nfl"));
    }

    [Fact]
    public async Task GetSnapshotAsync_SecondPoll_ReusesLeagueResponse_RefetchesTheRest()
    {
        var handler = new RoutingHandler(RouteFixtures);
        var directory = new InMemoryPlayerDirectory(LoadPlayers());
        var source = CreateSource(handler, directory: directory);

        await source.GetSnapshotAsync(CancellationToken.None);
        await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, handler.CountRequests($"/v1/league/{LeagueId}"));
        Assert.Equal(2, handler.CountRequests("/v1/state/nfl"));
        Assert.Equal(2, handler.CountRequests("/rosters"));
        Assert.Equal(2, handler.CountRequests("/users"));
        Assert.Equal(2, handler.CountRequests("/matchups/1"));
        Assert.Equal(2, handler.CountRequests("/v1/stats/nfl/regular/2026/1"));
        Assert.Equal(2, directory.Calls);
    }

    [Fact]
    public async Task GetSnapshotAsync_ForcedWeekAndSeason_SkipsStateAndUsesThem()
    {
        var handler = new RoutingHandler(RouteFixtures);
        var options = new LeagueOptions { Key = "sleeper", Provider = LeagueProvider.Sleeper, LeagueId = LeagueId, SeasonId = 2025, ScoringPeriodId = 3 };
        var source = CreateSource(handler, options);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.DoesNotContain("/v1/state/nfl", handler.RequestPaths);
        Assert.Contains($"/v1/league/{LeagueId}/matchups/3", handler.RequestPaths);
        Assert.Contains("/v1/stats/nfl/regular/2025/3", handler.RequestPaths);
        Assert.Equal(3, snapshot.ScoringPeriodId);
    }

    [Fact]
    public async Task GetSnapshotAsync_ForcedWeekOnly_StillReadsSeasonFromState()
    {
        var handler = new RoutingHandler(RouteFixtures);
        var options = new LeagueOptions { Key = "sleeper", Provider = LeagueProvider.Sleeper, LeagueId = LeagueId, ScoringPeriodId = 2 };

        await CreateSource(handler, options).GetSnapshotAsync(CancellationToken.None);

        Assert.Contains("/v1/state/nfl", handler.RequestPaths);
        Assert.Contains("/v1/stats/nfl/regular/2026/2", handler.RequestPaths);
    }

    [Fact]
    public async Task GetSnapshotAsync_BaseAddressWithPathPrefix_IsPreserved()
    {
        var handler = new RoutingHandler(RouteFixtures);
        var source = CreateSource(handler, baseAddress: "http://localhost:5199/sleeper/");

        await source.GetSnapshotAsync(CancellationToken.None);

        Assert.All(handler.RequestPaths, p => Assert.StartsWith("/sleeper/v1/", p));
    }

    [Fact]
    public async Task GetSnapshotAsync_ServerError_ThrowsSleeperApiExceptionWithUrl()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/rosters", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }
                : RouteFixtures(request));
        var source = CreateSource(handler);

        var ex = await Assert.ThrowsAsync<SleeperApiException>(() => source.GetSnapshotAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains($"/v1/league/{LeagueId}/rosters", ex.Message);
    }

    [Fact]
    public async Task GetSnapshotAsync_MalformedJson_ThrowsSleeperApiException()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal)
                ? JsonOk("{ not json")
                : RouteFixtures(request));

        var ex = await Assert.ThrowsAsync<SleeperApiException>(() => CreateSource(handler).GetSnapshotAsync(CancellationToken.None));

        Assert.Null(ex.StatusCode);
        Assert.Contains("/users", ex.Message);
    }

    [Fact]
    public async Task GetSnapshotAsync_NullLeagueBody_ThrowsSleeperApiException()
    {
        // Sleeper answers "null" (200) for an unknown league id.
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith($"/v1/league/{LeagueId}", StringComparison.Ordinal)
                ? JsonOk("null")
                : RouteFixtures(request));

        await Assert.ThrowsAsync<SleeperApiException>(() => CreateSource(handler).GetSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetSnapshotAsync_TransportFailure_ThrowsSleeperApiException()
    {
        var handler = new RoutingHandler(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<SleeperApiException>(() => CreateSource(handler).GetSnapshotAsync(CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task GetSnapshotAsync_EmptyStatsBeforeKickoff_YieldsZeroTouchdowns()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/v1/stats/", StringComparison.Ordinal)
                ? JsonOk("{}")
                : RouteFixtures(request));

        var snapshot = await CreateSource(handler).GetSnapshotAsync(CancellationToken.None);

        Assert.All(snapshot.Teams.SelectMany(t => t.Roster), p => Assert.Equal(0, p.Touchdowns.Total));
        Assert.Equal(12, snapshot.Teams.Count);
    }
}
