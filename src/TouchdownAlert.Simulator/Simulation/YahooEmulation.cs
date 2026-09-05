using System.Xml.Linq;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Simulator.Simulation;

/// <summary>
/// Builds Yahoo Fantasy Sports-shaped XML responses over the SAME <see cref="SimulatedLeague"/> state the
/// ESPN emulation uses, so a two-source (ESPN + Yahoo) integration test can run end to end against one
/// simulator instance. See <see cref="YahooXmlEndpoints"/> in Program.cs for the routes that call these.
/// </summary>
internal static class YahooEmulation
{
    private const string Ns = "http://fantasysports.yahooapis.com/fantasy/v2/base.rng";

    private static XElement E(string name, object? content) => new(XName.Get(name, Ns), content ?? "");

    public static XDocument BuildLeague(SimulatedLeague league, string leagueKey)
    {
        var root = new XElement(XName.Get("fantasy_content", Ns),
            E("league",
                new XElement[]
                {
                    E("league_key", leagueKey),
                    E("league_id", ExtractLeagueId(leagueKey)),
                    E("name", SimulatedLeague.LeagueName),
                    E("season", SimulatedLeague.SeasonId),
                    E("current_week", league.Week),
                    E("start_week", 1),
                    E("end_week", 17),
                    E("num_teams", league.GetTeamSummaries().Count),
                    E("is_finished", 0),
                }));

        return new XDocument(root);
    }

    public static XDocument BuildSettings(string leagueKey)
    {
        var stat = new Func<int, string, string, string, XElement>((id, name, displayName, positionType) =>
            E("stat", new XElement[]
            {
                E("stat_id", id),
                E("name", name),
                E("display_name", displayName),
                E("position_type", positionType),
                E("enabled", 1),
            }));

        var stats = E("stats", new XElement[]
        {
            stat(5, "Passing Touchdowns", "Pass TD", "O"),
            stat(10, "Rushing Touchdowns", "Rush TD", "O"),
            stat(13, "Reception Touchdowns", "Rec TD", "O"),
            stat(35, "Touchdown", "TD", "DT"),
            stat(49, "Kickoff and Punt Return Touchdowns", "Ret TD", "DT"),
        });

        var root = new XElement(XName.Get("fantasy_content", Ns),
            E("league", new XElement[]
            {
                E("league_key", leagueKey),
                E("settings", E("stat_categories", stats)),
            }));

        return new XDocument(root);
    }

    public static XDocument BuildScoreboard(SimulatedLeague league, string leagueKey, int week)
    {
        var summaries = league.GetTeamSummaries().ToDictionary(t => t.Id);
        var matchupElements = new List<XElement>();

        foreach (var matchup in league.GetMatchups())
        {
            var home = summaries[matchup.HomeTeamId];
            var away = summaries[matchup.AwayTeamId];

            matchupElements.Add(E("matchup",
                E("teams", new XElement[]
                {
                    BuildScoreboardTeam(leagueKey, home),
                    BuildScoreboardTeam(leagueKey, away),
                })));
        }

        var root = new XElement(XName.Get("fantasy_content", Ns),
            E("league", new XElement[]
            {
                E("league_key", leagueKey),
                E("scoreboard", E("matchups", matchupElements)),
            }));

        return new XDocument(root);
    }

    private static XElement BuildScoreboardTeam(string leagueKey, SimTeamSummary team) =>
        E("team", new XElement[]
        {
            E("team_key", $"{leagueKey}.t.{team.Id}"),
            E("team_id", team.Id),
            E("name", team.Name),
            E("team_points", E("total", team.Points)),
            E("team_projected_points", E("total", team.Points)),
        });

    public static XDocument BuildRoster(SimulatedLeague league, string leagueKey, int teamId)
    {
        var summary = league.GetTeamSummaries().FirstOrDefault(t => t.Id == teamId)
            ?? throw new KeyNotFoundException($"No team {teamId} in the simulated league.");

        var allPlayers = summary.Starters.Concat(summary.Bench);
        var playerElements = allPlayers.Select(p => BuildPlayer(leagueKey, p)).ToList();

        var root = new XElement(XName.Get("fantasy_content", Ns),
            E("team", new XElement[]
            {
                E("team_key", $"{leagueKey}.t.{teamId}"),
                E("roster", E("players", playerElements)),
            }));

        return new XDocument(root);
    }

    private static XElement BuildPlayer(string leagueKey, SimPlayerSummary player)
    {
        var selectedPosition = YahooSelectedPosition(player.Slot, player.LineupSlotId);
        var displayPosition = player.Position == "D/ST" ? "DEF" : player.Position;
        var yahooStats = MapEspnStatsToYahoo(player.Position, player.Stats);

        return E("player", new XElement[]
        {
            E("player_key", $"{leagueKey}.p.{player.Id}"),
            E("player_id", player.Id),
            E("name", E("full", player.Name)),
            E("display_position", displayPosition),
            E("editorial_team_abbr", ""),
            E("selected_position", E("position", selectedPosition)),
            E("player_points", E("total", player.Points)),
            E("player_stats", E("stats", yahooStats.Select(kv => E("stat", new XElement[]
            {
                E("stat_id", kv.Key),
                E("value", kv.Value),
            })))),
        });
    }

    /// <summary>ESPN lineup slot name -> Yahoo selected-position string: "BN" for bench, "DEF" for D/ST,
    /// "W/R/T" for FLEX, otherwise the position name.</summary>
    private static string YahooSelectedPosition(string espnSlotName, int lineupSlotId) => espnSlotName switch
    {
        "BE" => "BN",
        "IR" => "IR",
        "FLEX" => "W/R/T",
        "D/ST" => "DEF",
        _ => espnSlotName,
    };

    /// <summary>
    /// Translates the simulator's ESPN-style stat counters (keyed by ESPN stat id) to Yahoo stat ids:
    /// passing to 5, rushing to 10, receiving to 13, kick/punt return to 15 (offense) or 49 (D/ST), and
    /// fumble/interception/blocked-kick return to 15 (offense) or 35 (D/ST).
    /// </summary>
    private static Dictionary<int, double> MapEspnStatsToYahoo(string position, IReadOnlyDictionary<string, double> espnStats)
    {
        var isDefense = position == "D/ST";
        var result = new Dictionary<int, double>();

        void Add(int yahooStatId, double value)
        {
            if (value == 0)
            {
                return;
            }

            result[yahooStatId] = result.GetValueOrDefault(yahooStatId) + value;
        }

        foreach (var (statIdText, value) in espnStats)
        {
            if (!int.TryParse(statIdText, out var espnStatId))
            {
                continue;
            }

            if (espnStatId == EspnStatIds.PassingTd)
            {
                Add(5, value);
            }
            else if (espnStatId == EspnStatIds.RushingTd)
            {
                Add(10, value);
            }
            else if (espnStatId == EspnStatIds.ReceivingTd)
            {
                Add(13, value);
            }
            else if (espnStatId is EspnStatIds.KickReturnTd or EspnStatIds.PuntReturnTd)
            {
                Add(isDefense ? 49 : 15, value);
            }
            else if (espnStatId is EspnStatIds.FumbleReturnTd or EspnStatIds.InterceptionReturnTd or EspnStatIds.BlockedKickReturnTd)
            {
                Add(isDefense ? 35 : 15, value);
            }
        }

        return result;
    }

    private static string ExtractLeagueId(string leagueKey)
    {
        var idx = leagueKey.LastIndexOf(".l.", StringComparison.Ordinal);
        return idx >= 0 ? leagueKey[(idx + 3)..] : leagueKey;
    }
}
