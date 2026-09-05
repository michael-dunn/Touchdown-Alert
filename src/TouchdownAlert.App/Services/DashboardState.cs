using Microsoft.Extensions.Options;
using TouchdownAlert.App.Contracts;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.App.Services;

/// <summary>
/// Thread-safe in-memory snapshot of everything the dashboard needs to render, partitioned per league:
/// last snapshot, poll health, detector-seeded flag, plus a capped alert log shared across all leagues.
/// Single source of truth consumed by the HTTP endpoints, the SignalR hub, and PollingService/AlertDispatcher.
/// </summary>
public sealed class DashboardState
{
    private const int MaxAlertLogEntries = 200;

    private readonly object _lock = new();
    private readonly IAlertRouter _alertRouter;
    private readonly IOptionsMonitor<LeaguesOptions> _leaguesOptions;
    private readonly ISoundFileResolver _soundResolver;
    private readonly IOptionsMonitor<PollingOptions> _pollingOptions;

    private readonly Dictionary<string, LeagueRuntimeState> _leagues = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _nextPollAt;
    private long _pollCount;
    private readonly List<AlertLogEntryViewModel> _alertLog = new();

    /// <summary>Default team colors by position in the watched list, used when a team has no configured Color.</summary>
    public static readonly IReadOnlyList<string> DefaultPalette = new[]
    {
        "#22c55e", // green
        "#3b82f6", // blue
        "#f59e0b", // amber
        "#ec4899", // pink
        "#a855f7", // purple
        "#14b8a6", // teal
    };

    public static string ResolveColor(WatchedTeamOptions team, int index) =>
        !string.IsNullOrWhiteSpace(team.Color) ? team.Color.Trim() : DefaultPalette[index % DefaultPalette.Count];

    public DashboardState(
        IAlertRouter alertRouter,
        IOptionsMonitor<LeaguesOptions> leaguesOptions,
        ISoundFileResolver soundResolver,
        IOptionsMonitor<PollingOptions> pollingOptions)
    {
        _alertRouter = alertRouter;
        _leaguesOptions = leaguesOptions;
        _soundResolver = soundResolver;
        _pollingOptions = pollingOptions;
    }

    /// <summary>Last successfully fetched snapshot for the given league, if any. Used by the test-alert endpoint.</summary>
    public LeagueSnapshot? GetSnapshot(string leagueKey)
    {
        lock (_lock)
        {
            return _leagues.TryGetValue(leagueKey, out var state) ? state.Snapshot : null;
        }
    }

    /// <summary>
    /// The last snapshot's teams (id + name) for every league that has one, keyed by league key. Used by the
    /// settings API's <c>meta.leagueTeams</c> so the control page can offer team pickers by name. A league that
    /// hasn't been polled yet (or has no snapshot) is simply omitted.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(int TeamId, string Name)>> GetAllLeagueTeams()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, IReadOnlyList<(int TeamId, string Name)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, state) in _leagues)
            {
                if (state.Snapshot is not null)
                {
                    result[key] = state.Snapshot.Teams.Select(t => (t.TeamId, t.Name)).ToList();
                }
            }

            return result;
        }
    }

    public void RecordSnapshot(LeagueSnapshot snapshot, DateTimeOffset pollTime, bool detectorSeeded)
    {
        lock (_lock)
        {
            var state = GetOrCreate(snapshot.League.Key);
            state.Snapshot = snapshot;
            state.LastPollAt = pollTime;
            state.LastError = null;
            state.DetectorSeeded = detectorSeeded;
        }
    }

    public void RecordError(string leagueKey, string error)
    {
        lock (_lock)
        {
            var state = GetOrCreate(leagueKey);
            state.LastError = error;
        }
    }

    /// <summary>Call once per completed poll cycle (regardless of how many leagues it covered).</summary>
    public void RecordPollCycle()
    {
        lock (_lock)
        {
            _pollCount++;
        }
    }

    public void SetNextPollAt(DateTimeOffset nextPollAt)
    {
        lock (_lock)
        {
            _nextPollAt = nextPollAt;
        }
    }

    /// <summary>Resets the seeded flag for every league.</summary>
    public void ResetDetectorSeeded()
    {
        lock (_lock)
        {
            foreach (var state in _leagues.Values)
            {
                state.DetectorSeeded = false;
            }
        }
    }

    /// <summary>Resets the seeded flag for one league.</summary>
    public void ResetDetectorSeeded(string leagueKey)
    {
        lock (_lock)
        {
            GetOrCreate(leagueKey).DetectorSeeded = false;
        }
    }

    public void RecordAlert(Alert alert, bool soundFound)
    {
        lock (_lock)
        {
            _alertLog.Insert(0, new AlertLogEntryViewModel(
                alert.At,
                alert.TeamId,
                alert.LeagueKey,
                alert.TeamLabel,
                alert.Touchdown.PlayerName,
                alert.Touchdown.Type.ToString(),
                alert.Touchdown.Count,
                alert.IsTest,
                alert.SoundFile,
                soundFound));

            if (_alertLog.Count > MaxAlertLogEntries)
            {
                _alertLog.RemoveRange(MaxAlertLogEntries, _alertLog.Count - MaxAlertLogEntries);
            }
        }
    }

    public DashboardViewModel ToViewModel()
    {
        lock (_lock)
        {
            var configuredLeagues = _leaguesOptions.CurrentValue.Items;

            var leagueViewModels = configuredLeagues
                .Select(l =>
                {
                    var state = _leagues.GetValueOrDefault(l.Key);
                    return new LeagueViewModel(
                        Key: l.Key,
                        Provider: l.Provider.ToString(),
                        LeagueId: l.LeagueId,
                        Name: state?.Snapshot?.LeagueName,
                        Season: state?.Snapshot?.SeasonId,
                        Week: state?.Snapshot?.ScoringPeriodId,
                        LastPollAt: state?.LastPollAt,
                        LastError: state?.LastError,
                        DetectorSeeded: state?.DetectorSeeded ?? false);
                })
                .ToList();

            var hasSnapshot = _leagues.Values.Any(s => s.Snapshot is not null);
            var detectorSeeded = configuredLeagues.Count > 0 && leagueViewModels.All(l => l.DetectorSeeded);
            var first = leagueViewModels.FirstOrDefault();
            var firstError = leagueViewModels.Select(l => l.LastError).FirstOrDefault(e => e is not null);
            var lastPollAt = leagueViewModels.Select(l => l.LastPollAt).Where(t => t.HasValue).Select(t => t!.Value).DefaultIfEmpty().Max();

            var watchedTeams = _alertRouter.WatchedTeams
                .Select((team, index) => BuildTeamViewModel(team, ResolveColor(team, index)))
                .ToList();

            var poll = new PollHealthViewModel(
                Ok: firstError is null,
                LastPollAt: lastPollAt == default ? null : lastPollAt,
                NextPollAt: _nextPollAt,
                LastError: firstError,
                PollCount: _pollCount,
                IntervalSeconds: _pollingOptions.CurrentValue.IntervalSeconds);

            return new DashboardViewModel(
                LeagueName: first?.Name,
                SeasonId: first?.Season,
                Week: first?.Week,
                HasSnapshot: hasSnapshot,
                DetectorSeeded: detectorSeeded,
                Poll: poll,
                Leagues: leagueViewModels,
                WatchedTeams: watchedTeams,
                RecentAlerts: _alertLog.ToList());
        }
    }

    private LeagueRuntimeState GetOrCreate(string leagueKey)
    {
        if (!_leagues.TryGetValue(leagueKey, out var state))
        {
            state = new LeagueRuntimeState();
            _leagues[leagueKey] = state;
        }

        return state;
    }

    private WatchedTeamViewModel BuildTeamViewModel(WatchedTeamOptions watched, string color)
    {
        var leagueKey = watched.League ?? "";
        var snapshot = _leagues.GetValueOrDefault(leagueKey)?.Snapshot;
        var leagueName = snapshot?.LeagueName ?? leagueKey;

        var resolvedPath = _soundResolver.Resolve(watched.SoundFile);
        var label = string.IsNullOrWhiteSpace(watched.Label) ? $"Team {watched.TeamId}" : watched.Label;

        var team = snapshot?.FindTeam(watched.TeamId);
        if (team is null)
        {
            return new WatchedTeamViewModel(
                watched.TeamId,
                leagueKey,
                leagueName,
                label,
                color,
                EspnTeamName: null,
                Points: null,
                TouchdownTotal: 0,
                Opponent: null,
                SoundFile: watched.SoundFile,
                SoundFound: resolvedPath is not null,
                SoundResolvedPath: resolvedPath,
                HasRoster: false,
                Starters: Array.Empty<PlayerViewModel>(),
                Bench: Array.Empty<PlayerViewModel>());
        }

        OpponentViewModel? opponent = null;
        var matchup = snapshot!.Matchups.FirstOrDefault(m => m.HomeTeamId == watched.TeamId || m.AwayTeamId == watched.TeamId);
        if (matchup is not null)
        {
            var opponentTeamId = matchup.HomeTeamId == watched.TeamId ? matchup.AwayTeamId : matchup.HomeTeamId;
            var opponentPoints = matchup.HomeTeamId == watched.TeamId ? matchup.AwayPoints : matchup.HomePoints;
            var opponentTeam = snapshot.FindTeam(opponentTeamId);
            opponent = new OpponentViewModel(opponentTeamId, opponentTeam?.Name ?? $"Team {opponentTeamId}", opponentPoints);
        }

        var starters = team.Roster.Where(p => p.IsStarter).Select(ToPlayerViewModel).ToList();
        var bench = team.Roster.Where(p => !p.IsStarter).Select(ToPlayerViewModel).ToList();

        return new WatchedTeamViewModel(
            watched.TeamId,
            leagueKey,
            leagueName,
            label,
            color,
            EspnTeamName: team.Name,
            Points: team.Points,
            TouchdownTotal: starters.Sum(p => p.TouchdownTotal),
            Opponent: opponent,
            SoundFile: watched.SoundFile,
            SoundFound: resolvedPath is not null,
            SoundResolvedPath: resolvedPath,
            HasRoster: team.Roster.Count > 0,
            Starters: starters,
            Bench: bench);
    }

    private static PlayerViewModel ToPlayerViewModel(RosteredPlayer player) => new(
        player.LineupSlot,
        player.FullName,
        player.Position,
        player.Points,
        player.Touchdowns.Total);

    private sealed class LeagueRuntimeState
    {
        public LeagueSnapshot? Snapshot;
        public DateTimeOffset? LastPollAt;
        public string? LastError;
        public bool DetectorSeeded;
    }
}
