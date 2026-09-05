using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.Core.Yahoo;

/// <summary>Current Yahoo login state, for the /setup/yahoo page and /api/yahoo/status.</summary>
public sealed record YahooAuthStatus(bool IsConfigured, bool IsLoggedIn, DateTimeOffset? ExpiresAtUtc, string? Error);

/// <summary>Handles the Yahoo OAuth2 "out of band" flow: authorize URL, code exchange, and token refresh.</summary>
public interface IYahooAuthService
{
    /// <summary>The URL to open in a browser so the user can log in and get a short code back.</summary>
    Uri GetAuthorizationUrl();

    /// <summary>Exchanges the short code the user pasted back in for an access/refresh token pair and saves it.</summary>
    Task ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a currently-valid access token, refreshing it first if it's within two minutes of expiring.
    /// Throws <see cref="YahooAuthException"/> if there is no saved token (never logged in, or logged out).
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>Forces a token refresh regardless of expiry, for a caller that just got a 401. Throws
    /// <see cref="YahooAuthException"/> if there is no saved token to refresh.</summary>
    Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken);

    Task<YahooAuthStatus> GetStatusAsync();
}

/// <summary>
/// <see cref="IYahooAuthService"/> implementation backed by <see cref="IYahooTokenStore"/> and a real HTTP
/// call to Yahoo's token endpoint. A semaphore serializes refreshes so a poll racing another poll's refresh
/// can't both hit Yahoo at once or clobber each other's saved token.
/// </summary>
public sealed class YahooAuthService : IYahooAuthService
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<YahooOptions> _options;
    private readonly IYahooTokenStore _tokenStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YahooAuthService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public YahooAuthService(
        HttpClient httpClient,
        IOptionsMonitor<YahooOptions> options,
        IYahooTokenStore tokenStore,
        TimeProvider? timeProvider,
        ILogger<YahooAuthService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options;
        _tokenStore = tokenStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public Uri GetAuthorizationUrl()
    {
        var options = _options.CurrentValue;
        var baseUrl = options.AuthorizeUrl.TrimEnd('/');
        var url = $"{baseUrl}?client_id={Uri.EscapeDataString(options.ClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(options.RedirectUri)}" +
                  "&response_type=code&language=en-us";
        return new Uri(url);
    }

    public async Task ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code must not be blank.", nameof(code));
        }

        var options = _options.CurrentValue;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["redirect_uri"] = options.RedirectUri,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
        };

        var token = await PostTokenRequestAsync(options, form, cancellationToken).ConfigureAwait(false);
        _tokenStore.Save(token);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = _tokenStore.Load()
            ?? throw new YahooAuthException("Yahoo: not logged in - open /setup/yahoo to log in.");

        if (token.ExpiresAtUtc - _timeProvider.GetUtcNow() > RefreshSkew)
        {
            return token.AccessToken;
        }

        return await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock: another caller may have just refreshed it for us.
            var current = _tokenStore.Load()
                ?? throw new YahooAuthException("Yahoo: not logged in - open /setup/yahoo to log in.");

            if (current.ExpiresAtUtc - _timeProvider.GetUtcNow() > RefreshSkew)
            {
                return current.AccessToken;
            }

            var options = _options.CurrentValue;
            var form = new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["redirect_uri"] = options.RedirectUri,
                ["refresh_token"] = current.RefreshToken,
                ["grant_type"] = "refresh_token",
            };

            var refreshed = await PostTokenRequestAsync(options, form, cancellationToken).ConfigureAwait(false);
            _tokenStore.Save(refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task<YahooAuthStatus> GetStatusAsync()
    {
        var options = _options.CurrentValue;
        var isConfigured = !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret);
        var token = _tokenStore.Load();
        return Task.FromResult(new YahooAuthStatus(isConfigured, token is not null, token?.ExpiresAtUtc, Error: null));
    }

    private async Task<YahooTokenData> PostTokenRequestAsync(YahooOptions options, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}")));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to reach Yahoo token endpoint at {Uri}", options.TokenUrl);
            throw new YahooAuthException($"Failed to reach Yahoo token endpoint: {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo token endpoint returned {StatusCode}: {Body}", response.StatusCode, Truncate(body));
                throw new YahooAuthException(
                    $"Yahoo token endpoint returned {(int)response.StatusCode} ({response.StatusCode}): {Truncate(body)}");
            }

            YahooTokenResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<YahooTokenResponse>(body);
            }
            catch (JsonException ex)
            {
                throw new YahooAuthException($"Malformed response from Yahoo token endpoint: {ex.Message}", ex);
            }

            if (parsed is null || string.IsNullOrEmpty(parsed.AccessToken) || string.IsNullOrEmpty(parsed.RefreshToken))
            {
                throw new YahooAuthException("Yahoo token endpoint response was missing access_token/refresh_token.");
            }

            var now = _timeProvider.GetUtcNow();
            return new YahooTokenData(
                AccessToken: parsed.AccessToken,
                RefreshToken: parsed.RefreshToken,
                ExpiresAtUtc: now + TimeSpan.FromSeconds(parsed.ExpiresIn > 0 ? parsed.ExpiresIn : 3600),
                ObtainedAtUtc: now);
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";

    private sealed class YahooTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("xoauth_yahoo_guid")]
        public string? XoauthYahooGuid { get; set; }
    }
}
