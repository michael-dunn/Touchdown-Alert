using System.Globalization;
using System.Xml.Linq;

namespace TouchdownAlert.Core.Yahoo;

/// <summary>Parsed <c>league/{key}</c> response.</summary>
public sealed record YahooLeagueInfo(
    string LeagueKey,
    string LeagueId,
    string Name,
    int Season,
    int CurrentWeek,
    int StartWeek,
    int EndWeek,
    int NumTeams,
    bool IsFinished);

/// <summary>One row of <c>league/{key}/settings/stat_categories/stats</c>.</summary>
public sealed record YahooStatCategory(int StatId, string Name, string DisplayName, string PositionType, bool Enabled);

/// <summary>One team's line in a scoreboard matchup.</summary>
public sealed record YahooScoreboardTeam(string TeamKey, int TeamId, string Name, double Points, double ProjectedPoints);

/// <summary>One matchup from <c>league/{key}/scoreboard;week=N</c> - always exactly two teams.</summary>
public sealed record YahooScoreboardMatchup(YahooScoreboardTeam Team1, YahooScoreboardTeam Team2);

/// <summary>One rostered player from <c>team/{key}/roster;week=N/players/stats;...</c>.</summary>
public sealed record YahooRosterPlayer(
    long PlayerId,
    string PlayerKey,
    string FullName,
    string DisplayPosition,
    string EditorialTeamAbbr,
    string SelectedPosition,
    double Points,
    IReadOnlyDictionary<int, double> Stats);

/// <summary>
/// Pure XML parsing for the shapes of Yahoo Fantasy Sports XML responses this app consumes. Every lookup
/// matches on <see cref="XName.LocalName"/> so the response's <c>base.rng</c> namespace never has to be
/// declared by callers (including test fixtures, which can omit it entirely).
/// </summary>
public static class YahooXmlParser
{
    private static readonly HashSet<string> NonStarterSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "BN", "IR", "IR+", "IR-R", "IL",
    };

    /// <summary>True for every <c>selected_position/position</c> value other than bench/IR-family slots.</summary>
    public static bool IsStarterSlot(string? selectedPosition) =>
        !string.IsNullOrWhiteSpace(selectedPosition) && !NonStarterSlots.Contains(selectedPosition);

    public static YahooLeagueInfo ParseLeague(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var league = FindDescendant(document.Root, "league")
            ?? throw new YahooApiException("Yahoo league response had no <league> element.");

        return new YahooLeagueInfo(
            LeagueKey: Text(league, "league_key") ?? "",
            LeagueId: Text(league, "league_id") ?? "",
            Name: Text(league, "name") ?? "",
            Season: IntOrZero(Text(league, "season")),
            CurrentWeek: IntOrZero(Text(league, "current_week")),
            StartWeek: IntOrZero(Text(league, "start_week")),
            EndWeek: IntOrZero(Text(league, "end_week")),
            NumTeams: IntOrZero(Text(league, "num_teams")),
            IsFinished: IntOrZero(Text(league, "is_finished")) != 0);
    }

    public static IReadOnlyList<YahooStatCategory> ParseStatCategories(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<YahooStatCategory>();

        var statsContainer = FindDescendant(document.Root, "stat_categories");
        if (statsContainer is null)
        {
            return result;
        }

        foreach (var stat in Descendants(statsContainer, "stat"))
        {
            // Skip nested <stat_position_types> etc. by requiring a stat_id child directly on this element.
            var statIdText = Text(stat, "stat_id");
            if (statIdText is null)
            {
                continue;
            }

            result.Add(new YahooStatCategory(
                StatId: IntOrZero(statIdText),
                Name: Text(stat, "name") ?? "",
                DisplayName: Text(stat, "display_name") ?? "",
                PositionType: Text(stat, "position_type") ?? "",
                Enabled: (Text(stat, "enabled") ?? "1") != "0"));
        }

        return result;
    }

    public static IReadOnlyList<YahooScoreboardMatchup> ParseScoreboard(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<YahooScoreboardMatchup>();

        var matchupsContainer = FindDescendant(document.Root, "matchups");
        if (matchupsContainer is null)
        {
            return result;
        }

        foreach (var matchup in Elements(matchupsContainer, "matchup"))
        {
            var teamsContainer = FindDescendant(matchup, "teams");
            if (teamsContainer is null)
            {
                continue;
            }

            var teams = Elements(teamsContainer, "team").Select(ParseScoreboardTeam).ToList();
            if (teams.Count == 2)
            {
                result.Add(new YahooScoreboardMatchup(teams[0], teams[1]));
            }
        }

        return result;
    }

    public static IReadOnlyList<YahooRosterPlayer> ParseRoster(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<YahooRosterPlayer>();

        var playersContainer = FindDescendant(document.Root, "players");
        if (playersContainer is null)
        {
            return result;
        }

        foreach (var player in Elements(playersContainer, "player"))
        {
            result.Add(ParseRosterPlayer(player));
        }

        return result;
    }

    private static YahooScoreboardTeam ParseScoreboardTeam(XElement team)
    {
        var pointsEl = Child(team, "team_points");
        var projectedEl = Child(team, "team_projected_points");

        return new YahooScoreboardTeam(
            TeamKey: Text(team, "team_key") ?? "",
            TeamId: IntOrZero(Text(team, "team_id")),
            Name: Text(team, "name") ?? "",
            Points: DoubleOrZero(pointsEl is null ? null : Text(pointsEl, "total")),
            ProjectedPoints: DoubleOrZero(projectedEl is null ? null : Text(projectedEl, "total")));
    }

    private static YahooRosterPlayer ParseRosterPlayer(XElement player)
    {
        var nameEl = Child(player, "name");
        var fullName = nameEl is null ? null : Text(nameEl, "full");

        var selectedPositionEl = Child(player, "selected_position");
        var selectedPosition = selectedPositionEl is null ? null : Text(selectedPositionEl, "position");

        var pointsEl = Child(player, "player_points");
        var points = DoubleOrZero(pointsEl is null ? null : Text(pointsEl, "total"));

        var stats = new Dictionary<int, double>();
        var statsContainer = FindDescendant(player, "player_stats");
        var statsList = statsContainer is null ? null : FindDescendant(statsContainer, "stats");
        if (statsList is not null)
        {
            foreach (var stat in Elements(statsList, "stat"))
            {
                var statId = Text(stat, "stat_id");
                if (statId is null)
                {
                    continue;
                }

                stats[IntOrZero(statId)] = DoubleOrZero(Text(stat, "value"));
            }
        }

        return new YahooRosterPlayer(
            PlayerId: LongOrZero(Text(player, "player_id")),
            PlayerKey: Text(player, "player_key") ?? "",
            FullName: fullName ?? "",
            DisplayPosition: Text(player, "display_position") ?? "",
            EditorialTeamAbbr: Text(player, "editorial_team_abbr") ?? "",
            SelectedPosition: selectedPosition ?? "",
            Points: points,
            Stats: stats);
    }

    // ---- LocalName-based XML helpers (namespace-agnostic) ----

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Elements(XElement parent, string localName) =>
        parent.Elements().Where(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Descendants(XElement parent, string localName) =>
        parent.Descendants().Where(e => e.Name.LocalName == localName);

    /// <summary>Finds the first descendant-or-self element with the given local name, anywhere under (or at) root.</summary>
    private static XElement? FindDescendant(XElement? root, string localName)
    {
        if (root is null)
        {
            return null;
        }

        if (root.Name.LocalName == localName)
        {
            return root;
        }

        return root.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
    }

    private static string? Text(XElement element, string childLocalName) => Child(element, childLocalName)?.Value.Trim();

    private static int IntOrZero(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long LongOrZero(string? s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double DoubleOrZero(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
