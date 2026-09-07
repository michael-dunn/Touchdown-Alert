using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Wire-format DTOs for the Sleeper public API (https://api.sleeper.app, JSON, no auth). Only the fields we
/// read are modelled; unknown fields are ignored. Sleeper sends numbers as both <c>2</c> and <c>2.0</c>, and
/// ids/seasons as strings, so everything numeric that isn't structurally an int is a <see cref="double"/>.
/// Shared by the real source and the simulator so both sides agree on shape.
/// </summary>
public static class SleeperJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>GET /v1/state/nfl.</summary>
public sealed class SleeperNflState
{
    /// <summary>Current NFL week (Sleeper reports 1 during the preseason lead-in).</summary>
    [JsonPropertyName("week")] public int Week { get; set; }
    [JsonPropertyName("display_week")] public int DisplayWeek { get; set; }
    /// <summary>Season year as a string, e.g. "2026".</summary>
    [JsonPropertyName("season")] public string Season { get; set; } = "";
    [JsonPropertyName("season_type")] public string? SeasonType { get; set; }
    [JsonPropertyName("league_season")] public string? LeagueSeason { get; set; }
}

/// <summary>GET /v1/league/{league_id}.</summary>
public sealed class SleeperLeague
{
    [JsonPropertyName("league_id")] public string? LeagueId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Season year as a string, e.g. "2026".</summary>
    [JsonPropertyName("season")] public string? Season { get; set; }
    [JsonPropertyName("season_type")] public string? SeasonType { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("settings")] public SleeperLeagueSettings? Settings { get; set; }
    /// <summary>Slot list in lineup order, e.g. QB,RB,RB,WR,WR,TE,FLEX,FLEX,K,DEF,BN,... - starters arrays follow the non-BN entries.</summary>
    [JsonPropertyName("roster_positions")] public List<string> RosterPositions { get; set; } = new();
    [JsonPropertyName("scoring_settings")] public Dictionary<string, double>? ScoringSettings { get; set; }
}

public sealed class SleeperLeagueSettings
{
    [JsonPropertyName("num_teams")] public int NumTeams { get; set; }
    [JsonPropertyName("reserve_slots")] public int ReserveSlots { get; set; }
    [JsonPropertyName("start_week")] public int StartWeek { get; set; } = 1;
}

/// <summary>One element of GET /v1/league/{league_id}/users.</summary>
public sealed class SleeperUser
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    /// <summary>Null for users who never set anything; otherwise a bag of strings of which we read team_name.</summary>
    [JsonPropertyName("metadata")] public SleeperUserMetadata? Metadata { get; set; }
}

public sealed class SleeperUserMetadata
{
    [JsonPropertyName("team_name")] public string? TeamName { get; set; }
}

/// <summary>One element of GET /v1/league/{league_id}/rosters.</summary>
public sealed class SleeperRoster
{
    /// <summary>1..num_teams; this is the fantasy TeamId used everywhere else in TouchdownAlert.</summary>
    [JsonPropertyName("roster_id")] public int RosterId { get; set; }
    /// <summary>User id of the owner; null for orphaned/unassigned rosters.</summary>
    [JsonPropertyName("owner_id")] public string? OwnerId { get; set; }
    [JsonPropertyName("players")] public List<string>? Players { get; set; }
    /// <summary>Ordered to match the non-BN entries of <see cref="SleeperLeague.RosterPositions"/>; "0" is an empty slot.</summary>
    [JsonPropertyName("starters")] public List<string>? Starters { get; set; }
    [JsonPropertyName("reserve")] public List<string>? Reserve { get; set; }
    [JsonPropertyName("taxi")] public List<string>? Taxi { get; set; }
}

/// <summary>One element of GET /v1/league/{league_id}/matchups/{week}.</summary>
public sealed class SleeperMatchup
{
    [JsonPropertyName("roster_id")] public int RosterId { get; set; }
    /// <summary>Rosters sharing a matchup_id play each other; null means a bye.</summary>
    [JsonPropertyName("matchup_id")] public int? MatchupId { get; set; }
    [JsonPropertyName("points")] public double Points { get; set; }
    [JsonPropertyName("custom_points")] public double? CustomPoints { get; set; }
    [JsonPropertyName("starters")] public List<string>? Starters { get; set; }
    [JsonPropertyName("players")] public List<string>? Players { get; set; }
    /// <summary>Fantasy points per rostered player id for this week.</summary>
    [JsonPropertyName("players_points")] public Dictionary<string, double>? PlayersPoints { get; set; }
}

/// <summary>One value of the GET /v1/players/nfl dictionary (keyed by player id). Only the identity fields we keep.</summary>
public sealed class SleeperPlayer
{
    [JsonPropertyName("player_id")] public string? PlayerId { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    /// <summary>Absent for team defenses (first_name "Detroit", last_name "Lions").</summary>
    [JsonPropertyName("full_name")] public string? FullName { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("fantasy_positions")] public List<string>? FantasyPositions { get; set; }
    [JsonPropertyName("team")] public string? Team { get; set; }
    [JsonPropertyName("active")] public bool? Active { get; set; }

    /// <summary>full_name when present, else "first last" (how Sleeper names team defenses), else null.</summary>
    public string? ResolveFullName()
    {
        if (!string.IsNullOrWhiteSpace(FullName))
        {
            return FullName;
        }

        var joined = string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return joined.Length > 0 ? joined : null;
    }
}

/// <summary>
/// GET /v1/stats/nfl/regular/{season}/{week}: <c>{ "&lt;player_id&gt;": { "&lt;stat&gt;": number } }</c>. Parsed by
/// hand rather than bound straight to <c>Dictionary&lt;string, Dictionary&lt;string, double&gt;&gt;</c> so a single
/// odd value (null, string) never fails the whole poll, and so "TEAM_*" aggregate rows are dropped up front.
/// </summary>
public static class SleeperStatsParser
{
    public const string TeamAggregatePrefix = "TEAM_";

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Parse(JsonElement root)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var row in root.EnumerateObject())
        {
            if (row.Name.StartsWith(TeamAggregatePrefix, StringComparison.Ordinal) || row.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var stats = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var stat in row.Value.EnumerateObject())
            {
                if (stat.Value.ValueKind == JsonValueKind.Number && stat.Value.TryGetDouble(out var number))
                {
                    stats[stat.Name] = number;
                }
                else if (stat.Value.ValueKind == JsonValueKind.String
                         && double.TryParse(stat.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    stats[stat.Name] = parsed;
                }
            }

            result[row.Name] = stats;
        }

        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }
}
