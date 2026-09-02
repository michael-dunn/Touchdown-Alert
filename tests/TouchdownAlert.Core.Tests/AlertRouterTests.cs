using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Alerting;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Sounds;

namespace TouchdownAlert.Core.Tests;

public class AlertRouterTests
{
    private static AlertRouter CreateRouter(AlertOptions options)
    {
        var monitor = new TestOptionsMonitor<AlertOptions>(options);
        var soundResolver = new SoundFileResolver(Microsoft.Extensions.Options.Options.Create(new SoundOptions()));
        return new AlertRouter(monitor, soundResolver);
    }

    private static LeagueSnapshot LoadWeek1Snapshot()
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");
        return EspnSnapshotMapper.Map(response, DateTimeOffset.UtcNow);
    }

    private static TouchdownEvent MakeEvent(IReadOnlyList<int> startingTeamIds, IReadOnlyList<int>? benchedTeamIds = null) =>
        new(
            DetectedAt: DateTimeOffset.UtcNow,
            ScoringPeriodId: 1,
            PlayerId: 4362628,
            PlayerName: "Ja'Marr Chase",
            Position: "WR",
            Type: TouchdownType.Receiving,
            Count: 1,
            StartingTeamIds: startingTeamIds,
            BenchedTeamIds: benchedTeamIds ?? []);

    [Fact]
    public void Route_OrdersAlertsByConfiguredWatchOrder()
    {
        var options = new AlertOptions
        {
            WatchedTeams =
            [
                new WatchedTeamOptions { TeamId = 3, SoundFile = "horn.mp3" },
                new WatchedTeamOptions { TeamId = 1, SoundFile = "duck.mp3" },
            ],
        };
        var router = CreateRouter(options);
        var touchdown = MakeEvent([1, 3]);

        var alerts = router.Route(touchdown, LoadWeek1Snapshot());

        Assert.Equal(2, alerts.Count);
        Assert.Equal(3, alerts[0].TeamId);
        Assert.Equal(1, alerts[1].TeamId);
    }

    [Fact]
    public void Route_IgnoresUnwatchedTeams()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 1, SoundFile = "duck.mp3" }],
        };
        var router = CreateRouter(options);
        var touchdown = MakeEvent([3]);

        var alerts = router.Route(touchdown, LoadWeek1Snapshot());

        Assert.Empty(alerts);
    }

    [Fact]
    public void Route_DoesNotAlertForBenchedTeam()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 5, SoundFile = "duck.mp3" }],
        };
        var router = CreateRouter(options);
        var touchdown = MakeEvent(startingTeamIds: [3], benchedTeamIds: [5]);

        var alerts = router.Route(touchdown, LoadWeek1Snapshot());

        Assert.Empty(alerts);
    }

    [Fact]
    public void Route_SkipsWatchedTeamWithBlankSoundFile()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 1, SoundFile = "" }],
        };
        var router = CreateRouter(options);
        var touchdown = MakeEvent([1]);

        var alerts = router.Route(touchdown, LoadWeek1Snapshot());

        Assert.Empty(alerts);
    }

    [Fact]
    public void Route_UsesConfiguredLabelOrFallsBackToTeamName()
    {
        var options = new AlertOptions
        {
            WatchedTeams =
            [
                new WatchedTeamOptions { TeamId = 1, SoundFile = "duck.mp3", Label = "My Team" },
                new WatchedTeamOptions { TeamId = 3, SoundFile = "horn.mp3" },
            ],
        };
        var router = CreateRouter(options);
        var snapshot = LoadWeek1Snapshot();
        var touchdown = MakeEvent([1, 3]);

        var alerts = router.Route(touchdown, snapshot);

        Assert.Equal("My Team", alerts.Single(a => a.TeamId == 1).TeamLabel);
        Assert.Equal(snapshot.FindTeam(3)!.Name, alerts.Single(a => a.TeamId == 3).TeamLabel);
    }

    [Fact]
    public void CreateTestAlert_ProducesSyntheticIsTestAlert()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 1, SoundFile = "duck.mp3" }],
        };
        var router = CreateRouter(options);

        var alert = router.CreateTestAlert(1, LoadWeek1Snapshot());

        Assert.True(alert.IsTest);
        Assert.Equal(1, alert.TeamId);
        Assert.Equal("Test Player", alert.Touchdown.PlayerName);
        Assert.Equal(TouchdownType.Receiving, alert.Touchdown.Type);
        Assert.Equal(1, alert.Touchdown.Count);
    }

    [Fact]
    public void CreateTestAlert_ThrowsForUnwatchedTeam()
    {
        var options = new AlertOptions { WatchedTeams = [new WatchedTeamOptions { TeamId = 1, SoundFile = "duck.mp3" }] };
        var router = CreateRouter(options);

        Assert.Throws<ArgumentException>(() => router.CreateTestAlert(99, LoadWeek1Snapshot()));
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
