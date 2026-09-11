namespace TouchdownAlert.Overlay.Services;

/// <summary>One display's geometry, in physical pixels, plus its DPI scale factor (1.0 = 96 dpi).</summary>
public readonly record struct ScreenInfo(bool IsPrimary, double X, double Y, double Width, double Height, double Scale)
{
    public static ScreenInfo Fallback => new(true, 0, 0, 1920, 1080, 1.0);
}

/// <summary>
/// Pure placement math for the overlay window: which display to use given <see cref="Configuration.OverlayOptions.Display"/>,
/// and the default top-right position on that display, both DPI-corrected so a mixed-DPI setup still lands
/// where expected. Kept free of any Win32/WinForms calls so it can be unit tested with fake screen lists.
/// </summary>
public static class ScreenPlacement
{
    /// <summary>
    /// Picks a screen given the "display" setting: "primary", "secondary" (first non-primary, falling back to
    /// primary if there's only one screen), or a zero-based index (clamped to the available screens).
    /// </summary>
    public static ScreenInfo SelectScreen(IReadOnlyList<ScreenInfo> screens, string? display)
    {
        if (screens.Count == 0)
        {
            return ScreenInfo.Fallback;
        }

        var setting = (display ?? "primary").Trim();

        if (int.TryParse(setting, out var index))
        {
            var clamped = Math.Clamp(index, 0, screens.Count - 1);
            return screens[clamped];
        }

        if (setting.Equals("secondary", StringComparison.OrdinalIgnoreCase))
        {
            return screens.FirstOrDefault(s => !s.IsPrimary, screens[0]);
        }

        // "primary" or anything unrecognized.
        return screens.FirstOrDefault(s => s.IsPrimary, screens[0]);
    }

    /// <summary>
    /// Default placement: top-right corner of the given screen with a small margin, in DIPs (the units WPF's
    /// Window.Left/Top expect once the app is per-monitor-DPI-aware) so the window lands at the right spot
    /// regardless of that monitor's scale factor.
    /// </summary>
    public static (double Left, double Top) DefaultTopRight(
        ScreenInfo screen,
        double windowWidthDip,
        double windowHeightDip,
        double marginDip = 16)
    {
        var scale = screen.Scale <= 0 ? 1.0 : screen.Scale;
        var dipX = screen.X / scale;
        var dipY = screen.Y / scale;
        var dipWidth = screen.Width / scale;

        var left = dipX + dipWidth - windowWidthDip - marginDip;
        var top = dipY + marginDip;
        return (left, top);
    }

    /// <summary>
    /// Placement for the touchdown banner window: a strip the full width of the given screen, flush with its
    /// top edge, in DIPs. The height is whatever the banner content needs; only Left/Top/Width are derived here.
    /// </summary>
    public static (double Left, double Top, double Width) TopStrip(ScreenInfo screen)
    {
        var scale = screen.Scale <= 0 ? 1.0 : screen.Scale;
        return (screen.X / scale, screen.Y / scale, screen.Width / scale);
    }
}
