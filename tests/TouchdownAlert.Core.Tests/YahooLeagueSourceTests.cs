using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Yahoo;

namespace TouchdownAlert.Core.Tests;

public class YahooLeagueSourceTests
{
    private const string LeagueKey = "nfl.l.1";
    private const string Ns = "http://fantasysports.yahooapis.com/fantasy/v2/base.rng";

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.PathAndQuery);
            return await responder(request);
        }
    }

    private sealed class StaticAuthService(string accessToken = "token") : IYahooAuthService
    {
        public int RefreshCount { get; private set; }

        public Uri GetAuthorizationUrl() => new("https://example/authorize");

        public Task ExchangeCodeAsync(string code, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(accessToken);

        public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(accessToken + "-refreshed");
        }

        public Task<YahooAuthStatus> GetStatusAsync() => Task.FromResult(new YahooAuthStatus(true, true, null, null));
    }

    private static IOptionsMonitor<AlertOptions> AlertsMonitor(params WatchedTeamOptions[] watched)
    {
        var options = new AlertOptions { WatchedTeams = watched.ToList() };
        return new TestMonitor<AlertOptions>(options);
    }

    private sealed class TestMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static string LeagueXml(int week = 2) => $"""
        <fantasy_content xmlns="{Ns}">
          <league>
            <league_key>{LeagueKey}</league_key>
            <league_id>1</league_id>
            <name>Test Yahoo League</name>
            <season>2026</season>
            <current_week>{week}</current_week>
            <start_week>1</start_week>
            <end_week>17</end_week>
            <num_teams>2</num_teams>
            <is_finished>0</is_finished>
          </league>
        </fantasy_content>
        """;

    private static string SettingsXml() => $"""
        <fantasy_content xmlns="{Ns}">
          <league>
            <settings>
              <stat_categories>
                <stats>
                  <stat><stat_id>5</stat_id><name>Passing Touchdowns</name><position_type>O</position_type><enabled>1</enabled></stat>
                  <stat><stat_id>10</stat_id><name>Rushing Touchdowns</name><position_type>O</position_type><enabled>1</enabled></stat>
                  <stat><stat_id>13</stat_id><name>Reception Touchdowns</name><position_type>O</position_type><enabled>1</enabled></stat>
                </stats>
              </stat_categories>
            </settings>
          </league>
        </fantasy_content>
        """;

    private static string ScoreboardXml() => $"""
        <fantasy_content xmlns="{Ns}">
          <league>
            <scoreboard>
              <matchups>
                <matchup>
                  <teams>
                    <team>
                      <team_key>{LeagueKey}.t.1</team_key>
                      <team_id>1</team_id>
                      <name>Team One</name>
                      <team_points><total>10</total></team_points>
                    </team>
                    <team>
                      <team_key>{LeagueKey}.t.2</team_key>
                      <team_id>2</team_id>
                      <name>Team Two</name>
                      <team_points><total>20</total></team_points>
                    </team>
                  </teams>
                </matchup>
              </matchups>
            </scoreboard>
          </league>
        </fantasy_content>
        """;

    private static string RosterXml(int teamId) => $"""
        <fantasy_content xmlns="{Ns}">
          <team>
            <team_key>{LeagueKey}.t.{teamId}</team_key>
            <roster>
              <players>
                <player>
                  <player_key>{LeagueKey}.p.100</player_key>
                  <player_id>100</player_id>
                  <name><full>Star Player {teamId}</full></name>
                  <display_position>QB</display_position>
                  <editorial_team_abbr>ZZZ</editorial_team_abbr>
                  <selected_position><position>QB</position></selected_position>
                  <player_points><total>12.5</total></player_points>
                  <player_stats>
                    <stats>
                      <stat><stat_id>5</stat_id><value>2</value></stat>
                    </stats>
                  </player_stats>
                </player>
              </players>
            </roster>
          </team>
        </fantasy_content>
        """;

    private static HttpResponseMessage XmlOk(string xml) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
    };

    private YahooLeagueSource CreateSource(StubHandler handler, IYahooAuthService auth, params WatchedTeamOptions[] watched)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fantasysports.yahooapis.com/fantasy/v2/") };
        var options = new LeagueOptions { Key = "yahoo", Provider = LeagueProvider.Yahoo, LeagueId = "1" };
        return new YahooLeagueSource(httpClient, options, auth, AlertsMonitor(watched), TimeProvider.System, NullLogger<YahooLeagueSource>.Instance);
    }

    private static Task<HttpResponseMessage> DefaultRouter(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        if (path.Contains("/settings"))
        {
            return Task.FromResult(XmlOk(SettingsXml()));
        }

        if (path.Contains("/scoreboard"))
        {
            return Task.FromResult(XmlOk(ScoreboardXml()));
        }

        if (path.Contains("/roster"))
        {
            var teamId = path.Contains(".t.1") ? 1 : 2;
            return Task.FromResult(XmlOk(RosterXml(teamId)));
        }

        return Task.FromResult(XmlOk(LeagueXml()));
    }

    [Fact]
    public async Task GetSnapshotAsync_MapsLeagueTeamsAndMatchups()
    {
        var handler = new StubHandler(DefaultRouter);
        var watched = new WatchedTeamOptions { TeamId = 1, League = "yahoo", SoundFile = "a.mp3" };
        var source = CreateSource(handler, new StaticAuthService(), watched);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("Test Yahoo League", snapshot.LeagueName);
        Assert.Equal(2026, snapshot.SeasonId);
        Assert.Equal(2, snapshot.ScoringPeriodId);
        Assert.Equal(2, snapshot.Teams.Count);
        Assert.Single(snapshot.Matchups);
        Assert.Equal(LeagueProvider.Yahoo, snapshot.League.Provider);
        Assert.Equal("yahoo", snapshot.League.Key);
    }

    [Fact]
    public async Task GetSnapshotAsync_OnlyFetchesRosterForWatchedTeams()
    {
        var handler = new StubHandler(DefaultRouter);
        var watched = new WatchedTeamOptions { TeamId = 1, League = "yahoo", SoundFile = "a.mp3" };
        var source = CreateSource(handler, new StaticAuthService(), watched);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        var team1 = snapshot.FindTeam(1)!;
        var team2 = snapshot.FindTeam(2)!;

        Assert.NotEmpty(team1.Roster);
        Assert.Empty(team2.Roster);
        Assert.DoesNotContain(handler.RequestPaths, p => p.Contains(".t.2/roster"));
    }

    [Fact]
    public async Task GetSnapshotAsync_MapsTouchdownCountsByStatName()
    {
        var handler = new StubHandler(DefaultRouter);
        var watched = new WatchedTeamOptions { TeamId = 1, League = "yahoo", SoundFile = "a.mp3" };
        var source = CreateSource(handler, new StaticAuthService(), watched);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        var player = snapshot.FindTeam(1)!.Roster.Single();
        Assert.Equal(2, player.Touchdowns.Passing);
        Assert.True(player.IsStarter);
    }

    [Fact]
    public async Task GetSnapshotAsync_401Once_RefreshesAndRetries()
    {
        var attempt = 0;
        var handler = new StubHandler(async request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains(LeagueKey) && !path.Contains("settings") && !path.Contains("scoreboard") && !path.Contains("roster"))
            {
                attempt++;
                if (attempt == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
            }

            return await DefaultRouter(request);
        });

        var auth = new StaticAuthService();
        var source = CreateSource(handler, auth, new WatchedTeamOptions { TeamId = 1, League = "yahoo", SoundFile = "a.mp3" });

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, auth.RefreshCount);
        Assert.Equal("Test Yahoo League", snapshot.LeagueName);
    }

    [Fact]
    public async Task GetSnapshotAsync_ServerError_ThrowsYahooApiException()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        }));
        var source = CreateSource(handler, new StaticAuthService());

        await Assert.ThrowsAsync<YahooApiException>(() => source.GetSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetSnapshotAsync_NoLogin_ThrowsYahooAuthException()
    {
        var handler = new StubHandler(DefaultRouter);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fantasysports.yahooapis.com/fantasy/v2/") };
        var options = new LeagueOptions { Key = "yahoo", Provider = LeagueProvider.Yahoo, LeagueId = "1" };
        var tokenStore = new YahooTokenStore(Microsoft.Extensions.Options.Options.Create(new YahooOptions
        {
            TokenFilePath = Path.Combine(Path.GetTempPath(), "TouchdownAlert.Tests", Guid.NewGuid().ToString("N"), "token.json"),
        }));
        var authService = new YahooAuthService(new HttpClient(), AlertsMonitorForYahoo(), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);
        var source = new YahooLeagueSource(httpClient, options, authService, AlertsMonitor(), TimeProvider.System, NullLogger<YahooLeagueSource>.Instance);

        await Assert.ThrowsAsync<YahooAuthException>(() => source.GetSnapshotAsync(CancellationToken.None));
    }

    private static IOptionsMonitor<YahooOptions> AlertsMonitorForYahoo() => new TestMonitor<YahooOptions>(new YahooOptions { ClientId = "cid", ClientSecret = "secret" });
}
