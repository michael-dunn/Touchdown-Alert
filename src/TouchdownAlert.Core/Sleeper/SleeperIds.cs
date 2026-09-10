namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Converts Sleeper's string player ids to the <see cref="long"/> <c>RosteredPlayer.PlayerId</c> the detector
/// keys on. Numeric ids ("4881") parse directly. Team defenses use the NFL abbreviation ("DET"), which gets a
/// fixed negative id from a table of the 32 teams so it is stable across runs and never collides with a
/// real player. Anything else (legacy/relocated abbreviations, garbage) hashes deterministically below the table.
/// </summary>
public static class SleeperIds
{
    /// <summary>The 32 NFL abbreviations as Sleeper spells them, alphabetical. Index i maps to id -(i + 1).</summary>
    public static readonly IReadOnlyList<string> NflTeams = new[]
    {
        "ARI", "ATL", "BAL", "BUF", "CAR", "CHI", "CIN", "CLE", "DAL", "DEN", "DET", "GB", "HOU", "IND", "JAX", "KC",
        "LAC", "LAR", "LV", "MIA", "MIN", "NE", "NO", "NYG", "NYJ", "PHI", "PIT", "SEA", "SF", "TB", "TEN", "WAS",
    };

    private static readonly Dictionary<string, long> TeamIds = NflTeams
        .Select((abbr, index) => (abbr, id: -(long)(index + 1)))
        .ToDictionary(t => t.abbr, t => t.id, StringComparer.Ordinal);

    /// <summary>Hashed fallback ids start here so they can never overlap the -1..-32 team table.</summary>
    private const long HashedIdFloor = -1000;

    /// <summary>True when the id is an NFL team abbreviation (2-3 upper-case ASCII letters), i.e. a DEF unit.</summary>
    public static bool IsTeamDefense(string sleeperId)
    {
        if (string.IsNullOrEmpty(sleeperId) || sleeperId.Length < 2 || sleeperId.Length > 3)
        {
            return false;
        }

        foreach (var c in sleeperId)
        {
            if (c < 'A' || c > 'Z')
            {
                return false;
            }
        }

        return true;
    }

    public static long ToPlayerId(string sleeperId)
    {
        ArgumentNullException.ThrowIfNull(sleeperId);

        if (long.TryParse(sleeperId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        if (TeamIds.TryGetValue(sleeperId, out var teamId))
        {
            return teamId;
        }

        return HashedIdFloor - StableHash(sleeperId);
    }

    /// <summary>
    /// FNV-1a over UTF-16 code units, masked to 31 bits. <see cref="string.GetHashCode()"/> is randomised per
    /// process, which would make ids differ between runs and break the detector's per-player state.
    /// </summary>
    private static long StableHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash & 0x7FFFFFFF;
    }
}
