using TouchdownAlert.Overlay.Interop;

namespace TouchdownAlert.Overlay.Services;

/// <summary>
/// Thin wrapper around System.Windows.Forms.Screen.AllScreens + per-monitor DPI, isolated here so the pure
/// <see cref="ScreenPlacement"/> math stays unit-testable against fake <see cref="ScreenInfo"/> lists.
/// </summary>
public static class ScreenEnumerator
{
    public static IReadOnlyList<ScreenInfo> GetScreens()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return new[] { ScreenInfo.Fallback };
        }

        var result = new List<ScreenInfo>(screens.Length);
        foreach (var screen in screens)
        {
            var bounds = screen.Bounds;
            var centerX = bounds.X + bounds.Width / 2.0;
            var centerY = bounds.Y + bounds.Height / 2.0;
            var scale = WindowStyles.GetScaleForPoint(centerX, centerY);
            result.Add(new ScreenInfo(screen.Primary, bounds.X, bounds.Y, bounds.Width, bounds.Height, scale));
        }

        return result;
    }
}
