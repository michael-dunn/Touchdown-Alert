using TouchdownAlert.Core.Sleeper;

namespace TouchdownAlert.Core.Tests;

public class SleeperIdsTests
{
    [Fact]
    public void ToPlayerId_NumericString_ParsesToLong()
    {
        Assert.Equal(4881, SleeperIds.ToPlayerId("4881"));
        Assert.Equal(1401782105192570880, SleeperIds.ToPlayerId("1401782105192570880"));
    }

    [Fact]
    public void ToPlayerId_TeamAbbreviation_IsFixedNegative()
    {
        var det = SleeperIds.ToPlayerId("DET");

        Assert.True(det < 0);
        Assert.True(det >= -32);
        Assert.Equal(det, SleeperIds.ToPlayerId("DET"));
        Assert.NotEqual(det, SleeperIds.ToPlayerId("MIN"));
    }

    [Fact]
    public void ToPlayerId_All32Teams_AreUniqueAndNegative()
    {
        var ids = SleeperIds.NflTeams.Select(SleeperIds.ToPlayerId).ToList();

        Assert.Equal(32, ids.Count);
        Assert.Equal(32, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, -32, -1));
    }

    [Fact]
    public void ToPlayerId_UnknownAbbreviation_IsDeterministicNegativeBelowTeamTable()
    {
        var oak = SleeperIds.ToPlayerId("OAK");
        var stl = SleeperIds.ToPlayerId("STL");

        Assert.True(oak < -32);
        Assert.True(stl < -32);
        Assert.NotEqual(oak, stl);
        Assert.Equal(oak, SleeperIds.ToPlayerId("OAK"));
    }

    [Theory]
    [InlineData("DET", true)]
    [InlineData("GB", true)]
    [InlineData("WAS", true)]
    [InlineData("4881", false)]
    [InlineData("0", false)]
    [InlineData("TEAM_NE", false)]
    [InlineData("", false)]
    [InlineData("det", false)]
    public void IsTeamDefense_RecognisesNflAbbreviationsOnly(string id, bool expected)
    {
        Assert.Equal(expected, SleeperIds.IsTeamDefense(id));
    }

    [Theory]
    [InlineData("QB", 0, "QB")]
    [InlineData("RB", 2, "RB")]
    [InlineData("WR", 4, "WR")]
    [InlineData("TE", 6, "TE")]
    [InlineData("FLEX", 23, "FLEX")]
    [InlineData("K", 17, "K")]
    [InlineData("DEF", 16, "D/ST")]
    [InlineData("BN", 20, "BE")]
    [InlineData("IR", 21, "IR")]
    [InlineData("SUPER_FLEX", 23, "SUPER_FLEX")]
    [InlineData("REC_FLEX", 23, "REC_FLEX")]
    public void LineupSlots_Map_ProducesEspnSlotIds(string rosterPosition, int expectedId, string expectedName)
    {
        var (id, name) = SleeperLineupSlots.Map(rosterPosition);

        Assert.Equal(expectedId, id);
        Assert.Equal(expectedName, name);
        Assert.Equal(rosterPosition is not ("BN" or "IR"), Models.EspnLineupSlots.IsStarter(id));
    }

    [Fact]
    public void LineupSlots_StartingSlots_RemovesBenchAndIr()
    {
        var slots = SleeperLineupSlots.StartingSlots(new[] { "QB", "RB", "FLEX", "DEF", "K", "BN", "BN", "IR" });

        Assert.Equal(new[] { "QB", "RB", "FLEX", "DEF", "K" }, slots);
    }
}
