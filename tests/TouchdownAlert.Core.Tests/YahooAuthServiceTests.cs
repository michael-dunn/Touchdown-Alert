using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Yahoo;

namespace TouchdownAlert.Core.Tests;

public class YahooAuthServiceTests
{
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        /// <summary>(scheme, request body) captured BEFORE the request is disposed by the caller.</summary>
        public List<(string? AuthScheme, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Headers.Authorization?.Scheme, body));
            return responder(request);
        }
    }

    private sealed class InMemoryTokenStore : IYahooTokenStore
    {
        public YahooTokenData? Token { get; set; }

        public bool HasToken => Token is not null;

        public YahooTokenData? Load() => Token;

        public void Save(YahooTokenData token) => Token = token;

        public void Delete() => Token = null;
    }

    private static IOptionsMonitor<YahooOptions> MonitorOf(YahooOptions options)
    {
        var mock = new TestOptionsMonitor<YahooOptions>(options);
        return mock;
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task ExchangeCodeAsync_SavesToken()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            { "access_token": "at1", "refresh_token": "rt1", "expires_in": 3600, "token_type": "bearer", "xoauth_yahoo_guid": "guid" }
            """));
        var tokenStore = new InMemoryTokenStore();
        var options = new YahooOptions { ClientId = "cid", ClientSecret = "csecret", TokenUrl = "https://example/token" };
        var service = new YahooAuthService(new HttpClient(handler), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        await service.ExchangeCodeAsync("code123", CancellationToken.None);

        Assert.NotNull(tokenStore.Token);
        Assert.Equal("at1", tokenStore.Token!.AccessToken);
        Assert.Equal("rt1", tokenStore.Token.RefreshToken);

        var sentRequest = handler.Requests.Single();
        Assert.Equal("Basic", sentRequest.AuthScheme);
        Assert.Contains("grant_type=authorization_code", sentRequest.Body);
        Assert.Contains("code=code123", sentRequest.Body);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NoToken_ThrowsYahooAuthException()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tokenStore = new InMemoryTokenStore();
        var options = new YahooOptions { ClientId = "cid", ClientSecret = "csecret" };
        var service = new YahooAuthService(new HttpClient(handler), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        await Assert.ThrowsAsync<YahooAuthException>(() => service.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAccessTokenAsync_ValidToken_ReturnsWithoutRefreshing()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not refresh"));
        var tokenStore = new InMemoryTokenStore
        {
            Token = new YahooTokenData("at", "rt", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow),
        };
        var options = new YahooOptions { ClientId = "cid", ClientSecret = "csecret" };
        var service = new YahooAuthService(new HttpClient(handler), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        var token = await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("at", token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NearExpiry_TriggersRefresh()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            { "access_token": "at-refreshed", "refresh_token": "rt-refreshed", "expires_in": 3600 }
            """));
        var tokenStore = new InMemoryTokenStore
        {
            // Within the 2-minute refresh skew.
            Token = new YahooTokenData("at-old", "rt-old", DateTimeOffset.UtcNow.AddSeconds(30), DateTimeOffset.UtcNow),
        };
        var options = new YahooOptions { ClientId = "cid", ClientSecret = "csecret", TokenUrl = "https://example/token" };
        var service = new YahooAuthService(new HttpClient(handler), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        var token = await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("at-refreshed", token);
        Assert.Equal("at-refreshed", tokenStore.Token!.AccessToken);
        var sentRequest = handler.Requests.Single();
        Assert.Contains("grant_type=refresh_token", sentRequest.Body);
        Assert.Contains("refresh_token=rt-old", sentRequest.Body);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_NoToken_ThrowsYahooAuthException()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tokenStore = new InMemoryTokenStore();
        var options = new YahooOptions { ClientId = "cid", ClientSecret = "csecret" };
        var service = new YahooAuthService(new HttpClient(handler), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        await Assert.ThrowsAsync<YahooAuthException>(() => service.RefreshAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExchangeCodeAsync_NonSuccessResponse_ThrowsYahooAuthException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad code"),
        });
        var tokenStore = new InMemoryTokenStore();
        var options = new YahooOptions { ClientId = "cid", ClientSecret = "csecret", TokenUrl = "https://example/token" };
        var service = new YahooAuthService(new HttpClient(handler), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        await Assert.ThrowsAsync<YahooAuthException>(() => service.ExchangeCodeAsync("bad", CancellationToken.None));
        Assert.Null(tokenStore.Token);
    }

    [Fact]
    public void GetAuthorizationUrl_IncludesClientIdAndRedirectUri()
    {
        var options = new YahooOptions { ClientId = "my-client-id", RedirectUri = "oob" };
        var service = new YahooAuthService(new HttpClient(), MonitorOf(options), new InMemoryTokenStore(), TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        var url = service.GetAuthorizationUrl();

        Assert.Contains("client_id=my-client-id", url.ToString());
        Assert.Contains("redirect_uri=oob", url.ToString());
        Assert.Contains("response_type=code", url.ToString());
    }

    [Fact]
    public async Task GetStatusAsync_ReportsConfiguredAndLoggedInFlags()
    {
        var tokenStore = new InMemoryTokenStore();
        var options = new YahooOptions { ClientId = "", ClientSecret = "" };
        var service = new YahooAuthService(new HttpClient(), MonitorOf(options), tokenStore, TimeProvider.System, NullLogger<YahooAuthService>.Instance);

        var status = await service.GetStatusAsync();
        Assert.False(status.IsConfigured);
        Assert.False(status.IsLoggedIn);

        options.ClientId = "cid";
        options.ClientSecret = "secret";
        tokenStore.Token = new YahooTokenData("a", "r", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        var status2 = await service.GetStatusAsync();
        Assert.True(status2.IsConfigured);
        Assert.True(status2.IsLoggedIn);
    }
}
