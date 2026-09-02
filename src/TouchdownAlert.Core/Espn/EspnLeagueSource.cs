using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Espn.Wire;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Espn;

/// <summary>Fetches league state from the real ESPN fantasy read API and maps it to a <see cref="LeagueSnapshot"/>.</summary>
public sealed class EspnLeagueSource : ILeagueSource
{
    private static readonly ProductInfoHeaderValue UserAgentProduct = new("Mozilla", "5.0");

    private readonly HttpClient _httpClient;
    private readonly IOptions<EspnOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EspnLeagueSource> _logger;

    public EspnLeagueSource(
        HttpClient httpClient,
        IOptions<EspnOptions> options,
        TimeProvider? timeProvider,
        ILogger<EspnLeagueSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<LeagueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var seasonId = options.SeasonId ?? _timeProvider.GetUtcNow().Year;
        var uri = BuildRequestUri(options, seasonId);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(UserAgentProduct);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(compatible; TouchdownAlert)"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to reach ESPN API at {Uri}", uri);
            throw new EspnApiException($"Failed to reach ESPN API at {uri}.", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ESPN API returned {StatusCode} for {Uri}", response.StatusCode, uri);
                throw new EspnApiException(
                    $"ESPN API returned {(int)response.StatusCode} ({response.StatusCode}) for {uri}.",
                    response.StatusCode);
            }

            EspnLeagueResponse? parsed;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                parsed = await JsonSerializer.DeserializeAsync<EspnLeagueResponse>(
                    stream, EspnLeagueResponse.JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _logger.LogError(ex, "Malformed ESPN API response from {Uri}", uri);
                throw new EspnApiException($"Malformed ESPN API response from {uri}.", innerException: ex);
            }

            if (parsed is null)
            {
                throw new EspnApiException($"ESPN API response from {uri} deserialized to null.");
            }

            return EspnSnapshotMapper.Map(parsed, _timeProvider.GetUtcNow());
        }
    }

    /// <summary>Builds the ESPN league request URI for the given options and season, appending scoringPeriodId only when set.</summary>
    public static Uri BuildRequestUri(EspnOptions options, int seasonId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var path = $"{baseUrl}/apis/v3/games/ffl/seasons/{seasonId}/segments/0/leagues/{options.LeagueId}" +
                   "?view=mBoxscore&view=mMatchupScore&view=mTeam&view=mSettings";

        if (options.ScoringPeriodId is { } scoringPeriodId)
        {
            path += $"&scoringPeriodId={scoringPeriodId}";
        }

        return new Uri(path);
    }
}
