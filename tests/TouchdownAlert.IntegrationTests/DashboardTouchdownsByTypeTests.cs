extern alias AppAssembly;

using TouchdownAlert.Core.Models;
using DashboardState = AppAssembly::TouchdownAlert.App.Services.DashboardState;

namespace TouchdownAlert.IntegrationTests;

public sealed class DashboardTouchdownsByTypeTests
{
    private static RosteredPlayer Player(long id, TouchdownCounts tds) =>
        new(id, $"Player {id}", "WR", EspnLineupSlots.Wr, "WR", 1, 10.0, tds);

    [Fact]
    public void NoStarters_ListsCoreTypesAtZero()
    {
        var result = DashboardState.BuildTouchdownsByType(Array.Empty<RosteredPlayer>());

        Assert.Equal(new[] { "Passing", "Rushing", "Receiving" }, result.Select(r => r.Type));
        Assert.All(result, r => Assert.Equal(0, r.Count));
        Assert.Equal(new[] { "Pass", "Rush", "Rec" }, result.Select(r => r.Label));
    }

    [Fact]
    public void SumsEachTypeAcrossStarters_AndOnlyShowsRareTypesWhenScored()
    {
        var starters = new[]
        {
            Player(1, new TouchdownCounts(Passing: 2, Rushing: 1)),
            Player(2, new TouchdownCounts(Receiving: 1, KickReturn: 1)),
            Player(3, new TouchdownCounts(Receiving: 2)),
        };

        var result = DashboardState.BuildTouchdownsByType(starters);

        Assert.Equal(new[] { "Passing", "Rushing", "Receiving", "KickReturn" }, result.Select(r => r.Type));
        Assert.Equal(new[] { 2, 1, 3, 1 }, result.Select(r => r.Count));
        Assert.Equal("KR", result.Single(r => r.Type == "KickReturn").Label);
        Assert.Equal(7, result.Sum(r => r.Count));
    }
}
