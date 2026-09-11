using Microsoft.AspNetCore.SignalR;
using TouchdownAlert.App.Hubs;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.App.Services;

/// <summary>
/// The single place alerts flow through, for both real and test alerts: resolves the sound path, appends to
/// the dashboard alert log and pushes the new state, then hands the alert to <see cref="AlertPresenter"/>,
/// which plays the sound and sends the "alert" hub event together on the banner cadence.
/// </summary>
public sealed class AlertDispatcher
{
    private readonly ISoundFileResolver _soundResolver;
    private readonly AlertPresenter _presenter;
    private readonly DashboardState _state;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(
        ISoundFileResolver soundResolver,
        AlertPresenter presenter,
        DashboardState state,
        IHubContext<DashboardHub> hub,
        ILogger<AlertDispatcher> logger)
    {
        _soundResolver = soundResolver;
        _presenter = presenter;
        _state = state;
        _hub = hub;
        _logger = logger;
    }

    public async Task DispatchAsync(Alert alert)
    {
        var resolvedPath = _soundResolver.Resolve(alert.SoundFile);
        var soundFound = resolvedPath is not null;

        _logger.LogInformation(
            "{Kind} alert for {TeamLabel} ({TeamId}): {Player} {Type} x{Count} -> {SoundFile}",
            alert.IsTest ? "TEST" : "TOUCHDOWN", alert.TeamLabel, alert.TeamId,
            alert.Touchdown.PlayerName, alert.Touchdown.Type, alert.Touchdown.Count, alert.SoundFile);

        if (!soundFound)
        {
            _logger.LogWarning(
                "Sound file {SoundFile} not found for team {TeamLabel} ({TeamId}); showing alert without audio",
                alert.SoundFile, alert.TeamLabel, alert.TeamId);
        }

        var entry = new Contracts.AlertLogEntryViewModel(
            alert.At,
            alert.TeamId,
            alert.LeagueKey,
            alert.TeamLabel,
            alert.Touchdown.PlayerName,
            alert.Touchdown.Type.ToString(),
            alert.Touchdown.Count,
            alert.IsTest,
            alert.SoundFile,
            soundFound);

        // Banner + sound are paced by the presenter (immediate when nothing is showing); the alert log and
        // state update go out right away regardless.
        _presenter.Enqueue(entry, resolvedPath);
        _state.RecordAlert(alert, soundFound);
        await _hub.Clients.All.SendAsync("state", _state.ToViewModel());
    }
}
