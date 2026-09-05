using TouchdownAlert.App.Contracts;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Yahoo;

namespace TouchdownAlert.App.Services;

/// <summary>
/// Builds the exact JSON shape served by GET /api/settings and pushed to SignalR clients as the "settings"
/// event, so the HTTP endpoint, the hub's on-connect push, and the debounced change-triggered push all agree.
/// </summary>
public sealed class SettingsResponseBuilder
{
    private static readonly string[] SoundExtensions = { ".mp3", ".wav" };

    private readonly SettingsStore _store;
    private readonly ISoundFileResolver _soundResolver;
    private readonly DashboardState _dashboardState;
    private readonly IYahooAuthService _yahooAuth;

    public SettingsResponseBuilder(SettingsStore store, ISoundFileResolver soundResolver, DashboardState dashboardState, IYahooAuthService yahooAuth)
    {
        _store = store;
        _soundResolver = soundResolver;
        _dashboardState = dashboardState;
        _yahooAuth = yahooAuth;
    }

    public async Task<SettingsResponse> BuildAsync()
    {
        var doc = _store.Read();

        var availableSounds = Directory.Exists(_soundResolver.SoundsDirectory)
            ? Directory.EnumerateFiles(_soundResolver.SoundsDirectory)
                .Where(f => SoundExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(n => n is not null)
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        var leagueTeams = new Dictionary<string, IReadOnlyList<LeagueTeamDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (leagueKey, teams) in _dashboardState.GetAllLeagueTeams())
        {
            leagueTeams[leagueKey] = teams.Select(t => new LeagueTeamDto(t.TeamId, t.Name)).ToList();
        }

        foreach (var league in doc.Leagues)
        {
            leagueTeams.TryAdd(league.Key, Array.Empty<LeagueTeamDto>());
        }

        var yahooStatus = await _yahooAuth.GetStatusAsync();

        var meta = new SettingsMetaDto(
            RestartRequired: _store.IsRestartRequired(doc.Leagues),
            SettingsFile: _store.FilePath,
            SoundsDirectory: _soundResolver.SoundsDirectory,
            AvailableSounds: availableSounds,
            LeagueTeams: leagueTeams,
            Yahoo: new YahooStatusDto(yahooStatus.IsConfigured, yahooStatus.IsLoggedIn));

        return new SettingsResponse(
            doc.Leagues.Select(SettingsLeagueDto.FromLeagueOptions).ToList(),
            doc.WatchedTeams,
            doc.Polling,
            doc.Sounds,
            doc.Overlay,
            meta);
    }
}
