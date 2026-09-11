using TouchdownAlert.Overlay.Contracts;

namespace TouchdownAlert.Overlay.Tests;

public class OverlayViewModelTests
{
    private static StateDto StateWith(params (int TeamId, string Label, string Color, double? Points, int Tds)[] teams) =>
        new()
        {
            WatchedTeams = teams
                .Select(t => new WatchedTeamDto { TeamId = t.TeamId, LeagueKey = "main", Label = t.Label, Color = t.Color, Points = t.Points, TouchdownTotal = t.Tds })
                .ToList(),
        };

    [Fact]
    public void ApplyState_maps_watched_teams_onto_rows()
    {
        var vm = new OverlayViewModel();
        vm.ApplyState(StateWith((1, "Michael", "#22c55e", 92.8, 3), (3, "Lauryn", "#3b82f6", null, 0)));

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("Michael", vm.Rows[0].Label);
        Assert.Equal("#22c55e", vm.Rows[0].Color);
        Assert.Equal("92.8", vm.Rows[0].PointsText);
        Assert.Equal(3, vm.Rows[0].TouchdownTotal);

        Assert.Equal("Lauryn", vm.Rows[1].Label);
        Assert.Equal("—", vm.Rows[1].PointsText);
    }

    [Fact]
    public void ApplyState_caps_at_four_rows()
    {
        var vm = new OverlayViewModel();
        vm.ApplyState(StateWith(
            (1, "A", "#111111", 1, 0),
            (2, "B", "#222222", 2, 0),
            (3, "C", "#333333", 3, 0),
            (4, "D", "#444444", 4, 0),
            (5, "E", "#555555", 5, 0)));

        Assert.Equal(4, vm.Rows.Count);
        Assert.Equal("A", vm.Rows[0].Label);
        Assert.Equal("D", vm.Rows[3].Label);
    }

    [Fact]
    public void ApplyState_defaults_missing_label_to_team_id()
    {
        var vm = new OverlayViewModel();
        vm.ApplyState(new StateDto { WatchedTeams = { new WatchedTeamDto { TeamId = 7, LeagueKey = "main" } } });

        Assert.Equal("Team 7", vm.Rows[0].Label);
        Assert.Equal("—", vm.Rows[0].PointsText);
    }

    [Fact]
    public void ApplyState_reuses_existing_row_instance_for_same_team()
    {
        var vm = new OverlayViewModel();
        vm.ApplyState(StateWith((1, "Michael", "#22c55e", 10, 1)));
        var row = vm.Rows[0];

        vm.ApplyState(StateWith((1, "Michael", "#22c55e", 20, 2)));

        Assert.Same(row, vm.Rows[0]);
        Assert.Equal("20.0", row.PointsText);
        Assert.Equal(2, row.TouchdownTotal);
    }

    [Fact]
    public void ApplySettings_updates_settings_and_raises_property_changed()
    {
        var vm = new OverlayViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(OverlayViewModel.Settings);

        vm.ApplySettings(new OverlaySettingsDto { Scale = 1.5, Opacity = 0.5, Locked = false });

        Assert.True(raised);
        Assert.Equal(1.5, vm.Settings.Scale);
        Assert.False(vm.Settings.Locked);
    }

    [Fact]
    public void SetOffline_updates_is_offline()
    {
        var vm = new OverlayViewModel();
        vm.SetOffline(true);
        Assert.True(vm.IsOffline);
        vm.SetOffline(false);
        Assert.False(vm.IsOffline);
    }

    [Fact]
    public void ShowAlert_shows_banner_immediately_and_pulses_the_team_tile()
    {
        var clock = new FakeTimeProvider();
        var vm = new OverlayViewModel(clock);
        vm.ApplyState(StateWith((1, "Michael", "#22c55e", 10, 1)));

        vm.ShowAlert(new AlertDto { TeamId = 1, LeagueKey = "main", TeamLabel = "Michael", PlayerName = "Josh Allen", TouchdownType = "Passing", Count = 1 });

        Assert.True(vm.BannerVisible);
        Assert.NotNull(vm.CurrentBanner);
        Assert.Equal("TOUCHDOWN · Michael · Josh Allen · passing", vm.CurrentBanner!.DisplayText);
        Assert.Equal("#22c55e", vm.CurrentBanner.Color);
        Assert.True(vm.Rows[0].IsPulsing);
    }

    [Fact]
    public void Banner_text_lowercases_type_and_appends_count_when_greater_than_one()
    {
        var vm = new OverlayViewModel(new FakeTimeProvider());
        vm.ShowAlert(new AlertDto { TeamId = 1, TeamLabel = "Michael", PlayerName = "Josh Allen", TouchdownType = "PASSING", Count = 2 });

        Assert.Equal("TOUCHDOWN · Michael · Josh Allen · passing ×2", vm.CurrentBanner!.DisplayText);
    }

    [Fact]
    public void Banner_carries_is_test_flag()
    {
        var vm = new OverlayViewModel(new FakeTimeProvider());
        vm.ShowAlert(new AlertDto { TeamId = 1, TeamLabel = "Michael", PlayerName = "Josh Allen", TouchdownType = "rushing", Count = 1, IsTest = true });

        Assert.True(vm.CurrentBanner!.IsTest);
    }

    [Fact]
    public void Banner_hides_after_ten_seconds_by_default()
    {
        var clock = new FakeTimeProvider();
        var vm = new OverlayViewModel(clock);
        vm.ApplyState(StateWith((1, "Michael", "#22c55e", 10, 1)));
        vm.ShowAlert(new AlertDto { TeamId = 1, LeagueKey = "main", TeamLabel = "Michael", PlayerName = "Josh Allen", TouchdownType = "passing", Count = 1 });

        clock.Advance(TimeSpan.FromSeconds(9.9));
        Assert.True(vm.BannerVisible);
        Assert.True(vm.Rows[0].IsPulsing);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.False(vm.BannerVisible);
        Assert.Null(vm.CurrentBanner);
        Assert.False(vm.Rows[0].IsPulsing);
    }

    [Fact]
    public void Banner_uses_display_seconds_sent_by_the_app()
    {
        var clock = new FakeTimeProvider();
        var vm = new OverlayViewModel(clock);
        vm.ShowAlert(new AlertDto { TeamId = 1, TeamLabel = "Michael", PlayerName = "Josh Allen", TouchdownType = "passing", Count = 1, DisplaySeconds = 4 });

        clock.Advance(TimeSpan.FromSeconds(3.9));
        Assert.True(vm.BannerVisible);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.False(vm.BannerVisible);
    }

    [Fact]
    public void New_alert_replaces_the_current_banner_and_restarts_its_timer()
    {
        // The App paces alerts and starts each sound when it sends the alert, so the overlay must switch
        // banners the moment the next alert arrives rather than queueing it behind the current one.
        var clock = new FakeTimeProvider();
        var vm = new OverlayViewModel(clock);
        vm.ApplyState(StateWith((1, "Michael", "#22c55e", 10, 1), (3, "Lauryn", "#3b82f6", 10, 1)));

        vm.ShowAlert(new AlertDto { TeamId = 1, LeagueKey = "main", TeamLabel = "Michael", PlayerName = "Josh Allen", TouchdownType = "passing", Count = 1 });
        clock.Advance(TimeSpan.FromSeconds(8));

        vm.ShowAlert(new AlertDto { TeamId = 3, LeagueKey = "main", TeamLabel = "Lauryn", PlayerName = "CMC", TouchdownType = "rushing", Count = 1 });

        Assert.Equal("Lauryn", vm.CurrentBanner!.TeamLabel);
        Assert.False(vm.Rows[0].IsPulsing);
        Assert.True(vm.Rows[1].IsPulsing);

        // The first alert's timer (due at 10s) must not take the second banner down early.
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(vm.BannerVisible);
        Assert.Equal("Lauryn", vm.CurrentBanner!.TeamLabel);

        clock.Advance(TimeSpan.FromSeconds(7.1));
        Assert.False(vm.BannerVisible);
        Assert.False(vm.Rows[1].IsPulsing);
    }

    [Fact]
    public void Dispatch_delegate_is_used_for_every_mutation()
    {
        var invocations = 0;
        var vm = new OverlayViewModel(new FakeTimeProvider(), dispatch: action =>
        {
            invocations++;
            action();
        });

        vm.SetOffline(true);
        vm.ApplyState(new StateDto());
        vm.ApplySettings(new OverlaySettingsDto());
        vm.ShowAlert(new AlertDto { TeamId = 1 });

        Assert.Equal(4, invocations);
    }
}
