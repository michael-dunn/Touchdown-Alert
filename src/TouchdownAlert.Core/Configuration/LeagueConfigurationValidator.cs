using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Configuration;

/// <summary>
/// Validates cross-cutting invariants between <see cref="LeaguesOptions"/> and <see cref="AlertOptions"/> that
/// can't be expressed as a per-class DataAnnotation: unique non-empty league keys, resolvable watched-team
/// leagues, and provider support. Also resolves each watched team's blank <see cref="WatchedTeamOptions.League"/>
/// to the single configured league when there is exactly one.
/// </summary>
public static class LeagueConfigurationValidator
{
    /// <summary>
    /// Validates <paramref name="leagues"/> and <paramref name="watchedTeams"/>, throwing on the first problem
    /// found. Mutates each <see cref="WatchedTeamOptions.League"/> in place, filling in the default league key
    /// when it was null/blank and exactly one league is configured.
    /// </summary>
    /// <param name="yahooOptions">
    /// Required (non-null, with non-blank ClientId/ClientSecret) when any league uses the Yahoo provider.
    /// Being logged in is deliberately NOT checked here - that's a per-poll concern
    /// (<see cref="Yahoo.YahooAuthException"/>), not a startup failure.
    /// </param>
    public static void ValidateAndResolve(
        IReadOnlyList<LeagueOptions> leagues, IReadOnlyList<WatchedTeamOptions> watchedTeams, YahooOptions? yahooOptions = null)
    {
        ArgumentNullException.ThrowIfNull(leagues);
        ArgumentNullException.ThrowIfNull(watchedTeams);

        if (leagues.Count == 0)
        {
            throw new InvalidOperationException("At least one league must be configured under \"Leagues\".");
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var league in leagues)
        {
            if (string.IsNullOrWhiteSpace(league.Key))
            {
                throw new InvalidOperationException("Every entry under \"Leagues\" must have a non-empty \"Key\".");
            }

            if (!seenKeys.Add(league.Key))
            {
                throw new InvalidOperationException($"Duplicate league key \"{league.Key}\" configured under \"Leagues\". Keys must be unique.");
            }

            if (league.Provider == LeagueProvider.Yahoo)
            {
                if (string.IsNullOrWhiteSpace(yahooOptions?.ClientId) || string.IsNullOrWhiteSpace(yahooOptions?.ClientSecret))
                {
                    throw new InvalidOperationException(
                        $"League \"{league.Key}\" uses the Yahoo provider but \"Yahoo:ClientId\"/\"Yahoo:ClientSecret\" are not configured. " +
                        "Create a Yahoo developer app and put them in appsettings.Local.json.");
                }
            }
        }

        foreach (var watched in watchedTeams)
        {
            if (string.IsNullOrWhiteSpace(watched.League))
            {
                if (leagues.Count == 1)
                {
                    watched.League = leagues[0].Key;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Watched team {watched.TeamId} does not specify a \"League\" and multiple leagues are configured; " +
                    "set \"League\" to one of: " + string.Join(", ", leagues.Select(l => l.Key)));
            }

            if (!seenKeys.Contains(watched.League))
            {
                throw new InvalidOperationException(
                    $"Watched team {watched.TeamId} references unknown league \"{watched.League}\". Configured leagues: " +
                    string.Join(", ", leagues.Select(l => l.Key)));
            }
        }
    }
}
