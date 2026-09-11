extern alias AppAssembly;

using AppAssembly::TouchdownAlert.App.Contracts;
using AppAssembly::TouchdownAlert.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.IntegrationTests;

/// <summary>
/// AlertPresenter is the App's single alert timeline: sound + "alert" hub event go out together, and the next
/// alert waits until the banner duration has elapsed. These tests drive it with a fake clock.
/// </summary>
public class AlertPresenterTests
{
    private sealed class RecordingBroadcaster : IAlertBroadcaster
    {
        public List<AlertLogEntryViewModel> Sent { get; } = new();

        public Task BroadcastAsync(AlertLogEntryViewModel alert)
        {
            Sent.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticOptions : IOptionsMonitor<AlertOptions>
    {
        public StaticOptions(AlertOptions value) => CurrentValue = value;
        public AlertOptions CurrentValue { get; }
        public AlertOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AlertOptions, string?> listener) => null;
    }

    private static AlertLogEntryViewModel Entry(string team, string player) =>
        new(DateTimeOffset.UtcNow, 1, "main", team, player, "Passing", 1, false, $"{team}.mp3", true);

    private static (AlertPresenter Presenter, RecordingSoundPlayer Sounds, RecordingBroadcaster Hub, FakeTimeProvider Clock) Build(double bannerSeconds)
    {
        var sounds = new RecordingSoundPlayer();
        var hub = new RecordingBroadcaster();
        var clock = new FakeTimeProvider();
        var presenter = new AlertPresenter(
            sounds,
            hub,
            new StaticOptions(new AlertOptions { BannerSeconds = bannerSeconds }),
            clock,
            NullLogger<AlertPresenter>.Instance);
        return (presenter, sounds, hub, clock);
    }

    [Fact]
    public void First_alert_plays_sound_and_broadcasts_immediately_with_display_seconds()
    {
        var (presenter, sounds, hub, _) = Build(10);

        presenter.Enqueue(Entry("Michael", "Josh Allen"), @"C:\sounds\Michael.mp3");

        Assert.Equal(new[] { @"C:\sounds\Michael.mp3" }, sounds.Enqueued);
        var sent = Assert.Single(hub.Sent);
        Assert.Equal("Michael", sent.TeamLabel);
        Assert.Equal(10, sent.DisplaySeconds);
        Assert.Equal(0, presenter.QueuedCount);
    }

    [Fact]
    public void Second_alert_waits_for_the_first_banner_then_plays_its_sound_and_banner_together()
    {
        var (presenter, sounds, hub, clock) = Build(10);

        presenter.Enqueue(Entry("Michael", "Josh Allen"), @"C:\sounds\Michael.mp3");
        presenter.Enqueue(Entry("Lauryn", "CMC"), @"C:\sounds\Lauryn.mp3");

        // Only the first alert is out; the second is held so its sound doesn't play under Michael's banner.
        Assert.Single(sounds.Enqueued);
        Assert.Single(hub.Sent);
        Assert.Equal(1, presenter.QueuedCount);

        clock.Advance(TimeSpan.FromSeconds(9.9));
        Assert.Single(sounds.Enqueued);
        Assert.Single(hub.Sent);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal(new[] { @"C:\sounds\Michael.mp3", @"C:\sounds\Lauryn.mp3" }, sounds.Enqueued);
        Assert.Equal(new[] { "Michael", "Lauryn" }, hub.Sent.Select(a => a.TeamLabel));
        Assert.Equal(0, presenter.QueuedCount);
    }

    [Fact]
    public void Alerts_keep_arrival_order_across_several_banner_slots()
    {
        var (presenter, _, hub, clock) = Build(10);

        presenter.Enqueue(Entry("A", "p1"), null);
        presenter.Enqueue(Entry("B", "p2"), null);
        presenter.Enqueue(Entry("C", "p3"), null);

        clock.Advance(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "A", "B", "C" }, hub.Sent.Select(a => a.TeamLabel));
    }

    [Fact]
    public void Alert_arriving_after_the_slot_ended_goes_out_immediately()
    {
        var (presenter, _, hub, clock) = Build(10);

        presenter.Enqueue(Entry("A", "p1"), null);
        clock.Advance(TimeSpan.FromSeconds(12));

        presenter.Enqueue(Entry("B", "p2"), null);

        Assert.Equal(new[] { "A", "B" }, hub.Sent.Select(a => a.TeamLabel));
    }

    [Fact]
    public void Missing_sound_still_gets_its_banner_slot()
    {
        var (presenter, sounds, hub, _) = Build(10);

        presenter.Enqueue(Entry("A", "p1"), null);

        Assert.Empty(sounds.Enqueued);
        Assert.Single(hub.Sent);
    }

    [Fact]
    public void Zero_banner_seconds_disables_pacing()
    {
        var (presenter, sounds, hub, _) = Build(0);

        presenter.Enqueue(Entry("A", "p1"), @"a.mp3");
        presenter.Enqueue(Entry("B", "p2"), @"b.mp3");

        Assert.Equal(new[] { "a.mp3", "b.mp3" }, sounds.Enqueued);
        Assert.Equal(2, hub.Sent.Count);
        Assert.All(hub.Sent, a => Assert.Equal(0, a.DisplaySeconds));
    }
}
