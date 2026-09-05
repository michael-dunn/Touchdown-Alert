using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Yahoo;

/// <summary>
/// Fetches league state from the real Yahoo Fantasy Sports read API (XML) and maps it to a
/// <see cref="LeagueSnapshot"/>. Only the watched teams in this league get their roster fetched every poll
/// (everyone else gets an empty roster) - the dashboard/alerts only ever care about watched teams, and
/// fetching all N rosters every poll would multiply Yahoo's rate limit by team count for no benefit.
/// </summary>
public sealed class YahooLeagueSource : ILeagueSource
{
    private readonly HttpClient _httpClient;
    private readonly LeagueOptions _options;
    private readonly IYahooAuthService _auth;
    private readonly IOptionsMonitor<AlertOptions> _alertOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YahooLeagueSource> _logger;

    private IReadOnlyList<YahooStatCategory>? _cachedStatCategories;
    private readonly SemaphoreSlim _statCategoriesLock = new(1, 1);

    public YahooLeagueSource(
        HttpClient httpClient,
        LeagueOptions options,
        IYahooAuthService auth,
        IOptionsMonitor<AlertOptions> alertOptions,
        TimeProvider? timeProvider,
        ILogger<YahooLeagueSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(alertOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options;
        _auth = auth;
        _alertOptions = alertOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;

        League = new LeagueRef(options.Key, options.Provider, options.LeagueId);
    }

    public LeagueRef League { get; }

    /// <summary>Config <see cref="LeagueOptions.LeagueId"/> may be a bare numeric id or a full "{game_key}.l.{id}" key.</summary>
    public static string ResolveLeagueKey(string configuredLeagueId)
    {
        ArgumentNullException.ThrowIfNull(configuredLeagueId);
        return configuredLeagueId.Contains(".l.", StringComparison.Ordinal) ? configuredLeagueId : $"nfl.l.{configuredLeagueId}";
    }

    public async Task<LeagueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var leagueKey = ResolveLeagueKey(_options.LeagueId);
        var fetchedAt = _timeProvider.GetUtcNow();

        var leagueDoc = await GetXmlAsync($"league/{leagueKey}", cancellationToken).ConfigureAwait(false);
        var leagueInfo = YahooXmlParser.ParseLeague(leagueDoc);
        var week = _options.ScoringPeriodId ?? leagueInfo.CurrentWeek;

        var statCategories = await GetStatCategoriesAsync(leagueKey, cancellationToken).ConfigureAwait(false);
        var statTypeMap = BuildStatTypeMap(statCategories);

        var scoreboardDoc = await GetXmlAsync($"league/{leagueKey}/scoreboard;week={week}", cancellationToken).ConfigureAwait(false);
        var matchups = YahooXmlParser.ParseScoreboard(scoreboardDoc);

        var watchedTeamIds = _alertOptions.CurrentValue.WatchedTeams
            .Where(w => string.Equals(w.League, _options.Key, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.TeamId)
            .ToHashSet();

        var teams = new List<TeamSnapshot>();
        var matchupSnapshots = new List<MatchupSnapshot>();

        foreach (var matchup in matchups)
        {
            matchupSnapshots.Add(new MatchupSnapshot(
                matchup.Team1.TeamId, matchup.Team1.Points, matchup.Team2.TeamId, matchup.Team2.Points));

            foreach (var team in new[] { matchup.Team1, matchup.Team2 })
            {
                IReadOnlyList<RosteredPlayer> roster = Array.Empty<RosteredPlayer>();
                if (watchedTeamIds.Contains(team.TeamId))
                {
                    var teamKey = $"{leagueKey}.t.{team.TeamId}";
                    roster = await GetRosterAsync(teamKey, week, statTypeMap, cancellationToken).ConfigureAwait(false);
                }

                teams.Add(new TeamSnapshot(team.TeamId, team.Name, Abbreviation: "", team.Points, roster));
            }
        }

        return new LeagueSnapshot(
            League: League,
            LeagueName: leagueInfo.Name,
            SeasonId: leagueInfo.Season,
            ScoringPeriodId: week,
            MatchupPeriodId: week,
            FetchedAt: fetchedAt,
            Teams: teams,
            Matchups: matchupSnapshots);
    }

    private async Task<IReadOnlyList<YahooStatCategory>> GetStatCategoriesAsync(string leagueKey, CancellationToken cancellationToken)
    {
        if (_cachedStatCategories is not null)
        {
            return _cachedStatCategories;
        }

        await _statCategoriesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedStatCategories is not null)
            {
                return _cachedStatCategories;
            }

            var doc = await GetXmlAsync($"league/{leagueKey}/settings", cancellationToken).ConfigureAwait(false);
            _cachedStatCategories = YahooXmlParser.ParseStatCategories(doc);
            return _cachedStatCategories;
        }
        finally
        {
            _statCategoriesLock.Release();
        }
    }

    private static IReadOnlyDictionary<int, TouchdownType> BuildStatTypeMap(IReadOnlyList<YahooStatCategory> categories)
    {
        var map = new Dictionary<int, TouchdownType>(YahooStatIds.ByStatId);
        foreach (var category in categories)
        {
            var type = YahooStatMap.Classify(category.Name, category.PositionType);
            if (type is not null)
            {
                map[category.StatId] = type.Value;
            }
        }

        return map;
    }

    private async Task<IReadOnlyList<RosteredPlayer>> GetRosterAsync(
        string teamKey, int week, IReadOnlyDictionary<int, TouchdownType> statTypeMap, CancellationToken cancellationToken)
    {
        var doc = await GetXmlAsync($"team/{teamKey}/roster;week={week}/players/stats;type=week;week={week}", cancellationToken).ConfigureAwait(false);
        var players = YahooXmlParser.ParseRoster(doc);

        var roster = new List<RosteredPlayer>(players.Count);
        foreach (var player in players)
        {
            var counts = TouchdownCounts.Zero;
            foreach (var (statId, value) in player.Stats)
            {
                if (!statTypeMap.TryGetValue(statId, out var type))
                {
                    continue;
                }

                var rounded = (int)Math.Round(value);
                if (rounded != 0)
                {
                    counts = counts.With(type, counts.Get(type) + rounded);
                }
            }

            var isStarter = YahooXmlParser.IsStarterSlot(player.SelectedPosition);
            roster.Add(new RosteredPlayer(
                PlayerId: player.PlayerId,
                FullName: player.FullName,
                Position: player.DisplayPosition,
                LineupSlotId: isStarter ? 0 : EspnLineupSlots.Bench,
                LineupSlot: string.IsNullOrWhiteSpace(player.SelectedPosition) ? "BN" : player.SelectedPosition,
                ProTeamId: 0,
                Points: player.Points,
                Touchdowns: counts));
        }

        return roster;
    }

    private async Task<XDocument> GetXmlAsync(string relativePath, CancellationToken cancellationToken)
    {
        var response = await SendWithAuthAsync(relativePath, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return XDocument.Parse(body);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException)
            {
                throw new YahooApiException($"Malformed Yahoo API XML response for {relativePath}.", innerException: ex);
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(string relativePath, bool forceRefresh, CancellationToken cancellationToken)
    {
        var accessToken = forceRefresh
            ? await _auth.RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            : await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to reach Yahoo API for {Path}", relativePath);
            throw new YahooApiException($"Failed to reach Yahoo API for {relativePath}.", innerException: ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && !forceRefresh)
        {
            response.Dispose();
            _logger.LogInformation("Yahoo API returned 401 for {Path}; refreshing token and retrying once", relativePath);
            return await SendWithAuthAsync(relativePath, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();
            var snippet = body.Length <= 300 ? body : body[..300] + "...";
            throw new YahooApiException(
                $"Yahoo API returned {(int)response.StatusCode} ({response.StatusCode}) for {relativePath}: {snippet}",
                response.StatusCode);
        }

        return response;
    }
}
