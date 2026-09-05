using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Alerting;

/// <summary>Turns touchdown events into per-watched-team alerts, in the configured watch order.</summary>
public sealed class AlertRouter : IAlertRouter
{
    private readonly IOptionsMonitor<AlertOptions> _options;
    private readonly IOptionsMonitor<LeaguesOptions> _leaguesOptions;
    private readonly IOptionsMonitor<YahooOptions> _yahooOptions;
    private readonly ISoundFileResolver _soundFileResolver;

    public AlertRouter(
        IOptionsMonitor<AlertOptions> options,
        IOptionsMonitor<LeaguesOptions> leaguesOptions,
        IOptionsMonitor<YahooOptions> yahooOptions,
        ISoundFileResolver soundFileResolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(leaguesOptions);
        ArgumentNullException.ThrowIfNull(yahooOptions);
        ArgumentNullException.ThrowIfNull(soundFileResolver);
        _options = options;
        _leaguesOptions = leaguesOptions;
        _yahooOptions = yahooOptions;
        _soundFileResolver = soundFileResolver;
    }

    /// <summary>The resolver used to locate configured sound files (exposed for callers that need the sounds directory).</summary>
    public ISoundFileResolver SoundFileResolver => _soundFileResolver;

    /// <summary>The resolved watched-team list: each entry's League is filled in with the default league key
    /// where it was blank in configuration.</summary>
    public IReadOnlyList<WatchedTeamOptions> WatchedTeams
    {
        get
        {
            var watchedTeams = _options.CurrentValue.WatchedTeams;
            var leagues = _leaguesOptions.CurrentValue.Items;
            LeagueConfigurationValidator.ValidateAndResolve(leagues, watchedTeams, _yahooOptions.CurrentValue);
            return watchedTeams;
        }
    }

    public IReadOnlyList<Alert> Route(TouchdownEvent touchdown, LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(touchdown);
        ArgumentNullException.ThrowIfNull(snapshot);

        var alerts = new List<Alert>();

        foreach (var watched in WatchedTeams)
        {
            if (!string.Equals(watched.League, touchdown.League.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
                LeagueKey: touchdown.League.Key,
                TeamLabel: ResolveLabel(watched, snapshot),
                SoundFile: watched.SoundFile,
                Touchdown: touchdown));
        }

        return alerts;
    }

    public Alert CreateTestAlert(string leagueKey, int teamId, LeagueSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(leagueKey);

        var watched = WatchedTeams.FirstOrDefault(w => w.TeamId == teamId && string.Equals(w.League, leagueKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Team {teamId} is not watched in league \"{leagueKey}\".", nameof(teamId));

        var league = _leaguesOptions.CurrentValue.Items.FirstOrDefault(l => string.Equals(l.Key, leagueKey, StringComparison.OrdinalIgnoreCase));
        var leagueRef = new LeagueRef(leagueKey, league?.Provider ?? LeagueProvider.Espn, league?.LeagueId ?? "");

        var now = DateTimeOffset.UtcNow;
        var touchdown = new TouchdownEvent(
            League: leagueRef,
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
            LeagueKey: leagueKey,
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
