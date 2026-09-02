namespace TouchdownAlert.Core.Models;

/// <summary>
/// The kinds of touchdown we detect. Each maps to one ESPN stat id (see <see cref="EspnStatIds"/>).
/// </summary>
public enum TouchdownType
{
    Passing,
    Rushing,
    Receiving,
    KickReturn,
    PuntReturn,
    FumbleReturn,
    InterceptionReturn,
    BlockedKickReturn,
}

/// <summary>
/// ESPN fantasy stat ids, verified against real 2025 season data on 2026-09-02.
/// Stat 94 is an aggregate defensive-TD count and is intentionally NOT used (it would double count 103/104).
/// </summary>
public static class EspnStatIds
{
    public const int PassingTd = 4;
    public const int RushingTd = 25;
    public const int ReceivingTd = 43;
    public const int BlockedKickReturnTd = 93;
    public const int KickReturnTd = 101;
    public const int PuntReturnTd = 102;
    public const int FumbleReturnTd = 103;
    public const int InterceptionReturnTd = 104;

    public static readonly IReadOnlyDictionary<int, TouchdownType> ByStatId = new Dictionary<int, TouchdownType>
    {
        [PassingTd] = TouchdownType.Passing,
        [RushingTd] = TouchdownType.Rushing,
        [ReceivingTd] = TouchdownType.Receiving,
        [BlockedKickReturnTd] = TouchdownType.BlockedKickReturn,
        [KickReturnTd] = TouchdownType.KickReturn,
        [PuntReturnTd] = TouchdownType.PuntReturn,
        [FumbleReturnTd] = TouchdownType.FumbleReturn,
        [InterceptionReturnTd] = TouchdownType.InterceptionReturn,
    };

    public static int ToStatId(this TouchdownType type) => type switch
    {
        TouchdownType.Passing => PassingTd,
        TouchdownType.Rushing => RushingTd,
        TouchdownType.Receiving => ReceivingTd,
        TouchdownType.BlockedKickReturn => BlockedKickReturnTd,
        TouchdownType.KickReturn => KickReturnTd,
        TouchdownType.PuntReturn => PuntReturnTd,
        TouchdownType.FumbleReturn => FumbleReturnTd,
        TouchdownType.InterceptionReturn => InterceptionReturnTd,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

/// <summary>ESPN lineup slot ids for this league's roster settings.</summary>
public static class EspnLineupSlots
{
    public const int Qb = 0;
    public const int Rb = 2;
    public const int Wr = 4;
    public const int Te = 6;
    public const int Dst = 16;
    public const int K = 17;
    public const int Bench = 20;
    public const int Ir = 21;
    public const int Flex = 23;

    /// <summary>A player is a starter when they are in any slot other than Bench or IR.</summary>
    public static bool IsStarter(int lineupSlotId) => lineupSlotId != Bench && lineupSlotId != Ir;

    public static string Name(int lineupSlotId) => lineupSlotId switch
    {
        Qb => "QB",
        Rb => "RB",
        Wr => "WR",
        Te => "TE",
        Dst => "D/ST",
        K => "K",
        Bench => "BE",
        Ir => "IR",
        Flex => "FLEX",
        _ => $"SLOT{lineupSlotId}",
    };
}

/// <summary>ESPN defaultPositionId values.</summary>
public static class EspnPositions
{
    public static string Name(int defaultPositionId) => defaultPositionId switch
    {
        1 => "QB",
        2 => "RB",
        3 => "WR",
        4 => "TE",
        5 => "K",
        16 => "D/ST",
        _ => $"POS{defaultPositionId}",
    };
}
