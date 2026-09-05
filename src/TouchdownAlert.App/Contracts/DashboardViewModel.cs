namespace TouchdownAlert.App.Contracts;

/// <summary>Full snapshot of dashboard state, pushed to SignalR clients and served from GET /api/state.</summary>
public sealed record DashboardViewModel(
    string? LeagueName,
    int? SeasonId,
    int? Week,
    bool HasSnapshot,
    bool DetectorSeeded,
    PollHealthViewModel Poll,
    IReadOnlyList<LeagueViewModel> Leagues,
    IReadOnlyList<WatchedTeamViewModel> WatchedTeams,
    IReadOnlyList<AlertLogEntryViewModel> RecentAlerts);

public sealed record PollHealthViewModel(
    bool Ok,
    DateTimeOffset? LastPollAt,
    DateTimeOffset? NextPollAt,
    string? LastError,
    long PollCount,
    /// <summary>Configured poll interval, so clients can judge staleness (older than two intervals = stale).</summary>
    int IntervalSeconds);

/// <summary>One configured league's status, for the dashboard's per-league chip.</summary>
public sealed record LeagueViewModel(
    string Key,
    string Provider,
    string LeagueId,
    string? Name,
    int? Season,
    int? Week,
    DateTimeOffset? LastPollAt,
    string? LastError,
    bool DetectorSeeded);

public sealed record OpponentViewModel(int TeamId, string Name, double Points);

public sealed record PlayerViewModel(
    string Slot,
    string Name,
    string Position,
    double Points,
    int TouchdownTotal);

public sealed record WatchedTeamViewModel(
    int TeamId,
    string LeagueKey,
    string LeagueName,
    string Label,
    /// <summary>Resolved CSS hex color (configured or palette default). Never null.</summary>
    string Color,
    string? EspnTeamName,
    double? Points,
    /// <summary>Sum of touchdown counters across the team's current starters this week (the "(8)" on the tile).</summary>
    int TouchdownTotal,
    OpponentViewModel? Opponent,
    string SoundFile,
    bool SoundFound,
    string? SoundResolvedPath,
    bool HasRoster,
    IReadOnlyList<PlayerViewModel> Starters,
    IReadOnlyList<PlayerViewModel> Bench);

public sealed record AlertLogEntryViewModel(
    DateTimeOffset At,
    int TeamId,
    string LeagueKey,
    string TeamLabel,
    string PlayerName,
    string TouchdownType,
    int Count,
    bool IsTest,
    string SoundFile,
    bool SoundFound);
