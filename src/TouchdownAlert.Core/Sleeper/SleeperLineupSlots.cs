using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Maps Sleeper <c>roster_positions</c> entries onto the ESPN lineup slot ids the rest of the app already
/// understands (<see cref="EspnLineupSlots"/>), so <c>RosteredPlayer.IsStarter</c> and slot labels keep working
/// without a provider switch. Display names reuse ESPN's ("D/ST", "BE") for consistency on the dashboard.
/// </summary>
public static class SleeperLineupSlots
{
    /// <summary>Sleeper's marker for an empty starter slot.</summary>
    public const string EmptySlot = "0";

    public const string Bench = "BN";
    public const string InjuredReserve = "IR";
    public const string TeamDefense = "DEF";

    /// <summary>Slot id and display name for one roster_positions entry. Unknown starting slots (SUPER_FLEX,
    /// REC_FLEX, IDP_FLEX, DL, LB, DB, ...) count as starters via the FLEX id and keep Sleeper's raw name.</summary>
    public static (int Id, string Name) Map(string rosterPosition)
    {
        ArgumentNullException.ThrowIfNull(rosterPosition);

        return rosterPosition switch
        {
            "QB" => (EspnLineupSlots.Qb, EspnLineupSlots.Name(EspnLineupSlots.Qb)),
            "RB" => (EspnLineupSlots.Rb, EspnLineupSlots.Name(EspnLineupSlots.Rb)),
            "WR" => (EspnLineupSlots.Wr, EspnLineupSlots.Name(EspnLineupSlots.Wr)),
            "TE" => (EspnLineupSlots.Te, EspnLineupSlots.Name(EspnLineupSlots.Te)),
            "FLEX" => (EspnLineupSlots.Flex, EspnLineupSlots.Name(EspnLineupSlots.Flex)),
            "K" => (EspnLineupSlots.K, EspnLineupSlots.Name(EspnLineupSlots.K)),
            TeamDefense => (EspnLineupSlots.Dst, EspnLineupSlots.Name(EspnLineupSlots.Dst)),
            Bench => (EspnLineupSlots.Bench, EspnLineupSlots.Name(EspnLineupSlots.Bench)),
            InjuredReserve => (EspnLineupSlots.Ir, EspnLineupSlots.Name(EspnLineupSlots.Ir)),
            _ => (EspnLineupSlots.Flex, rosterPosition),
        };
    }

    /// <summary>The starting slots of a league, in the order Sleeper's <c>starters</c> arrays follow (BN/IR removed).</summary>
    public static IReadOnlyList<string> StartingSlots(IEnumerable<string> rosterPositions)
    {
        ArgumentNullException.ThrowIfNull(rosterPositions);
        return rosterPositions.Where(p => p != Bench && p != InjuredReserve).ToList();
    }

    /// <summary>Sleeper's "DEF" position rendered the way ESPN players are ("D/ST"), so both providers look alike.</summary>
    public static string NormalizePosition(string? position) =>
        string.Equals(position, TeamDefense, StringComparison.OrdinalIgnoreCase) ? EspnPositions.Name(16) : position ?? "?";
}
