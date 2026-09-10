using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Fetches league state from Sleeper's public read API and maps it to a <see cref="LeagueSnapshot"/>. Sleeper
/// needs no auth, and every response is small except the player dictionary (owned by
/// <see cref="ISleeperPlayerDirectory"/>), so each poll fetches everything it needs concurrently:
/// <c>/v1/state/nfl</c> (unless the week is forced), <c>/v1/league/{id}</c> (cached after the first success -
/// name/roster_positions don't change mid-season), <c>/users</c> + <c>/rosters</c> (every poll: lineups change),
/// <c>/matchups/{week}</c> (points) and <c>/v1/stats/nfl/regular/{season}/{week}</c> (the touchdown stat lines).
/// </summary>
public sealed class SleeperLeagueSource : ILeagueSource
{
    public const string DefaultBaseUrl = "https://api.sleeper.app";

    private readonly HttpClient _httpClient;
    private readonly LeagueOptions _options;
    private readonly ISleeperPlayerDirectory _players;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SleeperLeagueSource> _logger;

    private SleeperLeague? _cachedLeague;

    public SleeperLeagueSource(
        HttpClient httpClient,
        LeagueOptions options,
        ISleeperPlayerDirectory players,
        TimeProvider? timeProvider,
        ILogger<SleeperLeagueSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options;
        _players = players;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;

        League = new LeagueRef(options.Key, options.Provider, options.LeagueId);
    }

    public LeagueRef League { get; }

    public async Task<LeagueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var fetchedAt = _timeProvider.GetUtcNow();
        var leagueId = _options.LeagueId;

        // The directory may block on its once-a-day download; kick it off before the small requests.
        var playersTask = _players.GetPlayersAsync(cancellationToken);

        var (week, season) = await ResolveWeekAndSeasonAsync(cancellationToken).ConfigureAwait(false);

        var leagueTask = GetLeagueAsync(leagueId, cancellationToken);
        var usersTask = GetJsonAsync<List<SleeperUser>>($"v1/league/{leagueId}/users", cancellationToken);
        var rostersTask = GetJsonAsync<List<SleeperRoster>>($"v1/league/{leagueId}/rosters", cancellationToken);
        var matchupsTask = GetJsonAsync<List<SleeperMatchup>>($"v1/league/{leagueId}/matchups/{week}", cancellationToken);
        var statsTask = GetStatsAsync(season, week, cancellationToken);

        await Task.WhenAll(leagueTask, usersTask, rostersTask, matchupsTask, statsTask, playersTask).ConfigureAwait(false);

        return SleeperSnapshotMapper.Map(
            league: leagueTask.Result,
            users: usersTask.Result,
            rosters: rostersTask.Result,
            matchups: matchupsTask.Result,
            stats: statsTask.Result,
            players: playersTask.Result,
            week: week,
            leagueRef: League,
            fetchedAt: fetchedAt);
    }

    /// <summary>Week/season come from config when forced, else from <c>/v1/state/nfl</c> (skipped entirely when both are forced).</summary>
    private async Task<(int Week, string Season)> ResolveWeekAndSeasonAsync(CancellationToken cancellationToken)
    {
        if (_options.ScoringPeriodId is { } forcedWeek && _options.SeasonId is { } forcedSeason)
        {
            return (Math.Max(1, forcedWeek), forcedSeason.ToString());
        }

        var state = await GetJsonAsync<SleeperNflState>("v1/state/nfl", cancellationToken).ConfigureAwait(false);
        var week = _options.ScoringPeriodId ?? state.Week;
        var season = _options.SeasonId?.ToString()
            ?? (!string.IsNullOrWhiteSpace(state.Season) ? state.Season : _timeProvider.GetUtcNow().Year.ToString());
        return (Math.Max(1, week), season);
    }

    private async Task<SleeperLeague> GetLeagueAsync(string leagueId, CancellationToken cancellationToken)
    {
        if (_cachedLeague is { } cached)
        {
            return cached;
        }

        var league = await GetJsonAsync<SleeperLeague>($"v1/league/{leagueId}", cancellationToken).ConfigureAwait(false);
        _cachedLeague = league;
        return league;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>> GetStatsAsync(
        string season, int week, CancellationToken cancellationToken)
    {
        var path = $"v1/stats/nfl/regular/{season}/{week}";
        using var document = await GetJsonAsync<JsonDocument>(path, cancellationToken).ConfigureAwait(false);
        return SleeperStatsParser.Parse(document.RootElement);
    }

    private async Task<T> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken) where T : class
    {
        var uri = _httpClient.BaseAddress is { } baseAddress ? new Uri(baseAddress, relativePath) : new Uri(relativePath, UriKind.Relative);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to reach Sleeper API at {Uri}", uri);
            throw new SleeperApiException($"Failed to reach Sleeper API at {uri}.", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sleeper API returned {StatusCode} for {Uri}", response.StatusCode, uri);
                throw new SleeperApiException(
                    $"Sleeper API returned {(int)response.StatusCode} ({response.StatusCode}) for {uri}.",
                    response.StatusCode);
            }

            T? parsed;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                parsed = typeof(T) == typeof(JsonDocument)
                    ? (T)(object)await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false)
                    : await JsonSerializer.DeserializeAsync<T>(stream, SleeperJson.Options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _logger.LogError(ex, "Malformed Sleeper API response from {Uri}", uri);
                throw new SleeperApiException($"Malformed Sleeper API response from {uri}.", innerException: ex);
            }

            if (parsed is null)
            {
                // Sleeper answers "null" for unknown league ids and for matchups before the schedule exists.
                throw new SleeperApiException($"Sleeper API response from {uri} was null (unknown league id or week?).");
            }

            return parsed;
        }
    }
}
