using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Yahoo;

/// <summary>
/// Classifies a Yahoo stat category into a <see cref="TouchdownType"/> by name (Yahoo's stat ids are
/// per-league configurable, so name matching is the primary signal; callers fall back to the well-known
/// ids in <see cref="YahooStatIds"/> only when a league's settings are unavailable).
/// </summary>
public static class YahooStatMap
{
    /// <summary>
    /// Returns the touchdown type this stat category represents, or null if it isn't a touchdown stat at
    /// all (e.g. "2-Point Conversions"). Matching: case-insensitive "touchdown" in the name is required;
    /// then "passing" / "rushing" / "reception"|"receiving" / "return" (in that order) pick the specific
    /// type, and anything left over classifies as <see cref="TouchdownType.Defensive"/> only when
    /// <paramref name="positionType"/> is "DT" (defense/special teams).
    /// </summary>
    public static TouchdownType? Classify(string? name, string? positionType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var lower = name.ToLowerInvariant();
        if (!lower.Contains("touchdown"))
        {
            return null;
        }

        if (lower.Contains("passing"))
        {
            return TouchdownType.Passing;
        }

        if (lower.Contains("rushing"))
        {
            return TouchdownType.Rushing;
        }

        if (lower.Contains("reception") || lower.Contains("receiving"))
        {
            return TouchdownType.Receiving;
        }

        if (lower.Contains("return"))
        {
            return TouchdownType.Return;
        }

        if (string.Equals(positionType, "DT", StringComparison.OrdinalIgnoreCase))
        {
            return TouchdownType.Defensive;
        }

        return null;
    }
}

/// <summary>
/// Well-known Yahoo stat ids, used as a fallback when a league's <c>settings/stat_categories</c> response
/// isn't available yet (name matching via <see cref="YahooStatMap"/> is preferred and used whenever the
/// stat categories have been fetched).
/// </summary>
public static class YahooStatIds
{
    public const int PassingTd = 5;
    public const int RushingTd = 10;
    public const int ReceivingTd = 13;
    public const int ReturnTd = 15;
    public const int DefensiveTd = 35;
    public const int KickPuntReturnTd = 49;
    public const int TwoPointConversion = 16;

    public static readonly IReadOnlyDictionary<int, TouchdownType> ByStatId = new Dictionary<int, TouchdownType>
    {
        [PassingTd] = TouchdownType.Passing,
        [RushingTd] = TouchdownType.Rushing,
        [ReceivingTd] = TouchdownType.Receiving,
        [ReturnTd] = TouchdownType.Return,
        [DefensiveTd] = TouchdownType.Defensive,
        [KickPuntReturnTd] = TouchdownType.Return,
    };
}
