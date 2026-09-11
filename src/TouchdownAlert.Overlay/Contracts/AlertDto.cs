namespace TouchdownAlert.Overlay.Contracts;

/// <summary>The "alert" hub event payload (AlertLogEntryViewModel on the App side).</summary>
public sealed class AlertDto
{
    public DateTimeOffset At { get; set; }
    public int TeamId { get; set; }
    public string? LeagueKey { get; set; }
    public string? TeamLabel { get; set; }
    public string? PlayerName { get; set; }
    public string? TouchdownType { get; set; }
    public int Count { get; set; } = 1;
    public bool IsTest { get; set; }

    /// <summary>How long the App wants this banner shown (it paces consecutive alerts by the same amount). Zero/absent = default.</summary>
    public double DisplaySeconds { get; set; }
}
