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

/// <summary>One touchdown type's count across a team's current starters. <see cref="Type"/> is the
/// <see cref="TouchdownAlert.Core.Models.TouchdownType"/> name; <see cref="Label"/> is the short dashboard label.</summary>
public sealed record TouchdownTypeCountViewModel(string Type, string Label, int Count);

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
    /// <summary>Per-type breakdown of <see cref="TouchdownTotal"/>, in canonical type order. Passing, rushing and
    /// receiving are always present (even at zero); return/defensive types appear only once they have a count.</summary>
    IReadOnlyList<TouchdownTypeCountViewModel> TouchdownsByType,
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
    bool SoundFound,
    /// <summary>How long clients should show this alert's banner, in seconds. Set by <see cref="Services.AlertPresenter"/>
    /// on the "alert" hub event; the same value paces consecutive alerts so sound and banner start together.</summary>
    double DisplaySeconds = 10);
