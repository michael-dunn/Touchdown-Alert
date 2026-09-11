using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Configuration;

public sealed class LeagueOptions
{
    /// <summary>Short user-chosen id, required, unique, referenced by WatchedTeams:*:League.</summary>
    public string Key { get; set; } = "";

    public LeagueProvider Provider { get; set; } = LeagueProvider.Espn;

    /// <summary>
    /// Provider league id as a string: ESPN numeric id; Yahoo bare id or full "{game_key}.l.{id}" key; Sleeper
    /// 19-digit numeric id (the number in the league URL, e.g. 1401782105192570880).
    /// </summary>
    public string LeagueId { get; set; } = "";

    /// <summary>
    /// Base URL of the provider's read API. Null = provider default (ESPN: https://lm-api-reads.fantasy.espn.com,
    /// Yahoo: <see cref="YahooOptions.ApiBaseUrl"/>, Sleeper: <see cref="SleeperOptions.ApiBaseUrl"/> = https://api.sleeper.app).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Season year. Null = current year (auto).</summary>
    public int? SeasonId { get; set; }

    /// <summary>Force a scoring period (NFL week). Null = let the provider report the current one (auto).</summary>
    public int? ScoringPeriodId { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 20;
}

public sealed class LeaguesOptions
{
    public const string SectionName = "Leagues";

    public List<LeagueOptions> Items { get; set; } = new();
}

public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    /// <summary>Seconds between league polls.</summary>
    public int IntervalSeconds { get; set; } = 30;
}

public sealed class WatchedTeamOptions
{
    /// <summary>Fantasy team id within the league.</summary>
    public int TeamId { get; set; }

    /// <summary>
    /// The league Key this team lives in. If null/blank and exactly one league is configured, defaults to
    /// that league. Blank with multiple leagues configured fails fast at startup.
    /// </summary>
    public string? League { get; set; }

    /// <summary>Friendly name shown on the dashboard. Defaults to the provider team name when blank.</summary>
    public string? Label { get; set; }

    /// <summary>Sound file name (relative to Sounds:Directory) or absolute path. mp3 or wav.</summary>
    public string SoundFile { get; set; } = string.Empty;

    /// <summary>
    /// Team color as a CSS hex string (e.g. "#22c55e"), used on the dashboard tile, overlay row and TD banner.
    /// Blank = a default from a built-in palette, chosen by the team's position in the list.
    /// </summary>
    public string? Color { get; set; }
}

/// <summary>Position and look of the always-on-top TV overlay. Written by the control page and the overlay itself.</summary>
public sealed class OverlayOptions
{
    public const string SectionName = "Overlay";

    /// <summary>Left edge in screen pixels. Null = default placement (top-right of <see cref="Display"/>).</summary>
    public double? X { get; set; }

    /// <summary>Top edge in screen pixels. Null = default placement.</summary>
    public double? Y { get; set; }

    /// <summary>Which display hosts the overlay: "primary" (the TV in the agreed setup), "secondary", or a zero-based index.</summary>
    public string Display { get; set; } = "primary";

    /// <summary>Size multiplier, 0.5 - 2.0.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Background opacity, 0.0 - 1.0.</summary>
    public double Opacity { get; set; } = 0.7;

    /// <summary>Locked = click-through and not draggable. Unlock from the control page to reposition.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>Whether the overlay window should be shown at all.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class SoundOptions
{
    public const string SectionName = "Sounds";

    /// <summary>
    /// Directory holding the sound files. Relative paths resolve against the repo root
    /// (the nearest ancestor folder containing TouchdownAlert.slnx), then the current directory.
    /// </summary>
    public string Directory { get; set; } = "sounds";

    /// <summary>Output volume 0.0 - 1.0.</summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// Longest any one clip is allowed to play, in seconds. Longer files are cut off at this point.
    /// Zero or negative disables the cap.
    /// </summary>
    public double MaxDurationSeconds { get; set; } = 5;
}

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>
    /// Up to four teams to watch. Order matters: when one TD involves several watched teams,
    /// sounds play in this order.
    /// </summary>
    public List<WatchedTeamOptions> WatchedTeams { get; set; } = new();

    /// <summary>
    /// How long each touchdown banner stays on screen, in seconds. Alerts are presented one at a time at this
    /// cadence: each alert's sound starts exactly when its banner appears, so back-to-back touchdowns never
    /// have one team's sound playing under another team's banner. Zero or negative presents every alert
    /// immediately (tests). Lives in appsettings.json, not config/settings.json.
    /// </summary>
    public double BannerSeconds { get; set; } = 10;
}

public sealed class YahooOptions
{
    public const string SectionName = "Yahoo";

    /// <summary>OAuth2 client id from a Yahoo developer app (Fantasy Sports read permission).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth2 client secret from the same Yahoo developer app.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Base authorize URL (no query string - <see cref="Yahoo.YahooAuthService"/> appends
    /// client_id/redirect_uri/response_type/language) where the user logs in and gets a short code to paste back in.
    /// </summary>
    public string AuthorizeUrl { get; set; } = "https://api.login.yahoo.com/oauth2/request_auth";

    /// <summary>Token exchange/refresh endpoint.</summary>
    public string TokenUrl { get; set; } = "https://api.login.yahoo.com/oauth2/get_token";

    /// <summary>Base URL of the Yahoo Fantasy read API (trailing slash).</summary>
    public string ApiBaseUrl { get; set; } = "https://fantasysports.yahooapis.com/fantasy/v2/";

    /// <summary>
    /// Where the OAuth token (access + refresh) is persisted between runs. Relative paths resolve the same
    /// way <see cref="SoundOptions.Directory"/> does: against the nearest ancestor folder containing
    /// TouchdownAlert.slnx, else the current directory.
    /// </summary>
    public string TokenFilePath { get; set; } = "config/yahoo-token.json";

    /// <summary>Yahoo's "out of band" redirect for apps that can't host a callback URL.</summary>
    public string RedirectUri { get; set; } = "oob";

    /// <summary>
    /// OAuth scope requested at login. "fspt-r" is Fantasy Sports read access; the developer app must also have
    /// the Fantasy Sports permission enabled or Yahoo answers every fantasy call with
    /// "additional_authorization_required". Blank to omit the parameter.
    /// </summary>
    public string Scope { get; set; } = "fspt-r";
}

public sealed class SleeperOptions
{
    public const string SectionName = "Sleeper";

    /// <summary>Base URL of Sleeper's public read API (no auth). A league's <see cref="LeagueOptions.BaseUrl"/> overrides it.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.sleeper.app";

    /// <summary>
    /// Where the trimmed copy of Sleeper's ~15 MB player dictionary (GET /v1/players/nfl) is cached between
    /// runs. Sleeper asks clients to fetch that endpoint at most once a day, so it is never requested per poll.
    /// Relative paths resolve the same way <see cref="YahooOptions.TokenFilePath"/> does: against the nearest
    /// ancestor folder containing TouchdownAlert.slnx, else the current directory.
    /// </summary>
    public string PlayersCacheFilePath { get; set; } = "config/sleeper-players.json";

    /// <summary>Age after which the player cache is re-downloaded. Sleeper's stated limit is one call per day.</summary>
    public int PlayersCacheMaxAgeHours { get; set; } = 24;
}
