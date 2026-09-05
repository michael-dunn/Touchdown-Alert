using System.Net.Http;
using System.Net.Http.Json;
using TouchdownAlert.Overlay.Contracts;

namespace TouchdownAlert.Overlay.Services;

/// <summary>Writes the overlay's own settings back to the App (PUT /api/settings/overlay) after a manual drag.</summary>
public sealed class OverlaySettingsClient
{
    private readonly string _appUrl;
    private readonly FileLog _log;
    private readonly HttpClient _http;

    public OverlaySettingsClient(string appUrl, FileLog log)
    {
        _appUrl = appUrl.TrimEnd('/');
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<bool> SaveOverlayAsync(OverlaySettingsDto overlay)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"{_appUrl}/api/settings/overlay", overlay, Contracts.JsonOptions.Default);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warn($"PUT /api/settings/overlay returned {(int)response.StatusCode}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"PUT /api/settings/overlay failed: {ex.Message}");
            return false;
        }
    }
}
