using System.Text.Json;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Tests;

public class TouchdownCountsTests
{
    [Fact]
    public void FromEspnStats_JoshAllen_SeasonLine_HasPassing25Rushing14()
    {
        var stats = FindSeasonActualStats(3918298);

        var counts = TouchdownCounts.FromEspnStats(stats);

        Assert.Equal(25, counts.Passing);
        Assert.Equal(14, counts.Rushing);
        Assert.Equal(0, counts.Receiving);
    }

    [Fact]
    public void FromEspnStats_RavensDst_SeasonLine_HasFumbleAndInterceptionReturnAndIgnoresStat94()
    {
        var stats = FindSeasonActualStats(-16033);

        var counts = TouchdownCounts.FromEspnStats(stats);

        Assert.Equal(1, counts.FumbleReturn);
        Assert.Equal(1, counts.InterceptionReturn);
        // Stat 94 is an aggregate defensive-TD count (2 in the fixture) and must not be counted anywhere.
        Assert.Equal(2, counts.Total);
    }

    [Fact]
    public void FromEspnStats_EmptyDictionary_IsZero()
    {
        var counts = TouchdownCounts.FromEspnStats(new Dictionary<string, double>());

        Assert.Equal(TouchdownCounts.Zero, counts);
    }

    [Fact]
    public void Get_And_With_RoundTrip_ForReturnAndDefensive()
    {
        var counts = TouchdownCounts.Zero
            .With(TouchdownType.Return, 2)
            .With(TouchdownType.Defensive, 1);

        Assert.Equal(2, counts.Get(TouchdownType.Return));
        Assert.Equal(1, counts.Get(TouchdownType.Defensive));
        Assert.Equal(3, counts.Total);
    }

    [Fact]
    public void FromEspnStats_LeavesReturnAndDefensiveAtZero()
    {
        var counts = TouchdownCounts.FromEspnStats(new Dictionary<string, double> { ["4"] = 1 });

        Assert.Equal(0, counts.Return);
        Assert.Equal(0, counts.Defensive);
    }

    private static IReadOnlyDictionary<string, double> FindSeasonActualStats(long playerId)
    {
        using var doc = TestFixtures.LoadDocument("players-2025-td-stat-verification.json");
        var players = doc.RootElement.GetProperty("players");

        foreach (var entry in players.EnumerateArray())
        {
            if (entry.GetProperty("id").GetInt64() != playerId)
            {
                continue;
            }

            var player = entry.GetProperty("player");
            foreach (var statLine in player.GetProperty("stats").EnumerateArray())
            {
                var statSourceId = statLine.GetProperty("statSourceId").GetInt32();
                var statSplitTypeId = statLine.GetProperty("statSplitTypeId").GetInt32();
                var seasonId = statLine.GetProperty("seasonId").GetInt32();

                if (statSourceId == 0 && statSplitTypeId == 0 && seasonId == 2025)
                {
                    var result = new Dictionary<string, double>();
                    foreach (var prop in statLine.GetProperty("stats").EnumerateObject())
                    {
                        result[prop.Name] = prop.Value.GetDouble();
                    }

                    return result;
                }
            }
        }

        throw new InvalidOperationException($"No season actual stat line found for player {playerId}.");
    }
}
