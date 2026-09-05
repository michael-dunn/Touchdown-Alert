using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TouchdownAlert.Overlay.Contracts;

namespace TouchdownAlert.Overlay.Services;

/// <summary>
/// Connects to the App's SignalR hub at {appUrl}/hub and its HTTP API. Reconnects forever with backoff
/// (SignalR's own WithAutomaticReconnect for drops after the first connect, plus a manual retry loop for
/// the initial connect attempt, since AutomaticReconnect only covers connections that succeeded once).
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private static readonly TimeSpan[] ReconnectDelays =
    {
        TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    };

    private readonly string _appUrl;
    private readonly FileLog _log;
    private readonly HttpClient _http;
    private readonly HubConnection _connection;
    private CancellationTokenSource? _initialConnectCts;

    public event Action<StateDto>? StateReceived;
    public event Action<AlertDto>? AlertReceived;
    public event Action<SettingsDto>? SettingsReceived;
    public event Action<bool>? ConnectionChanged;

    public HubClient(string appUrl, FileLog log)
    {
        _appUrl = appUrl.TrimEnd('/');
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_appUrl}/hub")
            .WithAutomaticReconnect(ReconnectDelays)
            .Build();

        _connection.On<StateDto>("state", dto =>
        {
            _log.Info("Received state event");
            StateReceived?.Invoke(dto);
        });
        _connection.On<AlertDto>("alert", dto =>
        {
            _log.Info($"Received alert event: team {dto.TeamId} {dto.PlayerName} {dto.TouchdownType}");
            AlertReceived?.Invoke(dto);
        });
        _connection.On<SettingsDto>("settings", dto =>
        {
            _log.Info("Received settings event");
            SettingsReceived?.Invoke(dto);
        });

        _connection.Reconnecting += ex =>
        {
            _log.Warn($"Hub connection lost, reconnecting: {ex?.Message}");
            ConnectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            _log.Info("Hub reconnected");
            ConnectionChanged?.Invoke(true);
            return Task.CompletedTask;
        };
        _connection.Closed += async ex =>
        {
            _log.Warn($"Hub connection closed: {ex?.Message}");
            ConnectionChanged?.Invoke(false);
            await RetryInitialConnectAsync();
        };
    }

    /// <summary>Fetches the current state/settings once via HTTP (settings may 404 before Engineer A lands), then starts the hub connection with a forever-retry loop.</summary>
    public async Task StartAsync()
    {
        await FetchInitialStateAsync();
        await FetchInitialSettingsAsync();
        await RetryInitialConnectAsync();
    }

    private async Task FetchInitialStateAsync()
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<StateDto>($"{_appUrl}/api/state", Contracts.JsonOptions.Default);
            if (dto is not null)
            {
                StateReceived?.Invoke(dto);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"GET /api/state failed (app may not be up yet): {ex.Message}");
        }
    }

    private async Task FetchInitialSettingsAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_appUrl}/api/settings");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _log.Info("GET /api/settings returned 404 (settings API not deployed yet) - using overlay defaults");
                return;
            }

            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<SettingsDto>(Contracts.JsonOptions.Default);
            if (dto is not null)
            {
                SettingsReceived?.Invoke(dto);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"GET /api/settings failed - using overlay defaults: {ex.Message}");
        }
    }

    private async Task RetryInitialConnectAsync()
    {
        _initialConnectCts?.Cancel();
        var cts = new CancellationTokenSource();
        _initialConnectCts = cts;

        var attempt = 0;
        while (!cts.IsCancellationRequested)
        {
            if (_connection.State != HubConnectionState.Disconnected)
            {
                return;
            }

            try
            {
                await _connection.StartAsync(cts.Token);
                _log.Info("Hub connected");
                ConnectionChanged?.Invoke(true);
                return;
            }
            catch (Exception ex)
            {
                var delay = ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)];
                attempt++;
                _log.Warn($"Hub connect attempt failed ({ex.Message}); retrying in {delay.TotalSeconds}s");
                ConnectionChanged?.Invoke(false);
                try
                {
                    await Task.Delay(delay == TimeSpan.Zero ? TimeSpan.FromSeconds(2) : delay, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _initialConnectCts?.Cancel();
        _http.Dispose();
        await _connection.DisposeAsync();
    }
}
