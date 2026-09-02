using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Alerting;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Sounds;

namespace TouchdownAlert.Core.Tests;

public class AlertRouterTests
{
    private static readonly LeagueRef MainLeague = new("main", LeagueProvider.Espn, "998946988");
    private static readonly LeagueRef OtherLeague = new("other", LeagueProvider.Espn, "998946988");

    private static AlertRouter CreateRouter(AlertOptions options, LeaguesOptions? leaguesOptions = null)
    {
        var monitor = new TestOptionsMonitor<AlertOptions>(options);
        var leaguesMonitor = new TestOptionsMonitor<LeaguesOptions>(leaguesOptions ?? new LeaguesOptions
        {
            Items = [new LeagueOptions { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "998946988" }],
        });
        var soundResolver = new SoundFileResolver(Microsoft.Extensions.Options.Options.Create(new SoundOptions()));
        return new AlertRouter(monitor, leaguesMonitor, soundResolver);
    }

    private static LeagueSnapshot LoadWeek1Snapshot(LeagueRef? league = null)
    {
        var response = TestFixtures.LoadResponse("league-week1-live-sample.json");
        return EspnSnapshotMapper.Map(response, league ?? MainLeague, DateTimeOffset.UtcNow);
    }

    private static TouchdownEvent MakeEvent(IReadOnlyList<int> startingTeamIds, IReadOnlyList<int>? benchedTeamIds = null, LeagueRef? league = null) =>
        new(
            League: league ?? MainLeague,
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
                new WatchedTeamOptions { TeamId = 3, League = "main", SoundFile = "horn.mp3" },
                new WatchedTeamOptions { TeamId = 1, League = "main", SoundFile = "duck.mp3" },
            ],
        };
        var router = CreateRouter(options);
        var touchdown = MakeEvent([1, 3]);

        var alerts = router.Route(touchdown, LoadWeek1Snapshot());

        Assert.Equal(2, alerts.Count);
        Assert.Equal(3, alerts[0].TeamId);
        Assert.Equal(1, alerts[1].TeamId);
        Assert.All(alerts, a => Assert.Equal("main", a.LeagueKey));
    }

    [Fact]
    public void Route_IgnoresUnwatchedTeams()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 1, League = "main", SoundFile = "duck.mp3" }],
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
            WatchedTeams = [new WatchedTeamOptions { TeamId = 5, League = "main", SoundFile = "duck.mp3" }],
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
            WatchedTeams = [new WatchedTeamOptions { TeamId = 1, League = "main", SoundFile = "" }],
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
                new WatchedTeamOptions { TeamId = 1, League = "main", SoundFile = "duck.mp3", Label = "My Team" },
                new WatchedTeamOptions { TeamId = 3, League = "main", SoundFile = "horn.mp3" },
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
    public void Route_IgnoresSameTeamIdInAnotherLeague()
    {
        var options = new AlertOptions
        {
            WatchedTeams =
            [
                new WatchedTeamOptions { TeamId = 3, League = "other", SoundFile = "horn.mp3" },
            ],
        };
        var leaguesOptions = new LeaguesOptions
        {
            Items =
            [
                new LeagueOptions { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "998946988" },
                new LeagueOptions { Key = "other", Provider = LeagueProvider.Espn, LeagueId = "998946988" },
            ],
        };
        var router = CreateRouter(options, leaguesOptions);

        // Touchdown happened in "main", but team 3 is only watched in "other".
        var touchdown = MakeEvent([3], league: MainLeague);
        var alerts = router.Route(touchdown, LoadWeek1Snapshot());
        Assert.Empty(alerts);

        // Same team id, same touchdown, but in "other" -> matches.
        var touchdownOther = MakeEvent([3], league: OtherLeague);
        var alertsOther = router.Route(touchdownOther, LoadWeek1Snapshot(OtherLeague));
        Assert.Single(alertsOther);
        Assert.Equal("other", alertsOther[0].LeagueKey);
    }

    [Fact]
    public void CreateTestAlert_ProducesSyntheticIsTestAlert()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 1, League = "main", SoundFile = "duck.mp3" }],
        };
        var router = CreateRouter(options);

        var alert = router.CreateTestAlert("main", 1, LoadWeek1Snapshot());

        Assert.True(alert.IsTest);
        Assert.Equal(1, alert.TeamId);
        Assert.Equal("main", alert.LeagueKey);
        Assert.Equal("Test Player", alert.Touchdown.PlayerName);
        Assert.Equal(TouchdownType.Receiving, alert.Touchdown.Type);
        Assert.Equal(1, alert.Touchdown.Count);
    }

    [Fact]
    public void CreateTestAlert_ThrowsForUnwatchedTeam()
    {
        var options = new AlertOptions { WatchedTeams = [new WatchedTeamOptions { TeamId = 1, League = "main", SoundFile = "duck.mp3" }] };
        var router = CreateRouter(options);

        Assert.Throws<ArgumentException>(() => router.CreateTestAlert("main", 99, LoadWeek1Snapshot()));
    }

    [Fact]
    public void CreateTestAlert_IsLeagueQualified()
    {
        var options = new AlertOptions
        {
            WatchedTeams = [new WatchedTeamOptions { TeamId = 3, League = "other", SoundFile = "duck.mp3" }],
        };
        var leaguesOptions = new LeaguesOptions
        {
            Items =
            [
                new LeagueOptions { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "998946988" },
                new LeagueOptions { Key = "other", Provider = LeagueProvider.Espn, LeagueId = "998946988" },
            ],
        };
        var router = CreateRouter(options, leaguesOptions);

        // Team 3 is only watched in "other", not "main".
        Assert.Throws<ArgumentException>(() => router.CreateTestAlert("main", 3, LoadWeek1Snapshot()));

        var alert = router.CreateTestAlert("other", 3, LoadWeek1Snapshot(OtherLeague));
        Assert.Equal("other", alert.LeagueKey);
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
