using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>Identity of one Sleeper player (or team defense, where <see cref="PlayerId"/> is the NFL abbreviation).</summary>
public sealed record SleeperPlayerInfo(string PlayerId, string FullName, string Position, string? Team);

/// <summary>
/// Resolves Sleeper player ids to names/positions. Matchup and stats responses carry ids only, so every
/// snapshot needs this lookup; the full dictionary is ~15 MB and Sleeper asks for at most one download a day.
/// </summary>
public interface ISleeperPlayerDirectory
{
    Task<IReadOnlyDictionary<string, SleeperPlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Disk-cached <see cref="ISleeperPlayerDirectory"/>. Keeps a trimmed (id, name, position, team) copy at
/// <see cref="SleeperOptions.PlayersCacheFilePath"/>, refreshed once it is older than
/// <see cref="SleeperOptions.PlayersCacheMaxAgeHours"/>. A failed refresh falls back to the stale cache (a
/// day-old name table is far better than no touchdown alerts) and is not retried for a while so a Sleeper outage
/// doesn't turn every 30 s poll into a 15 MB download attempt. Shared by all Sleeper leagues.
/// </summary>
public sealed class SleeperPlayerDirectory : ISleeperPlayerDirectory
{
    public const string HttpClientName = "sleeper-players";
    public const string PlayersPath = "v1/players/nfl";

    /// <summary>Positions kept from the full dictionary. Anything else with a fantasy position (IDP) is also kept.</summary>
    private static readonly HashSet<string> FantasyPositions = new(StringComparer.Ordinal) { "QB", "RB", "WR", "TE", "K", "DEF" };

    private static readonly TimeSpan FailedDownloadRetryDelay = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<SleeperOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SleeperPlayerDirectory> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyDictionary<string, SleeperPlayerInfo>? _players;
    private DateTimeOffset _fetchedAt;
    private DateTimeOffset? _nextDownloadAttempt;

    public SleeperPlayerDirectory(
        HttpClient httpClient,
        IOptionsMonitor<SleeperOptions> options,
        TimeProvider? timeProvider,
        ILogger<SleeperPlayerDirectory> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>The absolute cache file path in use (configured path resolved against the repo root).</summary>
    public string CacheFilePath => ResolvePath(_options.CurrentValue.PlayersCacheFilePath);

    public async Task<IReadOnlyDictionary<string, SleeperPlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_players is not null && !IsStale(_fetchedAt, now))
        {
            return _players;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_players is not null && !IsStale(_fetchedAt, now))
            {
                return _players;
            }

            // Another process (or a previous run) may have refreshed the file since we last read it.
            if (_players is null)
            {
                var cached = TryLoadCache();
                if (cached is not null)
                {
                    (_players, _fetchedAt) = cached.Value;
                    if (!IsStale(_fetchedAt, now))
                    {
                        return _players;
                    }
                }
            }

            if (_players is not null && _nextDownloadAttempt is { } retryAt && now < retryAt)
            {
                return _players;
            }

            try
            {
                var fresh = await DownloadAsync(cancellationToken).ConfigureAwait(false);
                WriteCache(fresh, now);
                _players = fresh;
                _fetchedAt = now;
                _nextDownloadAttempt = null;
                _logger.LogInformation("Sleeper player directory refreshed: {Count} players cached at {Path}", fresh.Count, CacheFilePath);
                return _players;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_players is not null)
                {
                    _nextDownloadAttempt = now + FailedDownloadRetryDelay;
                    _logger.LogWarning(ex,
                        "Sleeper player download failed; using cached directory from {FetchedAt} and retrying after {RetryAt}",
                        _fetchedAt, _nextDownloadAttempt);
                    return _players;
                }

                throw new SleeperApiException(
                    $"Failed to download the Sleeper player directory from {PlayersPath} and no cache exists at {CacheFilePath}.",
                    innerException: ex);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsStale(DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        var maxAge = TimeSpan.FromHours(Math.Max(0, _options.CurrentValue.PlayersCacheMaxAgeHours));
        return now - fetchedAt >= maxAge;
    }

    private async Task<IReadOnlyDictionary<string, SleeperPlayerInfo>> DownloadAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(PlayersPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SleeperApiException(
                $"Sleeper API returned {(int)response.StatusCode} ({response.StatusCode}) for {PlayersPath}.", response.StatusCode);
        }

        Dictionary<string, SleeperPlayer>? raw;
        try
        {
            raw = await response.Content.ReadFromJsonAsync<Dictionary<string, SleeperPlayer>>(SleeperJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new SleeperApiException($"Malformed Sleeper API response from {PlayersPath}.", innerException: ex);
        }

        if (raw is null)
        {
            throw new SleeperApiException($"Sleeper API response from {PlayersPath} deserialized to null.");
        }

        return Trim(raw);
    }

    /// <summary>Keeps only entries that can appear on a fantasy roster; drops the ~11k retired/inactive/other rows.</summary>
    public static IReadOnlyDictionary<string, SleeperPlayerInfo> Trim(IReadOnlyDictionary<string, SleeperPlayer> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var result = new Dictionary<string, SleeperPlayerInfo>(StringComparer.Ordinal);
        foreach (var (id, player) in raw)
        {
            if (string.IsNullOrWhiteSpace(id) || player is null)
            {
                continue;
            }

            var position = player.Position;
            var keep = (position is not null && FantasyPositions.Contains(position)) || player.FantasyPositions is { Count: > 0 };
            if (!keep)
            {
                continue;
            }

            result[id] = new SleeperPlayerInfo(
                PlayerId: id,
                FullName: player.ResolveFullName() ?? $"Player {id}",
                Position: position ?? player.FantasyPositions?.FirstOrDefault() ?? "?",
                Team: player.Team);
        }

        return result;
    }

    private (IReadOnlyDictionary<string, SleeperPlayerInfo> Players, DateTimeOffset FetchedAt)? TryLoadCache()
    {
        var path = CacheFilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<CacheFile>(stream, CacheJsonOptions);
            if (file?.Players is null)
            {
                return null;
            }

            var players = new Dictionary<string, SleeperPlayerInfo>(file.Players.Count, StringComparer.Ordinal);
            foreach (var (id, entry) in file.Players)
            {
                players[id] = new SleeperPlayerInfo(id, entry.FullName ?? $"Player {id}", entry.Position ?? "?", entry.Team);
            }

            return (players, file.FetchedAtUtc);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Ignoring unreadable Sleeper player cache at {Path}", path);
            return null;
        }
    }

    /// <summary>Temp file + move so a crash mid-write never leaves a truncated cache for the next run to choke on.</summary>
    private void WriteCache(IReadOnlyDictionary<string, SleeperPlayerInfo> players, DateTimeOffset fetchedAt)
    {
        var path = CacheFilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new CacheFile
        {
            FetchedAtUtc = fetchedAt,
            Players = players.ToDictionary(
                p => p.Key,
                p => new CacheEntry { FullName = p.Value.FullName, Position = p.Value.Position, Team = p.Value.Team },
                StringComparer.Ordinal),
        };

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, file, CacheJsonOptions);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The in-memory copy is still good; only persistence failed.
            _logger.LogWarning(ex, "Could not write Sleeper player cache to {Path}", path);
            try { File.Delete(tempPath); } catch (IOException) { }
        }
    }

    internal static string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "config/sleeper-players.json";
        }

        return RepoPaths.Resolve(configuredPath);
    }

    /// <summary>On-disk cache shape: <c>{ "fetchedAtUtc": "...", "players": { "&lt;id&gt;": { full_name, position, team } } }</c>.</summary>
    private sealed class CacheFile
    {
        [JsonPropertyName("fetchedAtUtc")] public DateTimeOffset FetchedAtUtc { get; set; }
        [JsonPropertyName("players")] public Dictionary<string, CacheEntry>? Players { get; set; }
    }

    private sealed class CacheEntry
    {
        [JsonPropertyName("full_name")] public string? FullName { get; set; }
        [JsonPropertyName("position")] public string? Position { get; set; }
        [JsonPropertyName("team")] public string? Team { get; set; }
    }
}
