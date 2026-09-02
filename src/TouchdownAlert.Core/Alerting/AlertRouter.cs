using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Alerting;

/// <summary>Turns touchdown events into per-watched-team alerts, in the configured watch order.</summary>
public sealed class AlertRouter : IAlertRouter
{
    private readonly IOptionsMonitor<AlertOptions> _options;
    private readonly ISoundFileResolver _soundFileResolver;

    public AlertRouter(IOptionsMonitor<AlertOptions> options, ISoundFileResolver soundFileResolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(soundFileResolver);
        _options = options;
        _soundFileResolver = soundFileResolver;
    }

    /// <summary>The resolver used to locate configured sound files (exposed for callers that need the sounds directory).</summary>
    public ISoundFileResolver SoundFileResolver => _soundFileResolver;

    public IReadOnlyList<Alert> Route(TouchdownEvent touchdown, LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(touchdown);
        ArgumentNullException.ThrowIfNull(snapshot);

        var alerts = new List<Alert>();

        foreach (var watched in _options.CurrentValue.WatchedTeams)
        {
            if (!touchdown.StartingTeamIds.Contains(watched.TeamId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(watched.SoundFile))
            {
                continue;
            }

            alerts.Add(new Alert(
                At: touchdown.DetectedAt,
                TeamId: watched.TeamId,
                TeamLabel: ResolveLabel(watched, snapshot),
                SoundFile: watched.SoundFile,
                Touchdown: touchdown));
        }

        return alerts;
    }

    public Alert CreateTestAlert(int teamId, LeagueSnapshot? snapshot)
    {
        var watched = _options.CurrentValue.WatchedTeams.FirstOrDefault(w => w.TeamId == teamId)
            ?? throw new ArgumentException($"Team {teamId} is not watched.", nameof(teamId));

        var now = DateTimeOffset.UtcNow;
        var touchdown = new TouchdownEvent(
            DetectedAt: now,
            ScoringPeriodId: snapshot?.ScoringPeriodId ?? 0,
            PlayerId: -1,
            PlayerName: "Test Player",
            Position: "WR",
            Type: TouchdownType.Receiving,
            Count: 1,
            StartingTeamIds: [teamId],
            BenchedTeamIds: []);

        return new Alert(
            At: now,
            TeamId: teamId,
            TeamLabel: ResolveLabel(watched, snapshot),
            SoundFile: watched.SoundFile,
            Touchdown: touchdown,
            IsTest: true);
    }

    private static string ResolveLabel(WatchedTeamOptions watched, LeagueSnapshot? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(watched.Label))
        {
            return watched.Label;
        }

        var teamName = snapshot?.FindTeam(watched.TeamId)?.Name;
        return !string.IsNullOrWhiteSpace(teamName) ? teamName : $"Team {watched.TeamId}";
    }
}
