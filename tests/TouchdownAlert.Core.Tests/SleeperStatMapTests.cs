using TouchdownAlert.Core.Sleeper;

namespace TouchdownAlert.Core.Tests;

public class SleeperStatMapTests
{
    private static IReadOnlyDictionary<string, double> Row(params (string Key, double Value)[] stats) =>
        stats.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);

    [Fact]
    public void FromStats_Week1Fixture_Player4984_HasTwoPassingAndTwoRushing()
    {
        var stats = SleeperTestSupport.LoadStats("stats-2025-week1-sample.json");

        var counts = SleeperStatMap.FromStats(stats["4984"], isTeamDefense: false);

        Assert.Equal(2, counts.Passing);
        Assert.Equal(2, counts.Rushing);
        Assert.Equal(0, counts.Receiving);
        // anytime_tds (2) must not be added on top.
        Assert.Equal(4, counts.Total);
    }

    [Fact]
    public void FromStats_Week2Fixture_Player6945_StTd_MapsToReturn()
    {
        var stats = SleeperTestSupport.LoadStats("stats-2025-week2-return-tds.json");

        var counts = SleeperStatMap.FromStats(stats["6945"], isTeamDefense: false);

        Assert.Equal(1, counts.Return);
        Assert.Equal(0, counts.KickReturn);
        Assert.Equal(0, counts.PuntReturn);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void FromStats_Week2Fixture_Player4960_IdpDefTd_MapsToDefensive()
    {
        var stats = SleeperTestSupport.LoadStats("stats-2025-week2-return-tds.json");

        var counts = SleeperStatMap.FromStats(stats["4960"], isTeamDefense: false);

        Assert.Equal(1, counts.Defensive);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void Parse_DropsTeamAggregateRows()
    {
        var stats = SleeperTestSupport.LoadStats("stats-2025-week2-return-tds.json");

        Assert.False(stats.ContainsKey("TEAM_NE"));
        Assert.False(stats.ContainsKey("TEAM_SEA"));
        Assert.True(stats.ContainsKey("6945"));
    }

    [Fact]
    public void FromStats_TeamDefense_DefStTd_MapsToReturnOnly()
    {
        // Shape of an NE row with a special-teams return TD; the real 2025 fixtures never carried def_st_td
        // on a DEF row so this is synthetic.
        var counts = SleeperStatMap.FromStats(Row(("def_st_td", 1), ("td", 2), ("pts_allow", 14)), isTeamDefense: true);

        Assert.Equal(1, counts.Return);
        Assert.Equal(0, counts.Defensive);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void FromStats_TeamDefense_DefTd_MapsToDefensive()
    {
        var counts = SleeperStatMap.FromStats(Row(("def_td", 1), ("td", 3), ("int", 1)), isTeamDefense: true);

        Assert.Equal(1, counts.Defensive);
        Assert.Equal(0, counts.Return);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void FromStats_TeamDefense_OnlyTd_IsTouchdownsAllowed_YieldsZero()
    {
        // "td" on a DEF row is opponent touchdowns allowed (the simulator emits a decoy td:3 on every DEF row).
        Assert.Equal(0, SleeperStatMap.FromStats(Row(("td", 3), ("pts_allow", 21)), isTeamDefense: true).Total);
        Assert.Equal(0, SleeperStatMap.FromStats(Row(("td", 5), ("pts_std", -2)), isTeamDefense: true).Total);
    }

    [Fact]
    public void FromStats_Week1Fixture_AllDefRows_YieldZero()
    {
        var stats = SleeperTestSupport.LoadStats("stats-2025-week1-sample.json");
        var defRows = stats.Where(kv => SleeperIds.IsTeamDefense(kv.Key)).ToList();

        Assert.Equal(12, defRows.Count);
        // Most DEF rows carry the aggregate "td" (touchdowns allowed); a shutout row has none. Either way: zero.
        Assert.Contains(defRows, r => r.Value.ContainsKey("td"));
        foreach (var (_, row) in defRows)
        {
            Assert.Equal(0, SleeperStatMap.FromStats(row, isTeamDefense: true).Total);
        }
    }

    [Fact]
    public void FromStats_TeamDefense_MiscTd_NotAddedToDefStTd()
    {
        var counts = SleeperStatMap.FromStats(Row(("def_st_td", 1), ("misc_td", 1)), isTeamDefense: true);

        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void FromStats_Player_StTdPlusMiscTd_CountsOnce()
    {
        var counts = SleeperStatMap.FromStats(Row(("st_td", 1), ("misc_td", 1), ("kr_td", 1)), isTeamDefense: false);

        Assert.Equal(1, counts.Return);
        Assert.Equal(0, counts.KickReturn);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void FromStats_Player_WithoutStTd_FallsBackToSpecificReturnKeys()
    {
        var counts = SleeperStatMap.FromStats(
            Row(("kr_td", 1), ("pr_td", 1), ("blk_kick_ret_td", 1), ("blk_pr_td", 1)), isTeamDefense: false);

        Assert.Equal(1, counts.KickReturn);
        Assert.Equal(1, counts.PuntReturn);
        Assert.Equal(2, counts.BlockedKickReturn);
        Assert.Equal(0, counts.Return);
        Assert.Equal(4, counts.Total);
    }

    [Fact]
    public void FromStats_Player_FumRecEzTds_DuplicatesFumRecTd_CountsOnce()
    {
        var counts = SleeperStatMap.FromStats(Row(("fum_rec_td", 1), ("fum_rec_ez_tds", 1)), isTeamDefense: false);

        Assert.Equal(1, counts.FumbleReturn);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public void FromStats_Player_IgnoresAggregatesAndDecoys()
    {
        var counts = SleeperStatMap.FromStats(
            Row(("td", 3), ("anytime_tds", 3), ("first_td", 1), ("pass_int_td", 1), ("rush_td_lng", 40), ("rec_td_40p", 1), ("bonus_rec_td_50p", 1)),
            isTeamDefense: false);

        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public void FromStats_Player_DefTd_OnPlayerRow_CountsDefensive()
    {
        var counts = SleeperStatMap.FromStats(Row(("def_td", 1)), isTeamDefense: false);

        Assert.Equal(1, counts.Defensive);
    }

    [Fact]
    public void FromStats_FractionalValues_AreRounded()
    {
        var counts = SleeperStatMap.FromStats(Row(("rec_td", 2.0), ("rush_td", 1.0)), isTeamDefense: false);

        Assert.Equal(2, counts.Receiving);
        Assert.Equal(1, counts.Rushing);
    }

    [Fact]
    public void FromStats_EmptyRow_IsZero()
    {
        Assert.Equal(0, SleeperStatMap.FromStats(Row(), isTeamDefense: false).Total);
        Assert.Equal(0, SleeperStatMap.FromStats(Row(), isTeamDefense: true).Total);
    }

    [Fact]
    public void Parse_TolerantOfNonNumericValues()
    {
        var stats = SleeperStatsParser.Parse("""{"1": {"rush_td": 1, "note": "x", "gp": null, "rec_td": "2"}, "TEAM_X": {"td": 9}, "bad": 5}""");

        Assert.Single(stats);
        Assert.Equal(1, stats["1"]["rush_td"]);
        Assert.Equal(2, stats["1"]["rec_td"]);
        Assert.False(stats["1"].ContainsKey("note"));
        Assert.False(stats["1"].ContainsKey("gp"));
    }
}
