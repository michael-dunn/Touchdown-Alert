using System.Text.Json.Nodes;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Simulator.Simulation;

/// <summary>
/// Builds Sleeper-shaped JSON (https://api.sleeper.app/v1/...) over the SAME <see cref="SimulatedLeague"/> state the
/// ESPN and Yahoo emulations use, so a multi-source integration test can run end to end against one simulator.
/// Everything is built as <see cref="JsonNode"/> graphs rather than typed DTOs: the Core Sleeper wire types are being
/// written in parallel and the simulator must not depend on them - the emulation is the *contract* the parser is
/// tested against, so field names here are the real snake_case Sleeper names (see fixtures/sleeper/*.json).
/// </summary>
/// <remarks>
/// Id conventions (matching real Sleeper):
/// <list type="bullet">
/// <item>Offensive players and kickers: the simulator's ESPN numeric id as a string ("3918298").</item>
/// <item>Team defenses: the NFL team abbreviation ("BAL"), position "DEF", first_name = city, last_name = nickname.
/// The sim's ESPN proTeamId is mapped through <see cref="NflTeams"/>; if two simulated D/STs ever shared a pro team
/// the first one encountered wins (dictionaries are built with TryAdd) so output stays deterministic.</item>
/// </list>
/// The simulator models a single scoring period, so the {week}/{season} route parameters on matchups and stats are
/// accepted but ignored (same forgiving behaviour as the ESPN route's scoringPeriodId and the Yahoo scoreboard week).
/// </remarks>
internal static class SleeperEmulation
{
    /// <summary>Sleeper's league id is a string on the wire, even though the simulated league id is numeric.</summary>
    public static readonly string LeagueId = SimulatedLeague.LeagueId.ToString();

    /// <summary>Sleeper's season is a STRING ("2026"), unlike ESPN's numeric seasonId - the parser must cope with that.</summary>
    public static readonly string Season = SimulatedLeague.SeasonId.ToString();

    /// <summary>Roster id (== sim team id) whose user row carries <c>"metadata": null</c>, so the parser's fallback from
    /// metadata.team_name to display_name is exercised. Real Sleeper omits team_name for most users (see users.json).</summary>
    public const int TeamWithNullMetadata = 10;

    /// <summary>
    /// Decoy value emitted as <c>"td"</c> on every DEF stats row. On real Sleeper that key is the number of touchdowns
    /// the defense ALLOWED (it feeds points-allowed scoring, e.g. "BAL td:5"), so a correct parser must never count it
    /// as a defensive touchdown. Emitting a constant nonzero value here makes any such bug fail the e2e tests loudly.
    /// </summary>
    public const int DefTdDecoy = 3;

    /// <summary>ESPN proTeamId -> (Sleeper abbreviation, city, nickname) for the 32 NFL teams. ESPN skips 31 and 32.</summary>
    private static readonly IReadOnlyDictionary<int, (string Abbr, string City, string Nickname)> NflTeams =
        new Dictionary<int, (string, string, string)>
        {
            [1] = ("ATL", "Atlanta", "Falcons"),
            [2] = ("BUF", "Buffalo", "Bills"),
            [3] = ("CHI", "Chicago", "Bears"),
            [4] = ("CIN", "Cincinnati", "Bengals"),
            [5] = ("CLE", "Cleveland", "Browns"),
            [6] = ("DAL", "Dallas", "Cowboys"),
            [7] = ("DEN", "Denver", "Broncos"),
            [8] = ("DET", "Detroit", "Lions"),
            [9] = ("GB", "Green Bay", "Packers"),
            [10] = ("TEN", "Tennessee", "Titans"),
            [11] = ("IND", "Indianapolis", "Colts"),
            [12] = ("KC", "Kansas City", "Chiefs"),
            [13] = ("LV", "Las Vegas", "Raiders"),
            [14] = ("LAR", "Los Angeles", "Rams"),
            [15] = ("MIA", "Miami", "Dolphins"),
            [16] = ("MIN", "Minnesota", "Vikings"),
            [17] = ("NE", "New England", "Patriots"),
            [18] = ("NO", "New Orleans", "Saints"),
            [19] = ("NYG", "New York", "Giants"),
            [20] = ("NYJ", "New York", "Jets"),
            [21] = ("PHI", "Philadelphia", "Eagles"),
            [22] = ("ARI", "Arizona", "Cardinals"),
            [23] = ("PIT", "Pittsburgh", "Steelers"),
            [24] = ("LAC", "Los Angeles", "Chargers"),
            [25] = ("SF", "San Francisco", "49ers"),
            [26] = ("SEA", "Seattle", "Seahawks"),
            [27] = ("TB", "Tampa Bay", "Buccaneers"),
            [28] = ("WAS", "Washington", "Commanders"),
            [29] = ("CAR", "Carolina", "Panthers"),
            [30] = ("JAX", "Jacksonville", "Jaguars"),
            [33] = ("BAL", "Baltimore", "Ravens"),
            [34] = ("HOU", "Houston", "Texans"),
        };

    // ---- Public builders, one per emulated route ----

    /// <summary>GET /v1/state/nfl</summary>
    public static JsonObject BuildState(SimulatedLeague league)
    {
        var week = league.Week;
        return new JsonObject
        {
            ["week"] = week,
            ["leg"] = week,
            ["display_week"] = week,
            ["season"] = Season,
            ["season_type"] = "regular",
            ["league_season"] = Season,
            ["previous_season"] = (SimulatedLeague.SeasonId - 1).ToString(),
            ["season_has_scores"] = true,
        };
    }

    /// <summary>GET /v1/league/{leagueId}</summary>
    public static JsonObject BuildLeague(SimulatedLeague league)
    {
        var teams = league.GetTeamSummaries();
        var rosterPositions = RosterPositions(teams);

        return new JsonObject
        {
            ["league_id"] = LeagueId,
            ["name"] = SimulatedLeague.LeagueName,
            ["season"] = Season,
            ["season_type"] = "regular",
            ["status"] = "in_season",
            ["sport"] = "nfl",
            ["total_rosters"] = teams.Count,
            ["settings"] = new JsonObject
            {
                ["num_teams"] = teams.Count,
                ["playoff_week_start"] = 15,
                ["start_week"] = 1,
                ["leg"] = league.Week,
                ["reserve_slots"] = 0,
            },
            ["roster_positions"] = ToJsonArray(rosterPositions),
            // Only the TD-related scoring keys: these are the ones a parser might use to discover which stat keys the
            // league scores, and they match the real league's values (fixtures/sleeper/league.json).
            ["scoring_settings"] = new JsonObject
            {
                ["pass_td"] = 4.0,
                ["rush_td"] = 6.0,
                ["rec_td"] = 6.0,
                ["def_td"] = 6.0,
                ["st_td"] = 6.0,
                ["def_st_td"] = 6.0,
                ["fum_rec_td"] = 6.0,
            },
        };
    }

    /// <summary>GET /v1/league/{leagueId}/users - one user per team. Team <see cref="TeamWithNullMetadata"/> has null metadata.</summary>
    public static JsonArray BuildUsers(SimulatedLeague league)
    {
        var users = new JsonArray();
        foreach (var team in league.GetTeamSummaries())
        {
            users.Add(new JsonObject
            {
                ["user_id"] = UserId(team.Id),
                ["display_name"] = DisplayName(team),
                ["avatar"] = null,
                ["is_bot"] = false,
                ["is_owner"] = team.Id == 1 ? true : null,
                ["league_id"] = LeagueId,
                ["metadata"] = team.Id == TeamWithNullMetadata
                    ? null
                    : new JsonObject { ["team_name"] = team.Name, ["allow_pn"] = "on" },
                ["settings"] = null,
            });
        }

        return users;
    }

    /// <summary>GET /v1/league/{leagueId}/rosters - one roster per team; roster_id is the sim team id.</summary>
    public static JsonArray BuildRosters(SimulatedLeague league)
    {
        var teams = league.GetTeamSummaries();
        RosterPositions(teams); // asserts every team has the same lineup shape

        var rosters = new JsonArray();
        foreach (var team in teams)
        {
            rosters.Add(new JsonObject
            {
                ["roster_id"] = team.Id,
                ["owner_id"] = UserId(team.Id),
                ["co_owners"] = null,
                ["league_id"] = LeagueId,
                ["players"] = ToJsonArray(AllPlayerIds(team)),
                ["starters"] = ToJsonArray(team.Starters.Select(PlayerId)),
                ["reserve"] = null,
                ["taxi"] = null,
                ["metadata"] = null,
                ["settings"] = new JsonObject
                {
                    ["wins"] = 0,
                    ["losses"] = 0,
                    ["ties"] = 0,
                    ["fpts"] = 0,
                    ["fpts_decimal"] = 0,
                    ["total_moves"] = 0,
                    ["waiver_position"] = team.Id,
                },
            });
        }

        return rosters;
    }

    /// <summary>GET /v1/league/{leagueId}/matchups/{week} - one entry per team; opponents share a matchup_id.</summary>
    public static JsonArray BuildMatchups(SimulatedLeague league)
    {
        var teams = league.GetTeamSummaries();
        var matchupIdByTeam = new Dictionary<int, int>();
        foreach (var matchup in league.GetMatchups())
        {
            matchupIdByTeam[matchup.HomeTeamId] = matchup.Id;
            matchupIdByTeam[matchup.AwayTeamId] = matchup.Id;
        }

        var result = new JsonArray();
        foreach (var team in teams)
        {
            var playersPoints = new JsonObject();
            foreach (var player in team.Starters.Concat(team.Bench))
            {
                // A player can be rostered twice on one team only through the deliberate dual-rostering scenario
                // (see SimPlayer docs); a dictionary key can't repeat, so last write wins with the same value.
                playersPoints[PlayerId(player)] = player.Points;
            }

            result.Add(new JsonObject
            {
                ["roster_id"] = team.Id,
                // Real Sleeper uses null for a bye week; every sim team has an opponent but keep the shape honest.
                ["matchup_id"] = matchupIdByTeam.TryGetValue(team.Id, out var matchupId) ? matchupId : null,
                ["points"] = team.Points,
                ["custom_points"] = null,
                ["starters"] = ToJsonArray(team.Starters.Select(PlayerId)),
                ["starters_points"] = new JsonArray(team.Starters.Select(p => (JsonNode?)JsonValue.Create(p.Points)).ToArray()),
                ["players"] = ToJsonArray(AllPlayerIds(team)),
                ["players_points"] = playersPoints,
            });
        }

        return result;
    }

    /// <summary>
    /// GET /v1/stats/nfl/regular/{season}/{week} - <c>{ "&lt;player_id&gt;": { stat: value } }</c>. Offensive players with
    /// nothing to report are omitted (the real API only lists players who recorded something); every DEF unit is
    /// always present because a defense always has a points-allowed line - which is where the <c>"td"</c> decoy lives.
    /// </summary>
    public static JsonObject BuildStats(SimulatedLeague league)
    {
        var stats = new JsonObject();
        foreach (var player in DistinctPlayers(league))
        {
            var row = TranslateStats(player);
            if (row is not null)
            {
                stats.TryAdd(PlayerId(player), row);
            }
        }

        return stats;
    }

    /// <summary>GET /v1/players/nfl - the (trimmed) player dictionary for every simulated player.</summary>
    public static JsonObject BuildPlayers(SimulatedLeague league)
    {
        var players = new JsonObject();
        foreach (var player in DistinctPlayers(league))
        {
            players.TryAdd(PlayerId(player), BuildPlayerEntry(player));
        }

        return players;
    }

    // ---- Stat translation ----

    /// <summary>
    /// Translates the simulator's ESPN stat counters (keyed by ESPN stat id as a string) into Sleeper stat keys.
    /// Returns null when an offensive player has nothing nonzero to report. Zero-valued keys are never emitted, as on
    /// the real API.
    /// </summary>
    /// <remarks>
    /// Individual players: 4 pass_td, 25 rush_td, 43 rec_td, 103 fum_rec_td, 104 idp_def_td; 101/102/93 are summed into
    /// st_td and ALSO emitted as kr_td/pr_td/blk_kick_ret_td (the parser prefers st_td and must ignore the detail keys
    /// when st_td is present, or it double counts). anytime_tds is a decoy aggregate the parser must ignore.
    /// Team defense: 103+104 -> def_td; 101+102+93 -> def_st_td, with misc_td duplicating the 93 component (ignored by
    /// the parser), plus the constant <see cref="DefTdDecoy"/> "td".
    /// </remarks>
    internal static JsonObject? TranslateStats(SimPlayerSummary player)
    {
        double Stat(int espnStatId) => player.Stats.GetValueOrDefault(espnStatId.ToString());

        var row = new JsonObject();
        var isDefense = player.Position == "D/ST";

        void Put(string key, double value)
        {
            if (value != 0)
            {
                row[key] = value;
            }
        }

        var kickReturn = Stat(EspnStatIds.KickReturnTd);
        var puntReturn = Stat(EspnStatIds.PuntReturnTd);
        var blockedKick = Stat(EspnStatIds.BlockedKickReturnTd);
        var fumbleReturn = Stat(EspnStatIds.FumbleReturnTd);
        var interceptionReturn = Stat(EspnStatIds.InterceptionReturnTd);

        if (isDefense)
        {
            Put("def_td", fumbleReturn + interceptionReturn);
            Put("def_st_td", kickReturn + puntReturn + blockedKick);
            Put("misc_td", blockedKick);
            // See DefTdDecoy: "td" on a DEF row is touchdowns ALLOWED on real Sleeper, never a scoring event.
            row["td"] = DefTdDecoy;
        }
        else
        {
            var rush = Stat(EspnStatIds.RushingTd);
            var rec = Stat(EspnStatIds.ReceivingTd);
            var specialTeams = kickReturn + puntReturn + blockedKick;

            Put("pass_td", Stat(EspnStatIds.PassingTd));
            Put("rush_td", rush);
            Put("rec_td", rec);
            Put("kr_td", kickReturn);
            Put("pr_td", puntReturn);
            Put("blk_kick_ret_td", blockedKick);
            Put("st_td", specialTeams);
            Put("fum_rec_td", fumbleReturn);
            Put("idp_def_td", interceptionReturn);
            Put("anytime_tds", rush + rec + specialTeams + fumbleReturn);

            if (row.Count == 0 && player.Points == 0)
            {
                return null;
            }
        }

        row["pts_std"] = player.Points;
        row["pts_half_ppr"] = player.Points;
        row["pts_ppr"] = player.Points;
        row["gp"] = 1;
        row["gms_active"] = 1;
        return row;
    }

    // ---- Id / naming helpers ----

    /// <summary>Sleeper player id for a simulated player: numeric id as a string, or the NFL abbreviation for a D/ST.</summary>
    public static string PlayerId(SimPlayerSummary player) => PlayerId(player.Id, player.Position, player.ProTeamId);

    public static string PlayerId(long id, string position, int proTeamId) =>
        position == "D/ST" ? TeamAbbreviation(proTeamId) : id.ToString();

    /// <summary>Abbreviation for an ESPN proTeamId; unknown ids (the sim's invented 100+ ids) fall back to "T{id}" so a
    /// D/ST with an unmapped pro team still gets a unique, stable id rather than throwing.</summary>
    private static string TeamAbbreviation(int proTeamId) =>
        NflTeams.TryGetValue(proTeamId, out var team) ? team.Abbr : $"T{proTeamId}";

    /// <summary>Fake Sleeper user id: 15 digits of padding followed by the team id, as a string.</summary>
    public static string UserId(int teamId) => "100000000000000" + teamId;

    /// <summary>A username-looking handle derived from the team name ("KCP - Kimmie Cocoa Pop" -> "KCPKimmieCocoaPop").</summary>
    private static string DisplayName(SimTeamSummary team)
    {
        var chars = team.Name.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length > 0 ? new string(chars) : team.Abbrev;
    }

    private static JsonObject BuildPlayerEntry(SimPlayerSummary player)
    {
        string firstName, lastName, position;
        string? team;

        if (player.Position == "D/ST")
        {
            position = "DEF";
            if (NflTeams.TryGetValue(player.ProTeamId, out var nfl))
            {
                (team, firstName, lastName) = nfl;
            }
            else
            {
                team = TeamAbbreviation(player.ProTeamId);
                // "Baltimore D/ST" -> "Baltimore"
                firstName = player.Name.Replace(" D/ST", string.Empty, StringComparison.Ordinal);
                lastName = "D/ST";
            }
        }
        else
        {
            position = player.Position;
            // The sim's invented proTeamIds (100+) aren't NFL teams; real Sleeper uses null for players without a team.
            team = NflTeams.TryGetValue(player.ProTeamId, out var nfl) ? nfl.Abbr : null;
            var space = player.Name.IndexOf(' ');
            firstName = space > 0 ? player.Name[..space] : player.Name;
            lastName = space > 0 ? player.Name[(space + 1)..] : string.Empty;
        }

        return new JsonObject
        {
            ["player_id"] = PlayerId(player),
            ["first_name"] = firstName,
            ["last_name"] = lastName,
            ["full_name"] = $"{firstName} {lastName}".Trim(),
            ["position"] = position,
            ["fantasy_positions"] = new JsonArray(position),
            ["team"] = team,
            ["active"] = true,
            ["sport"] = "nfl",
            ["status"] = "Active",
            ["injury_status"] = null,
        };
    }

    // ---- Lineup helpers ----

    /// <summary>
    /// Sleeper's roster_positions: the starting slots in lineup order, then one "BN" per bench slot. Derived from the
    /// teams' actual rosters (RosterBuilder fills every team identically: QB RB RB WR WR TE FLEX DEF K + 4 BN) and
    /// verified to be identical for every team, because Sleeper's starters arrays are positional against this list.
    /// </summary>
    internal static IReadOnlyList<string> RosterPositions(IReadOnlyList<SimTeamSummary> teams)
    {
        if (teams.Count == 0)
        {
            return Array.Empty<string>();
        }

        var expected = PositionsFor(teams[0]);
        foreach (var team in teams.Skip(1))
        {
            var actual = PositionsFor(team);
            if (!actual.SequenceEqual(expected))
            {
                throw new InvalidOperationException(
                    $"Team {team.Id} lineup [{string.Join(",", actual)}] differs from team {teams[0].Id} [{string.Join(",", expected)}]; " +
                    "Sleeper roster_positions must be identical for every roster.");
            }
        }

        return expected;
    }

    private static List<string> PositionsFor(SimTeamSummary team) =>
        team.Starters.Select(p => SlotName(p.Slot)).Concat(team.Bench.Select(_ => "BN")).ToList();

    /// <summary>ESPN lineup slot name -> Sleeper roster position ("D/ST" -> "DEF", "BE" -> "BN").</summary>
    private static string SlotName(string espnSlotName) => espnSlotName switch
    {
        "D/ST" => "DEF",
        "BE" => "BN",
        _ => espnSlotName,
    };

    private static IEnumerable<string> AllPlayerIds(SimTeamSummary team) =>
        team.Starters.Concat(team.Bench).Select(PlayerId);

    /// <summary>Every distinct simulated player (a dual-rostered player appears once), in stable id order.</summary>
    private static IEnumerable<SimPlayerSummary> DistinctPlayers(SimulatedLeague league) =>
        league.GetTeamSummaries()
            .SelectMany(t => t.Starters.Concat(t.Bench))
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.Id);

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
}
