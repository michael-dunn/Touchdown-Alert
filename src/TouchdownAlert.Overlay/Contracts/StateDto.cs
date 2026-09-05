namespace TouchdownAlert.Overlay.Contracts;

/// <summary>
/// Overlay's own tolerant view of the App's DashboardViewModel JSON (camelCase over the wire). Only the
/// fields the overlay needs are modeled; everything is nullable/defaulted so an evolving contract on the
/// App side never breaks deserialization.
/// </summary>
public sealed class StateDto
{
    public string? LeagueName { get; set; }
    public List<WatchedTeamDto> WatchedTeams { get; set; } = new();
}

public sealed class WatchedTeamDto
{
    public int TeamId { get; set; }
    public string? LeagueKey { get; set; }
    public string? Label { get; set; }
    public string? Color { get; set; }
    public double? Points { get; set; }
    public int TouchdownTotal { get; set; }
}
