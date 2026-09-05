using Microsoft.AspNetCore.SignalR;
using TouchdownAlert.App.Services;

namespace TouchdownAlert.App.Hubs;

/// <summary>
/// SignalR hub at /hub. Server pushes "state" (full DashboardViewModel) after every poll, "settings" whenever
/// config/settings.json changes, and "alert" whenever an alert fires. New connections get "state" and
/// "settings" immediately.
/// </summary>
public sealed class DashboardHub : Hub
{
    private readonly DashboardState _state;
    private readonly SettingsResponseBuilder _settingsResponseBuilder;

    public DashboardHub(DashboardState state, SettingsResponseBuilder settingsResponseBuilder)
    {
        _state = state;
        _settingsResponseBuilder = settingsResponseBuilder;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("state", _state.ToViewModel());
        await Clients.Caller.SendAsync("settings", await _settingsResponseBuilder.BuildAsync());
        await base.OnConnectedAsync();
    }
}
