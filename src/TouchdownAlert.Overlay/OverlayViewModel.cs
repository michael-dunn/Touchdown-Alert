using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TouchdownAlert.Overlay.Contracts;

namespace TouchdownAlert.Overlay;

/// <summary>One tile in the overlay: a watched team's color, label, and score.</summary>
public sealed class TeamRowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int TeamId { get; init; }
    public string LeagueKey { get; init; } = "";

    private string _label = "";
    public string Label { get => _label; set => SetField(ref _label, value); }

    private string _color = "#22c55e";
    public string Color { get => _color; set => SetField(ref _color, value); }

    private string _pointsText = "—";
    public string PointsText { get => _pointsText; set => SetField(ref _pointsText, value); }

    private int _touchdownTotal;
    public int TouchdownTotal { get => _touchdownTotal; set => SetField(ref _touchdownTotal, value); }

    private bool _isPulsing;
    public bool IsPulsing { get => _isPulsing; set => SetField(ref _isPulsing, value); }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>One TD banner currently on screen.</summary>
public sealed record BannerViewModel(string TeamLabel, string PlayerName, string TouchdownType, int Count, bool IsTest, string Color)
{
    /// <summary>"TOUCHDOWN · Michael · Josh Allen · passing" (with "×2" appended when Count &gt; 1).</summary>
    public string DisplayText =>
        $"TOUCHDOWN · {TeamLabel} · {PlayerName} · {TouchdownType}" + (Count > 1 ? $" ×{Count}" : "");
}

/// <summary>
/// The overlay's whole UI state: team tiles, the current TD banner, connection status, and applied overlay
/// settings. Pure C# / INotifyPropertyChanged - no WPF types - so it's unit-testable. Every public mutator is
/// marshalled through the optional UI-thread dispatcher (WPF's Dispatcher.Invoke in production; inline in
/// tests) since state/alert/settings arrive on SignalR's own threads.
///
/// Banners are NOT queued here. The App paces alerts (one per Alerts:BannerSeconds) and starts each alert's
/// sound at the moment it sends the "alert" event, so the banner must appear right then: a new alert replaces
/// whatever is showing and runs for the DisplaySeconds the App attached (default 10s).
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    public static readonly TimeSpan DefaultBannerDuration = TimeSpan.FromSeconds(10);

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly TimeProvider _timeProvider;
    private readonly Action<Action> _dispatch;
    private ITimer? _bannerTimer;

    public ObservableCollection<TeamRowViewModel> Rows { get; } = new();

    private bool _isOffline;
    public bool IsOffline { get => _isOffline; private set => SetField(ref _isOffline, value); }

    private bool _bannerVisible;
    public bool BannerVisible { get => _bannerVisible; private set => SetField(ref _bannerVisible, value); }

    private BannerViewModel? _currentBanner;
    public BannerViewModel? CurrentBanner { get => _currentBanner; private set => SetField(ref _currentBanner, value); }

    private OverlaySettingsDto _settings = new();
    public OverlaySettingsDto Settings { get => _settings; private set => SetField(ref _settings, value); }

    public OverlayViewModel(TimeProvider? timeProvider = null, Action<Action>? dispatch = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dispatch = dispatch ?? (action => action());
    }

    public void SetOffline(bool offline) => _dispatch(() => IsOffline = offline);

    /// <summary>Maps a state snapshot's watchedTeams (first 4) onto Rows, preserving existing row identity/pulse state.</summary>
    public void ApplyState(StateDto state) => _dispatch(() =>
    {
        var incoming = state.WatchedTeams.Take(4).ToList();

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!incoming.Any(t => Matches(t, Rows[i])))
            {
                Rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < incoming.Count; i++)
        {
            var dto = incoming[i];
            var row = Rows.FirstOrDefault(r => Matches(dto, r));
            if (row is null)
            {
                row = new TeamRowViewModel { TeamId = dto.TeamId, LeagueKey = dto.LeagueKey ?? "" };
                Rows.Insert(Math.Min(i, Rows.Count), row);
            }

            row.Label = string.IsNullOrWhiteSpace(dto.Label) ? $"Team {dto.TeamId}" : dto.Label;
            row.Color = string.IsNullOrWhiteSpace(dto.Color) ? "#22c55e" : dto.Color;
            row.PointsText = dto.Points.HasValue ? dto.Points.Value.ToString("0.0") : "—";
            row.TouchdownTotal = dto.TouchdownTotal;
        }
    });

    public void ApplySettings(OverlaySettingsDto overlay) => _dispatch(() => Settings = overlay);

    /// <summary>
    /// Shows the alert's banner immediately (replacing any banner already up, since the App has just started
    /// this alert's sound) and pulses its team's tile, for the alert's DisplaySeconds (default 10s).
    /// </summary>
    public void ShowAlert(AlertDto alert) => _dispatch(() =>
    {
        ClearPulse();

        var row = Rows.FirstOrDefault(r => r.TeamId == alert.TeamId && r.LeagueKey == (alert.LeagueKey ?? ""));
        if (row is not null)
        {
            row.IsPulsing = true;
        }

        CurrentBanner = new BannerViewModel(
            alert.TeamLabel ?? row?.Label ?? $"Team {alert.TeamId}",
            alert.PlayerName ?? "",
            (alert.TouchdownType ?? "").ToLowerInvariant(),
            alert.Count <= 0 ? 1 : alert.Count,
            alert.IsTest,
            row?.Color ?? "#22c55e");
        BannerVisible = true;

        var duration = alert.DisplaySeconds > 0 ? TimeSpan.FromSeconds(alert.DisplaySeconds) : DefaultBannerDuration;
        _bannerTimer?.Dispose();
        _bannerTimer = _timeProvider.CreateTimer(
            _ => _dispatch(HideBanner),
            null,
            duration,
            Timeout.InfiniteTimeSpan);
    });

    private void HideBanner()
    {
        ClearPulse();
        BannerVisible = false;
        CurrentBanner = null;
    }

    private void ClearPulse()
    {
        foreach (var row in Rows)
        {
            row.IsPulsing = false;
        }
    }

    private static bool Matches(WatchedTeamDto dto, TeamRowViewModel row) =>
        dto.TeamId == row.TeamId && (dto.LeagueKey ?? "") == row.LeagueKey;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
