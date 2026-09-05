using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TouchdownAlert.Overlay.Contracts;
using TouchdownAlert.Overlay.Interop;
using TouchdownAlert.Overlay.Services;

namespace TouchdownAlert.Overlay;

/// <summary>
/// The always-on-top overlay window. Owns Win32 style application (click-through/locked vs. draggable/unlocked),
/// default placement on the configured display, live-applying scale/opacity/locked/enabled from settings, and
/// saving a new position back to the App after a drag. All actual UI state lives in <see cref="OverlayViewModel"/>;
/// this class is the thin WPF-specific glue on top of it.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly OverlaySettingsClient _settingsClient;
    private readonly FileLog _log;
    private IntPtr _hwnd;
    private bool _sourceInitialized;

    public OverlayWindow(OverlayViewModel viewModel, OverlaySettingsClient settingsClient, FileLog log)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsClient = settingsClient;
        _log = log;
        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _sourceInitialized = true;
        ApplyLockStyle();
        ApplyVisibility();
        ApplyScale();
        PositionWindow();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OverlayViewModel.Settings))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            ApplyLockStyle();
            ApplyVisibility();
            ApplyScale();
            PositionWindow();
        });
    }

    private void ApplyLockStyle()
    {
        if (!_sourceInitialized)
        {
            return;
        }

        WindowStyles.ApplyExStyle(_hwnd, _viewModel.Settings.Locked);
    }

    private void ApplyVisibility()
    {
        Visibility = _viewModel.Settings.Enabled ? Visibility.Visible : Visibility.Hidden;
    }

    private void ApplyScale()
    {
        var scale = _viewModel.Settings.Scale <= 0 ? 1.0 : _viewModel.Settings.Scale;
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
    }

    /// <summary>Applies saved X/Y if present, else the default top-right placement on the configured display.</summary>
    private void PositionWindow()
    {
        var settings = _viewModel.Settings;
        if (settings.X.HasValue && settings.Y.HasValue)
        {
            Left = settings.X.Value;
            Top = settings.Y.Value;
            return;
        }

        // Defer until layout has run so ActualWidth/Height (needed for top-right math) are known.
        Dispatcher.InvokeAsync(() =>
        {
            var screens = ScreenEnumerator.GetScreens();
            var screen = ScreenPlacement.SelectScreen(screens, settings.Display);
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var (left, top) = ScreenPlacement.DefaultTopRight(screen, width, height);
            Left = left;
            Top = top;
        }, DispatcherPriority.Loaded);
    }

    private void Caption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Settings.Locked)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if called outside a mouse-down handler in some edge cases; ignore.
            return;
        }

        _ = SaveCurrentPositionAsync();
    }

    private void CloseButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private async Task SaveCurrentPositionAsync()
    {
        var current = _viewModel.Settings;
        var overlay = new OverlaySettingsDto
        {
            X = Left,
            Y = Top,
            Display = current.Display,
            Scale = current.Scale,
            Opacity = current.Opacity,
            Locked = current.Locked,
            Enabled = current.Enabled,
        };

        var ok = await _settingsClient.SaveOverlayAsync(overlay);
        _log.Info(ok
            ? $"Saved overlay position ({overlay.X:0}, {overlay.Y:0})"
            : "Failed to save overlay position (will retry on next drag)");
    }
}
