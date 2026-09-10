using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Turns one Sleeper weekly stat row into <see cref="TouchdownCounts"/>. The key set was verified against all
/// 18 weeks of 2025 data (docs/sleeper-plan.md). Sleeper publishes several overlapping aggregates
/// (<c>td</c>, <c>anytime_tds</c>, <c>st_td</c>, <c>misc_td</c>, <c>fum_rec_ez_tds</c>), so this map is deliberately
/// explicit about which one wins in each case to avoid double counting - and about which are never counted.
/// </summary>
public static class SleeperStatMap
{
    // Individual player keys.
    public const string PassTd = "pass_td";
    public const string RushTd = "rush_td";
    public const string RecTd = "rec_td";
    /// <summary>Aggregate of kick/punt/blocked-kick return TDs. Preferred over the parts when present.</summary>
    public const string SpecialTeamsTd = "st_td";
    public const string KickReturnTd = "kr_td";
    public const string PuntReturnTd = "pr_td";
    public const string BlockedKickReturnTd = "blk_kick_ret_td";
    public const string BlockedPuntReturnTd = "blk_pr_td";
    public const string FumbleRecoveryTd = "fum_rec_td";
    public const string IdpDefensiveTd = "idp_def_td";

    // Team-defense keys (row id is an NFL abbreviation).
    /// <summary>INT/fumble return TD by the defense. Also honoured on a player row if Sleeper ever sends it there.</summary>
    public const string DefensiveTd = "def_td";
    /// <summary>Kick/punt/blocked-kick return TD by the special teams unit.</summary>
    public const string DefensiveSpecialTeamsTd = "def_st_td";

    /// <summary>
    /// Keys that look like touchdowns but must never be counted: <c>td</c> (players: total; DEF rows: opponent
    /// TDs allowed), <c>anytime_tds</c>/<c>first_td</c> (aggregates/props), <c>pass_int_td</c> (a pick-six thrown by
    /// this QB), <c>misc_td</c> (already inside st_td/def_st_td), <c>fum_rec_ez_tds</c> (duplicates fum_rec_td), plus
    /// all <c>*_lng</c>/<c>*_40p</c>/<c>*_50p</c>/<c>bonus_*</c> variants. Listed for documentation and tests.
    /// </summary>
    public static readonly IReadOnlySet<string> NeverCounted = new HashSet<string>(StringComparer.Ordinal)
    {
        "td", "anytime_tds", "first_td", "pass_int_td", "misc_td", "fum_rec_ez_tds",
    };

    /// <summary>
    /// Builds counts from one stat row. <paramref name="isTeamDefense"/> selects the DEF-unit rules (the row id is
    /// an NFL abbreviation, see <see cref="SleeperIds.IsTeamDefense"/>): those rows use <c>def_td</c>/<c>def_st_td</c>
    /// and their <c>td</c> means touchdowns *allowed*, so the individual-player keys are ignored there.
    /// </summary>
    public static TouchdownCounts FromStats(IReadOnlyDictionary<string, double> stats, bool isTeamDefense)
    {
        ArgumentNullException.ThrowIfNull(stats);

        int Read(string key) => stats.TryGetValue(key, out var v) ? (int)Math.Round(v) : 0;
        bool Has(string key) => stats.ContainsKey(key);

        if (isTeamDefense)
        {
            return new TouchdownCounts(
                Defensive: Read(DefensiveTd),
                Return: Read(DefensiveSpecialTeamsTd));
        }

        // st_td is the aggregate; only fall back to the parts when it is absent (never add both).
        var returnTds = Has(SpecialTeamsTd) ? Read(SpecialTeamsTd) : 0;
        var kickReturn = 0;
        var puntReturn = 0;
        var blockedKickReturn = 0;
        if (!Has(SpecialTeamsTd))
        {
            kickReturn = Read(KickReturnTd);
            puntReturn = Read(PuntReturnTd);
            blockedKickReturn = Read(BlockedKickReturnTd) + Read(BlockedPuntReturnTd);
        }

        return new TouchdownCounts(
            Passing: Read(PassTd),
            Rushing: Read(RushTd),
            Receiving: Read(RecTd),
            KickReturn: kickReturn,
            PuntReturn: puntReturn,
            FumbleReturn: Read(FumbleRecoveryTd),
            BlockedKickReturn: blockedKickReturn,
            Return: returnTds,
            Defensive: Read(IdpDefensiveTd) + Read(DefensiveTd));
    }
}
