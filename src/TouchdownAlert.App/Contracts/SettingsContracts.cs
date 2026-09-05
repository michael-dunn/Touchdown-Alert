using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.App.Contracts;

/// <summary>One league entry as exposed by the settings API - everything in <see cref="LeagueOptions"/> except
/// the internal-only <see cref="LeagueOptions.RequestTimeoutSeconds"/>.</summary>
public sealed record SettingsLeagueDto(string Key, LeagueProvider Provider, string LeagueId, string? BaseUrl, int? SeasonId, int? ScoringPeriodId)
{
    public LeagueOptions ToLeagueOptions() => new()
    {
        Key = Key,
        Provider = Provider,
        LeagueId = LeagueId,
        BaseUrl = BaseUrl,
        SeasonId = SeasonId,
        ScoringPeriodId = ScoringPeriodId,
    };

    public static SettingsLeagueDto FromLeagueOptions(LeagueOptions league) =>
        new(league.Key, league.Provider, league.LeagueId, league.BaseUrl, league.SeasonId, league.ScoringPeriodId);
}

/// <summary>Sounds settings exposed by the API - everything in <see cref="SoundOptions"/> except the
/// host-only <see cref="SoundOptions.Directory"/> (that stays in appsettings.json).</summary>
public sealed record SettingsSoundsDto(float Volume, double MaxDurationSeconds);

/// <summary>The settings document body, shared by GET (as part of <see cref="SettingsResponse"/>) and PUT
/// (as the whole request body).</summary>
public sealed record SettingsBodyDto(
    List<SettingsLeagueDto> Leagues,
    List<WatchedTeamOptions> WatchedTeams,
    PollingOptions Polling,
    SettingsSoundsDto Sounds,
    OverlayOptions Overlay);

public sealed record LeagueTeamDto(int TeamId, string Name);

public sealed record YahooStatusDto(bool IsConfigured, bool IsLoggedIn);

public sealed record SettingsMetaDto(
    bool RestartRequired,
    string SettingsFile,
    string SoundsDirectory,
    IReadOnlyList<string> AvailableSounds,
    IReadOnlyDictionary<string, IReadOnlyList<LeagueTeamDto>> LeagueTeams,
    YahooStatusDto Yahoo);

/// <summary>GET /api/settings response: the document plus <see cref="Meta"/>.</summary>
public sealed record SettingsResponse(
    IReadOnlyList<SettingsLeagueDto> Leagues,
    IReadOnlyList<WatchedTeamOptions> WatchedTeams,
    PollingOptions Polling,
    SettingsSoundsDto Sounds,
    OverlayOptions Overlay,
    SettingsMetaDto Meta);

public sealed record SettingsPutResponse(bool Ok, bool RestartRequired);

public sealed record SettingsErrorResponse(IReadOnlyList<string> Errors);

public sealed record OverlayPutResponse(bool Ok, OverlayOptions Overlay);

/// <summary>The typed settings document, as read from/written to config/settings.json.</summary>
public sealed record SettingsDocument(
    List<LeagueOptions> Leagues,
    List<WatchedTeamOptions> WatchedTeams,
    PollingOptions Polling,
    SettingsSoundsDto Sounds,
    OverlayOptions Overlay);
