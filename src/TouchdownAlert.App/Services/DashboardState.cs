using Microsoft.Extensions.Options;
using TouchdownAlert.App.Contracts;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.App.Services;

/// <summary>
/// Thread-safe in-memory snapshot of everything the dashboard needs to render:
/// last league snapshot, poll health, and a capped alert log. Single source of
/// truth consumed by the HTTP endpoints, the SignalR hub, and PollingService/AlertDispatcher.
/// </summary>
public sealed class DashboardState
{
    private const int MaxAlertLogEntries = 200;

    private readonly object _lock = new();
    private readonly IOptionsMonitor<AlertOptions> _alertOptions;
    private readonly ISoundFileResolver _soundResolver;

    private LeagueSnapshot? _lastSnapshot;
    private DateTimeOffset? _lastPollAt;
    private DateTimeOffset? _nextPollAt;
    private string? _lastError;
    private long _pollCount;
    private bool _detectorSeeded;
    private readonly List<AlertLogEntryViewModel> _alertLog = new();

    public DashboardState(IOptionsMonitor<AlertOptions> alertOptions, ISoundFileResolver soundResolver)
    {
        _alertOptions = alertOptions;
        _soundResolver = soundResolver;
    }

    /// <summary>Last successfully fetched snapshot, if any. Used by the test-alert endpoint.</summary>
    public LeagueSnapshot? LastSnapshot
    {
        get { lock (_lock) { return _lastSnapshot; } }
    }

    public void RecordSnapshot(LeagueSnapshot snapshot, DateTimeOffset pollTime, bool detectorSeeded)
    {
        lock (_lock)
        {
            _lastSnapshot = snapshot;
            _lastPollAt = pollTime;
            _lastError = null;
            _detectorSeeded = detectorSeeded;
            _pollCount++;
        }
    }

    public void RecordError(string error)
    {
        lock (_lock)
        {
            _lastError = error;
        }
    }

    public void SetNextPollAt(DateTimeOffset nextPollAt)
    {
        lock (_lock)
        {
            _nextPollAt = nextPollAt;
        }
    }

    public void ResetDetectorSeeded()
    {
        lock (_lock)
        {
            _detectorSeeded = false;
        }
    }

    public void RecordAlert(Alert alert, bool soundFound)
    {
        lock (_lock)
        {
            _alertLog.Insert(0, new AlertLogEntryViewModel(
                alert.At,
                alert.TeamId,
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
            var snapshot = _lastSnapshot;
            var watchedTeams = _alertOptions.CurrentValue.WatchedTeams
                .Select(w => BuildTeamViewModel(w, snapshot))
                .ToList();

            var poll = new PollHealthViewModel(
                Ok: _lastError is null,
                LastPollAt: _lastPollAt,
                NextPollAt: _nextPollAt,
                LastError: _lastError,
                PollCount: _pollCount);

            return new DashboardViewModel(
                LeagueName: snapshot?.LeagueName,
                SeasonId: snapshot?.SeasonId,
                Week: snapshot?.ScoringPeriodId,
                HasSnapshot: snapshot is not null,
                DetectorSeeded: _detectorSeeded,
                Poll: poll,
                WatchedTeams: watchedTeams,
                RecentAlerts: _alertLog.ToList());
        }
    }

    private WatchedTeamViewModel BuildTeamViewModel(WatchedTeamOptions watched, LeagueSnapshot? snapshot)
    {
        var resolvedPath = _soundResolver.Resolve(watched.SoundFile);
        var label = string.IsNullOrWhiteSpace(watched.Label) ? $"Team {watched.TeamId}" : watched.Label;

        var team = snapshot?.FindTeam(watched.TeamId);
        if (team is null)
        {
            return new WatchedTeamViewModel(
                watched.TeamId,
                label,
                EspnTeamName: null,
                Points: null,
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
            label,
            EspnTeamName: team.Name,
            Points: team.Points,
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
}
