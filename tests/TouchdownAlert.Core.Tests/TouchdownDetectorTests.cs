using TouchdownAlert.Core.Detection;
using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Tests;

public class TouchdownDetectorTests
{
    private static LeagueSnapshot LoadSnapshot(string fileName, DateTimeOffset fetchedAt)
    {
        var response = TestFixtures.LoadResponse(fileName);
        return EspnSnapshotMapper.Map(response, fetchedAt);
    }

    [Fact]
    public void Update_FirstCall_SeedsAndReturnsEmpty()
    {
        var detector = new TouchdownDetector();
        var snapshot = LoadSnapshot("league-week1-live-sample.json", DateTimeOffset.UtcNow);

        var events = detector.Update(snapshot);

        Assert.Empty(events);
        Assert.True(detector.IsSeeded);
        Assert.Equal(snapshot.ScoringPeriodId, detector.SeededScoringPeriodId);
    }

    [Fact]
    public void Update_BeforeThenAfterFixture_YieldsExactlyTwoEvents()
    {
        var detector = new TouchdownDetector();
        var before = LoadSnapshot("league-week1-live-sample.json", DateTimeOffset.UtcNow);
        detector.Update(before);

        var after = LoadSnapshot("league-week1-live-sample-after-td.json", before.FetchedAt.AddMinutes(1));
        var events = detector.Update(after);

        Assert.Equal(2, events.Count);

        var passing = Assert.Single(events, e => e.Type == TouchdownType.Passing);
        Assert.Equal(3918298, passing.PlayerId);
        Assert.Equal(1, passing.Count);
        Assert.Equal([1], passing.StartingTeamIds);
        Assert.Empty(passing.BenchedTeamIds);

        var receiving = Assert.Single(events, e => e.Type == TouchdownType.Receiving);
        Assert.Equal(4362628, receiving.PlayerId);
        Assert.Equal(1, receiving.Count);
        Assert.Equal([3], receiving.StartingTeamIds);
        Assert.Equal([5], receiving.BenchedTeamIds);
    }

    [Fact]
    public void Update_DecreaseInCount_EmitsNothingButUpdatesState()
    {
        var detector = new TouchdownDetector();
        var high = LoadSnapshot("league-week1-live-sample-after-td.json", DateTimeOffset.UtcNow);
        detector.Update(high);

        var low = LoadSnapshot("league-week1-live-sample.json", high.FetchedAt.AddMinutes(1));
        var events = detector.Update(low);

        Assert.Empty(events);

        // Subsequent increase back up should only report the delta from the corrected (lower) baseline.
        var backUp = LoadSnapshot("league-week1-live-sample-after-td.json", low.FetchedAt.AddMinutes(1));
        var events2 = detector.Update(backUp);
        Assert.Equal(2, events2.Count);
        Assert.All(events2, e => Assert.Equal(1, e.Count));
    }

    [Fact]
    public void Update_ScoringPeriodChange_ReSeedsAndReturnsEmpty()
    {
        var detector = new TouchdownDetector();
        var before = LoadSnapshot("league-week1-live-sample.json", DateTimeOffset.UtcNow);
        detector.Update(before);

        var afterSameResponse = TestFixtures.LoadResponse("league-week1-live-sample-after-td.json");
        afterSameResponse.ScoringPeriodId = 2;
        var newPeriodSnapshot = EspnSnapshotMapper.Map(afterSameResponse, before.FetchedAt.AddMinutes(1));

        var events = detector.Update(newPeriodSnapshot);

        Assert.Empty(events);
        Assert.Equal(2, detector.SeededScoringPeriodId);
    }

    [Fact]
    public void Update_NewPlayerMidPeriod_SeedsSilently()
    {
        var detector = new TouchdownDetector();
        var before = LoadSnapshot("league-week1-live-sample.json", DateTimeOffset.UtcNow);
        detector.Update(before);

        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");
        var team1Roster = response.Schedule
            .SelectMany(m => new[] { m.Home, m.Away })
            .Where(s => s?.TeamId == 1)
            .Select(s => s!.RosterForCurrentScoringPeriod!.Entries)
            .Single();

        var pickup = new Espn.Wire.EspnRosterEntry
        {
            PlayerId = 555111,
            LineupSlotId = 20,
            PlayerPoolEntry = new Espn.Wire.EspnPlayerPoolEntry
            {
                Id = 555111,
                AppliedStatTotal = 3.0,
                Player = new Espn.Wire.EspnPlayer
                {
                    Id = 555111,
                    FullName = "Waiver Pickup",
                    DefaultPositionId = 3,
                    ProTeamId = 9,
                    Stats =
                    [
                        new Espn.Wire.EspnPlayerStats
                        {
                            SeasonId = 2026,
                            ScoringPeriodId = 1,
                            StatSourceId = 0,
                            StatSplitTypeId = 1,
                            AppliedTotal = 3.0,
                            Stats = new Dictionary<string, double> { ["43"] = 1.0 },
                        },
                    ],
                },
            },
        };
        team1Roster.Add(pickup);

        var withPickup = EspnSnapshotMapper.Map(response, before.FetchedAt.AddMinutes(2));
        var events = detector.Update(withPickup);

        // Seeded silently: no event even though the pickup already "has" a receiving TD.
        Assert.Empty(events);

        // A later increase for that player should now be detected as a genuine event.
        team1Roster.Single(e => e.PlayerId == 555111).PlayerPoolEntry!.Player!.Stats[0].Stats["43"] = 2.0;
        var withPickupTd = EspnSnapshotMapper.Map(response, before.FetchedAt.AddMinutes(3));
        var events2 = detector.Update(withPickupTd);

        var evt = Assert.Single(events2);
        Assert.Equal(555111, evt.PlayerId);
        Assert.Equal(TouchdownType.Receiving, evt.Type);
        Assert.Equal(1, evt.Count);
    }

    [Fact]
    public void Update_CounterJumpsByTwo_ReportsCountTwo()
    {
        var detector = new TouchdownDetector();
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");
        var baseline = EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);
        detector.Update(baseline);

        var chaseEntry = response.Schedule
            .SelectMany(m => new[] { m.Home, m.Away })
            .Where(s => s?.TeamId == 3)
            .SelectMany(s => s!.RosterForCurrentScoringPeriod!.Entries)
            .Single(e => e.PlayerId == 4362628);
        chaseEntry.PlayerPoolEntry!.Player!.Stats[0].Stats["43"] = 2.0;

        var jumped = EspnSnapshotMapper.Map(response, baseline.FetchedAt.AddMinutes(1));
        var events = detector.Update(jumped);

        var evt = Assert.Single(events);
        Assert.Equal(TouchdownType.Receiving, evt.Type);
        Assert.Equal(2, evt.Count);
    }

    [Fact]
    public void Reset_ClearsSeededState()
    {
        var detector = new TouchdownDetector();
        var before = LoadSnapshot("league-week1-live-sample.json", DateTimeOffset.UtcNow);
        detector.Update(before);
        Assert.True(detector.IsSeeded);

        detector.Reset();

        Assert.False(detector.IsSeeded);
        Assert.Null(detector.SeededScoringPeriodId);

        // Next update seeds again rather than emitting events, even against the "after" fixture.
        var after = LoadSnapshot("league-week1-live-sample-after-td.json", before.FetchedAt.AddMinutes(1));
        var events = detector.Update(after);
        Assert.Empty(events);
    }
}
