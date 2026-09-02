using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Abstractions;

/// <summary>Source of league snapshots. The real implementation calls ESPN; tests use fakes.</summary>
public interface ILeagueSource
{
    Task<LeagueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Stateful detector. Feed it every snapshot in order; it returns the touchdowns that happened since the previous one.
/// The first snapshot (or the first after a scoring period change) seeds state and returns no events.
/// Decreases (stat corrections) are ignored but the stored counters are updated.
/// </summary>
public interface ITouchdownDetector
{
    bool IsSeeded { get; }
    int? SeededScoringPeriodId { get; }
    IReadOnlyList<TouchdownEvent> Update(LeagueSnapshot snapshot);
    void Reset();
}

/// <summary>
/// Turns a touchdown event into zero or more alerts, one per watched team that starts the scoring player,
/// in configured order.
/// </summary>
public interface IAlertRouter
{
    IReadOnlyList<Alert> Route(TouchdownEvent touchdown, LeagueSnapshot snapshot);
    Alert CreateTestAlert(int teamId, LeagueSnapshot? snapshot);
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
