namespace TouchdownAlert.Overlay.Contracts;

/// <summary>
/// Tolerant view of Engineer A's settings contract: GET/PUT /api/settings and the "settings" hub event.
/// The overlay only cares about the "overlay" sub-object; everything else round-trips as opaque JSON
/// elements it never has to understand (and so never breaks on).
/// </summary>
public sealed class SettingsDto
{
    public OverlaySettingsDto? Overlay { get; set; }
}

/// <summary>Mirrors TouchdownAlert.Core.Configuration.OverlayOptions, camelCase over the wire.</summary>
public sealed class OverlaySettingsDto
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public string Display { get; set; } = "primary";
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.7;
    public bool Locked { get; set; } = true;
    public bool Enabled { get; set; } = true;
}
