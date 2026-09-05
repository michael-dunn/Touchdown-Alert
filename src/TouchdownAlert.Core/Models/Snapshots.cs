namespace TouchdownAlert.Core.Models;

/// <summary>Cumulative touchdown counters for one player in the current scoring period.</summary>
public sealed record TouchdownCounts(
    int Passing = 0,
    int Rushing = 0,
    int Receiving = 0,
    int KickReturn = 0,
    int PuntReturn = 0,
    int FumbleReturn = 0,
    int InterceptionReturn = 0,
    int BlockedKickReturn = 0,
    int Return = 0,
    int Defensive = 0)
{
    public static readonly TouchdownCounts Zero = new();

    public int Total => Passing + Rushing + Receiving + KickReturn + PuntReturn + FumbleReturn + InterceptionReturn + BlockedKickReturn + Return + Defensive;

    public int Get(TouchdownType type) => type switch
    {
        TouchdownType.Passing => Passing,
        TouchdownType.Rushing => Rushing,
        TouchdownType.Receiving => Receiving,
        TouchdownType.KickReturn => KickReturn,
        TouchdownType.PuntReturn => PuntReturn,
        TouchdownType.FumbleReturn => FumbleReturn,
        TouchdownType.InterceptionReturn => InterceptionReturn,
        TouchdownType.BlockedKickReturn => BlockedKickReturn,
        TouchdownType.Return => Return,
        TouchdownType.Defensive => Defensive,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public TouchdownCounts With(TouchdownType type, int value) => type switch
    {
        TouchdownType.Passing => this with { Passing = value },
        TouchdownType.Rushing => this with { Rushing = value },
        TouchdownType.Receiving => this with { Receiving = value },
        TouchdownType.KickReturn => this with { KickReturn = value },
        TouchdownType.PuntReturn => this with { PuntReturn = value },
        TouchdownType.FumbleReturn => this with { FumbleReturn = value },
        TouchdownType.InterceptionReturn => this with { InterceptionReturn = value },
        TouchdownType.BlockedKickReturn => this with { BlockedKickReturn = value },
        TouchdownType.Return => this with { Return = value },
        TouchdownType.Defensive => this with { Defensive = value },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Builds counts from an ESPN "stats" dictionary keyed by stat id (as string) with double values.
    /// ESPN reports return/defensive touchdowns via their specific stat ids, so <see cref="Return"/> and
    /// <see cref="Defensive"/> (Yahoo-only, generic categories) are always left at zero here.</summary>
    public static TouchdownCounts FromEspnStats(IReadOnlyDictionary<string, double> stats)
    {
        int Read(int statId) => stats.TryGetValue(statId.ToString(), out var v) ? (int)Math.Round(v) : 0;
        return new TouchdownCounts(
            Passing: Read(EspnStatIds.PassingTd),
            Rushing: Read(EspnStatIds.RushingTd),
            Receiving: Read(EspnStatIds.ReceivingTd),
            KickReturn: Read(EspnStatIds.KickReturnTd),
            PuntReturn: Read(EspnStatIds.PuntReturnTd),
            FumbleReturn: Read(EspnStatIds.FumbleReturnTd),
            InterceptionReturn: Read(EspnStatIds.InterceptionReturnTd),
            BlockedKickReturn: Read(EspnStatIds.BlockedKickReturnTd));
    }
}

/// <summary>One rostered player on a fantasy team for the current scoring period.</summary>
public sealed record RosteredPlayer(
    long PlayerId,
    string FullName,
    string Position,        // "QB", "RB", "WR", "TE", "K", "D/ST"
    int LineupSlotId,
    string LineupSlot,      // "QB", "RB", "FLEX", "BE", ...
    int ProTeamId,
    double Points,          // applied fantasy points this scoring period (live)
    TouchdownCounts Touchdowns)
{
    public bool IsStarter => EspnLineupSlots.IsStarter(LineupSlotId);
}

/// <summary>One fantasy team as of the latest poll.</summary>
public sealed record TeamSnapshot(
    int TeamId,
    string Name,
    string Abbreviation,
    double Points,
    IReadOnlyList<RosteredPlayer> Roster)
{
    public IEnumerable<RosteredPlayer> Starters => Roster.Where(p => p.IsStarter);
}

public sealed record MatchupSnapshot(int HomeTeamId, double HomePoints, int AwayTeamId, double AwayPoints);

/// <summary>Everything the app needs from one poll of one league.</summary>
public sealed record LeagueSnapshot(
    LeagueRef League,
    string LeagueName,
    int SeasonId,
    int ScoringPeriodId,
    int MatchupPeriodId,
    DateTimeOffset FetchedAt,
    IReadOnlyList<TeamSnapshot> Teams,
    IReadOnlyList<MatchupSnapshot> Matchups)
{
    public TeamSnapshot? FindTeam(int teamId) => Teams.FirstOrDefault(t => t.TeamId == teamId);
}
