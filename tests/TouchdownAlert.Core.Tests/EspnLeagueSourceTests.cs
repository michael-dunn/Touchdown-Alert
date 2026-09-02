using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Tests;

public class EspnLeagueSourceTests
{
    [Fact]
    public void BuildRequestUri_WithoutScoringPeriodId_OmitsParameter()
    {
        var options = new LeagueOptions { Key = "main", BaseUrl = "https://lm-api-reads.fantasy.espn.com", LeagueId = "998946988" };

        var uri = EspnLeagueSource.BuildRequestUri(options, 2026);

        Assert.Equal(
            "https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/2026/segments/0/leagues/998946988?view=mBoxscore&view=mMatchupScore&view=mTeam&view=mSettings",
            uri.ToString());
    }

    [Fact]
    public void BuildRequestUri_WithScoringPeriodId_AppendsParameter()
    {
        var options = new LeagueOptions { Key = "main", BaseUrl = "https://lm-api-reads.fantasy.espn.com", LeagueId = "998946988", ScoringPeriodId = 3 };

        var uri = EspnLeagueSource.BuildRequestUri(options, 2026);

        Assert.EndsWith("&scoringPeriodId=3", uri.ToString());
    }

    [Fact]
    public void BuildRequestUri_WithoutBaseUrl_UsesEspnDefault()
    {
        var options = new LeagueOptions { Key = "main", LeagueId = "998946988" };

        var uri = EspnLeagueSource.BuildRequestUri(options, 2026);

        Assert.StartsWith("https://lm-api-reads.fantasy.espn.com/", uri.ToString());
    }

    [Fact]
    public async Task GetSnapshotAsync_SuccessResponse_MapsFixture()
    {
        var json = TestFixtures.ReadText("league-week1-live-sample.json");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://lm-api-reads.fantasy.espn.com") };
        var source = new EspnLeagueSource(
            httpClient,
            new LeagueOptions { Key = "main", LeagueId = "998946988" },
            TimeProvider.System,
            NullLogger<EspnLeagueSource>.Instance);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(10, snapshot.Teams.Count);
        Assert.Equal("main", snapshot.League.Key);
        Assert.Equal("998946988", snapshot.League.LeagueId);
        Assert.Equal(LeagueProvider.Espn, snapshot.League.Provider);
    }

    [Fact]
    public async Task GetSnapshotAsync_ServerError_ThrowsEspnApiException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://lm-api-reads.fantasy.espn.com") };
        var source = new EspnLeagueSource(
            httpClient,
            new LeagueOptions { Key = "main", LeagueId = "998946988" },
            TimeProvider.System,
            NullLogger<EspnLeagueSource>.Instance);

        var ex = await Assert.ThrowsAsync<EspnApiException>(() => source.GetSnapshotAsync(CancellationToken.None));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task GetSnapshotAsync_MalformedJson_ThrowsEspnApiException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not valid json", System.Text.Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://lm-api-reads.fantasy.espn.com") };
        var source = new EspnLeagueSource(
            httpClient,
            new LeagueOptions { Key = "main", LeagueId = "998946988" },
            TimeProvider.System,
            NullLogger<EspnLeagueSource>.Instance);

        await Assert.ThrowsAsync<EspnApiException>(() => source.GetSnapshotAsync(CancellationToken.None));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
