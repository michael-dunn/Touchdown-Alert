using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchdownAlert.Core.Espn.Wire;

/// <summary>
/// Wire-format DTOs for the ESPN fantasy v3 league response
/// (GET /apis/v3/games/ffl/seasons/{season}/segments/0/leagues/{leagueId}?view=mBoxscore&amp;view=mMatchupScore&amp;view=mTeam&amp;view=mSettings).
/// Only the fields we read are modelled. Shared by the real parser and the simulator so both sides agree on shape.
/// Unknown fields are ignored on read and never written.
/// </summary>
public sealed class EspnLeagueResponse
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("gameId")] public int GameId { get; set; } = 1;
    [JsonPropertyName("seasonId")] public int SeasonId { get; set; }
    /// <summary>Current NFL week per ESPN (0 during preseason).</summary>
    [JsonPropertyName("scoringPeriodId")] public int ScoringPeriodId { get; set; }
    [JsonPropertyName("settings")] public EspnSettings? Settings { get; set; }
    [JsonPropertyName("status")] public EspnStatus? Status { get; set; }
    [JsonPropertyName("teams")] public List<EspnTeam> Teams { get; set; } = new();
    [JsonPropertyName("schedule")] public List<EspnMatchup> Schedule { get; set; } = new();
}

public sealed class EspnSettings
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class EspnStatus
{
    [JsonPropertyName("currentMatchupPeriod")] public int CurrentMatchupPeriod { get; set; }
    [JsonPropertyName("latestScoringPeriod")] public int LatestScoringPeriod { get; set; }
    [JsonPropertyName("firstScoringPeriod")] public int FirstScoringPeriod { get; set; } = 1;
    [JsonPropertyName("finalScoringPeriod")] public int FinalScoringPeriod { get; set; } = 17;
    [JsonPropertyName("isActive")] public bool IsActive { get; set; } = true;
}

public sealed class EspnTeam
{
    [JsonPropertyName("id")] public int Id { get; set; }
    /// <summary>Newer API: full team name. Older responses use location + nickname instead.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("nickname")] public string? Nickname { get; set; }
    [JsonPropertyName("abbrev")] public string? Abbrev { get; set; }
    [JsonPropertyName("owners")] public List<string>? Owners { get; set; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name :
        string.Join(' ', new[] { Location, Nickname }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } ln ? ln :
        $"Team {Id}";
}

public sealed class EspnMatchup
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("matchupPeriodId")] public int MatchupPeriodId { get; set; }
    [JsonPropertyName("playoffTierType")] public string? PlayoffTierType { get; set; } = "NONE";
    [JsonPropertyName("winner")] public string? Winner { get; set; } = "UNDECIDED";
    [JsonPropertyName("home")] public EspnMatchupSide? Home { get; set; }
    /// <summary>Null for bye weeks.</summary>
    [JsonPropertyName("away")] public EspnMatchupSide? Away { get; set; }
}

public sealed class EspnMatchupSide
{
    [JsonPropertyName("teamId")] public int TeamId { get; set; }
    /// <summary>Final/settled points for the matchup period.</summary>
    [JsonPropertyName("totalPoints")] public double TotalPoints { get; set; }
    /// <summary>Live running total while games are in progress (absent otherwise).</summary>
    [JsonPropertyName("totalPointsLive")] public double? TotalPointsLive { get; set; }
    [JsonPropertyName("rosterForCurrentScoringPeriod")] public EspnRoster? RosterForCurrentScoringPeriod { get; set; }
}

public sealed class EspnRoster
{
    [JsonPropertyName("appliedStatTotal")] public double? AppliedStatTotal { get; set; }
    [JsonPropertyName("entries")] public List<EspnRosterEntry> Entries { get; set; } = new();
}

public sealed class EspnRosterEntry
{
    [JsonPropertyName("playerId")] public long PlayerId { get; set; }
    [JsonPropertyName("lineupSlotId")] public int LineupSlotId { get; set; }
    [JsonPropertyName("playerPoolEntry")] public EspnPlayerPoolEntry? PlayerPoolEntry { get; set; }
}

public sealed class EspnPlayerPoolEntry
{
    [JsonPropertyName("id")] public long Id { get; set; }
    /// <summary>Applied fantasy points for the current scoring period.</summary>
    [JsonPropertyName("appliedStatTotal")] public double AppliedStatTotal { get; set; }
    [JsonPropertyName("player")] public EspnPlayer? Player { get; set; }
}

public sealed class EspnPlayer
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("fullName")] public string? FullName { get; set; }
    [JsonPropertyName("firstName")] public string? FirstName { get; set; }
    [JsonPropertyName("lastName")] public string? LastName { get; set; }
    [JsonPropertyName("defaultPositionId")] public int DefaultPositionId { get; set; }
    [JsonPropertyName("proTeamId")] public int ProTeamId { get; set; }
    [JsonPropertyName("injuryStatus")] public string? InjuryStatus { get; set; }
    [JsonPropertyName("stats")] public List<EspnPlayerStats> Stats { get; set; } = new();
}

/// <summary>
/// One stat line. Live/actual stats have statSourceId 0; projections have statSourceId 1.
/// statSplitTypeId 1 = single scoring period, 0 = season total.
/// </summary>
public sealed class EspnPlayerStats
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("seasonId")] public int SeasonId { get; set; }
    [JsonPropertyName("scoringPeriodId")] public int ScoringPeriodId { get; set; }
    [JsonPropertyName("statSourceId")] public int StatSourceId { get; set; }
    [JsonPropertyName("statSplitTypeId")] public int StatSplitTypeId { get; set; }
    [JsonPropertyName("appliedTotal")] public double AppliedTotal { get; set; }
    /// <summary>Raw stat counters keyed by ESPN stat id (as a string), e.g. "43": 1 for one receiving TD.</summary>
    [JsonPropertyName("stats")] public Dictionary<string, double> Stats { get; set; } = new();
    [JsonPropertyName("appliedStats")] public Dictionary<string, double>? AppliedStats { get; set; }

    public bool IsActual => StatSourceId == 0;
    public bool IsProjection => StatSourceId == 1;
}
