using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Configuration;

public sealed class LeagueOptions
{
    /// <summary>Short user-chosen id, required, unique, referenced by WatchedTeams:*:League.</summary>
    public string Key { get; set; } = "";

    public LeagueProvider Provider { get; set; } = LeagueProvider.Espn;

    /// <summary>ESPN numeric league id as string; Yahoo league key later.</summary>
    public string LeagueId { get; set; } = "";

    /// <summary>Base URL of the provider's read API. Null = provider default (ESPN: https://lm-api-reads.fantasy.espn.com).</summary>
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
}
