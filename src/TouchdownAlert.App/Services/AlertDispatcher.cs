using Microsoft.AspNetCore.SignalR;
using TouchdownAlert.App.Hubs;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.App.Services;

/// <summary>
/// The single place alerts flow through, for both real and test alerts: resolves the sound
/// path, enqueues playback, appends to the dashboard alert log, and broadcasts to SignalR clients.
/// </summary>
public sealed class AlertDispatcher
{
    private readonly ISoundFileResolver _soundResolver;
    private readonly ISoundPlayer _soundPlayer;
    private readonly DashboardState _state;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(
        ISoundFileResolver soundResolver,
        ISoundPlayer soundPlayer,
        DashboardState state,
        IHubContext<DashboardHub> hub,
        ILogger<AlertDispatcher> logger)
    {
        _soundResolver = soundResolver;
        _soundPlayer = soundPlayer;
        _state = state;
        _hub = hub;
        _logger = logger;
    }

    public async Task DispatchAsync(Alert alert)
    {
        var resolvedPath = _soundResolver.Resolve(alert.SoundFile);
        var soundFound = resolvedPath is not null;

        if (soundFound)
        {
            _soundPlayer.Enqueue(resolvedPath!);
        }
        else
        {
            _logger.LogWarning(
                "Sound file {SoundFile} not found for team {TeamLabel} ({TeamId}); showing alert without audio",
                alert.SoundFile, alert.TeamLabel, alert.TeamId);
        }

        _state.RecordAlert(alert, soundFound);

        var entry = new Contracts.AlertLogEntryViewModel(
            alert.At,
            alert.TeamId,
            alert.TeamLabel,
            alert.Touchdown.PlayerName,
            alert.Touchdown.Type.ToString(),
            alert.Touchdown.Count,
            alert.IsTest,
            alert.SoundFile,
            soundFound);

        await _hub.Clients.All.SendAsync("alert", entry);
        await _hub.Clients.All.SendAsync("state", _state.ToViewModel());
    }
}
