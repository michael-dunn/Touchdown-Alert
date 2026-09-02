using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Abstractions;

/// <summary>Source of league snapshots for one configured league. The real implementation calls ESPN; tests use fakes.</summary>
public interface ILeagueSource
{
    LeagueRef League { get; }

    Task<LeagueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Stateful detector, with state partitioned per league (by <see cref="LeagueRef.Key"/>). Feed it every
/// snapshot in order; it returns the touchdowns that happened since the previous snapshot for that league.
/// The first snapshot for a league (or the first after a scoring period change) seeds state for that league
/// and returns no events. Decreases (stat corrections) are ignored but the stored counters are updated.
/// </summary>
public interface ITouchdownDetector
{
    bool IsSeeded(string leagueKey);

    bool AllSeeded(IEnumerable<string> leagueKeys);

    int? SeededScoringPeriodId(string leagueKey);

    IReadOnlyList<TouchdownEvent> Update(LeagueSnapshot snapshot);

    /// <summary>Resets state for every league.</summary>
    void Reset();

    /// <summary>Resets state for one league only.</summary>
    void Reset(string leagueKey);
}

/// <summary>
/// Turns a touchdown event into zero or more alerts, one per watched team (in the same league as the
/// touchdown) that starts the scoring player, in configured order.
/// </summary>
public interface IAlertRouter
{
    IReadOnlyList<Alert> Route(TouchdownEvent touchdown, LeagueSnapshot snapshot);

    Alert CreateTestAlert(string leagueKey, int teamId, LeagueSnapshot? snapshot);

    /// <summary>The resolved watched-team list: each entry's <see cref="WatchedTeamOptions.League"/> is filled
    /// in with the default league key where it was blank in configuration.</summary>
    IReadOnlyList<WatchedTeamOptions> WatchedTeams { get; }
}

/// <summary>Plays sounds one at a time in FIFO order. Implementations must be safe to call from any thread.</summary>
public interface ISoundPlayer
{
    /// <summary>Queue a sound. Returns immediately.</summary>
    void Enqueue(string soundFilePath);
    int QueueLength { get; }
}

/// <summary>Resolves a configured sound file name to an absolute path, or null if it does not exist.</summary>
public interface ISoundFileResolver
{
    string SoundsDirectory { get; }
    string? Resolve(string soundFile);
}
