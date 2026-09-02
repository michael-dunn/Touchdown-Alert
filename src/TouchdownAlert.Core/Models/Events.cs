namespace TouchdownAlert.Core.Models;

/// <summary>
/// A touchdown detected by diffing two consecutive snapshots. One event per (player, type);
/// <see cref="Count"/> is normally 1 but can be higher if a player scored twice between polls.
/// </summary>
/// <param name="StartingTeamIds">Fantasy team ids that have this player in a STARTING slot right now.</param>
/// <param name="BenchedTeamIds">Fantasy team ids that have this player on the bench/IR (informational only, never alerted).</param>
public sealed record TouchdownEvent(
    DateTimeOffset DetectedAt,
    int ScoringPeriodId,
    long PlayerId,
    string PlayerName,
    string Position,
    TouchdownType Type,
    int Count,
    IReadOnlyList<int> StartingTeamIds,
    IReadOnlyList<int> BenchedTeamIds);

/// <summary>A sound that should be played for a watched team because of a touchdown.</summary>
public sealed record Alert(
    DateTimeOffset At,
    int TeamId,
    string TeamLabel,
    string SoundFile,
    TouchdownEvent Touchdown,
    bool IsTest = false);
