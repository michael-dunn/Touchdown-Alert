using System.Runtime.InteropServices;

namespace TouchdownAlert.Overlay.Interop;

/// <summary>
/// Win32 P/Invoke needed to make the overlay window click-through / always-on-top / not-in-taskbar,
/// and to read the DPI of the monitor a window is on for correct placement on a mixed-DPI setup.
/// </summary>
internal static class WindowStyles
{
    public const int GWL_EXSTYLE = -20;

    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    /// <summary>Applies (or removes) click-through style on top of the base toolwindow/no-activate styles.</summary>
    public static void ApplyExStyle(IntPtr hWnd, bool locked)
    {
        var baseStyle = WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        var style = locked ? baseStyle | WS_EX_TRANSPARENT : baseStyle;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Returns the DPI scale factor (1.0 = 96 dpi) of the monitor the given window handle is currently on.</summary>
    public static double GetScaleForWindow(IntPtr hWnd)
    {
        try
        {
            var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            return ScaleFromMonitor(monitor);
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>Returns the DPI scale factor of the monitor containing the given point (physical pixels) — used to
    /// scale a screen's bounds before a window exists on it (e.g. computing default placement before Show()).</summary>
    public static double GetScaleForPoint(double x, double y)
    {
        try
        {
            var monitor = MonitorFromPoint(new POINT { X = (int)x, Y = (int)y }, MONITOR_DEFAULTTONEAREST);
            return ScaleFromMonitor(monitor);
        }
        catch
        {
            return 1.0;
        }
    }

    private static double ScaleFromMonitor(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }
}
