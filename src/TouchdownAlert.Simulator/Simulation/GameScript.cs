using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Simulator.Simulation;

/// <summary>
/// Shared "what would realistically happen next" logic used by both the manual random-touchdown control
/// endpoint and <see cref="AutoPlayService"/>.
/// </summary>
internal static class GameScript
{
    private static readonly TouchdownType[] ReturnTypes =
    {
        TouchdownType.KickReturn, TouchdownType.PuntReturn, TouchdownType.FumbleReturn, TouchdownType.InterceptionReturn, TouchdownType.BlockedKickReturn,
    };

    /// <summary>Picks an appropriate TD type for the player's position, or null if that position can't score (K).</summary>
    public static TouchdownType? RandomTdTypeForPosition(string position, Random rng) => position switch
    {
        "QB" => rng.NextDouble() < 0.85 ? TouchdownType.Passing : TouchdownType.Rushing,
        "RB" => rng.NextDouble() < 0.55 ? TouchdownType.Rushing : TouchdownType.Receiving,
        "WR" or "TE" => TouchdownType.Receiving,
        "D/ST" => ReturnTypes[rng.Next(ReturnTypes.Length)],
        _ => null, // K, or anything else - kickers don't score TDs here
    };

    /// <summary>Weighted-random pick of a starter, favoring focus teams when given. Returns null if there are no starters.</summary>
    public static (int TeamId, SimPlayerSummary Player)? PickWeightedStarter(
        IReadOnlyList<(int TeamId, SimPlayerSummary Player)> starters, IReadOnlyList<int> focusTeamIds, Random rng)
    {
        if (starters.Count == 0)
        {
            return null;
        }

        if (focusTeamIds.Count == 0)
        {
            return starters[rng.Next(starters.Count)];
        }

        // Focus-team starters get 4x the weight of everyone else.
        var weighted = new List<(int TeamId, SimPlayerSummary Player)>();
        foreach (var s in starters)
        {
            var copies = focusTeamIds.Contains(s.TeamId) ? 4 : 1;
            for (var i = 0; i < copies; i++)
            {
                weighted.Add(s);
            }
        }

        return weighted[rng.Next(weighted.Count)];
    }
}
