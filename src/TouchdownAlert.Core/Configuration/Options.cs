namespace TouchdownAlert.Core.Configuration;

public sealed class EspnOptions
{
    public const string SectionName = "Espn";

    /// <summary>Base URL of the ESPN fantasy read API. Point at the simulator for integration testing.</summary>
    public string BaseUrl { get; set; } = "https://lm-api-reads.fantasy.espn.com";

    public int LeagueId { get; set; } = 998946988;

    /// <summary>Season year. Null = current year (auto).</summary>
    public int? SeasonId { get; set; }

    /// <summary>Force a scoring period (NFL week). Null = let ESPN report the current one (auto).</summary>
    public int? ScoringPeriodId { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 20;
}

public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    /// <summary>Seconds between ESPN polls.</summary>
    public int IntervalSeconds { get; set; } = 30;
}

public sealed class WatchedTeamOptions
{
    /// <summary>ESPN fantasy team id within the league.</summary>
    public int TeamId { get; set; }

    /// <summary>Friendly name shown on the dashboard. Defaults to the ESPN team name when blank.</summary>
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
