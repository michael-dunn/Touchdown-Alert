using Microsoft.AspNetCore.SignalR;
using TouchdownAlert.App.Services;

namespace TouchdownAlert.App.Hubs;

/// <summary>
/// SignalR hub at /hub. Server pushes "state" (full DashboardViewModel) after every poll
/// and "alert" whenever an alert fires. New connections get the current state immediately.
/// </summary>
public sealed class DashboardHub : Hub
{
    private readonly DashboardState _state;

    public DashboardHub(DashboardState state)
    {
        _state = state;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("state", _state.ToViewModel());
        await base.OnConnectedAsync();
    }
}
