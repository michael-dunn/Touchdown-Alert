using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TouchdownAlert.Overlay.Interop;
using TouchdownAlert.Overlay.Services;

namespace TouchdownAlert.Overlay;

/// <summary>
/// The touchdown banner: a transparent, click-through, always-on-top strip spanning the full width of the
/// configured display, flush with its top edge. It is always present (so a banner can slide in the instant the
/// App sends the alert, in step with the sound) but paints nothing until <see cref="OverlayViewModel.BannerVisible"/>
/// is true. While a banner is up its text scrolls right-to-left as a continuous ticker: the text is repeated
/// enough times to fill the strip and shifted by exactly one repeat per animation cycle, so the loop is seamless.
/// Follows the overlay's display/scale/enabled settings; position is never saved, it is always derived.
/// </summary>
public partial class BannerWindow : Window
{
    /// <summary>Ticker speed in DIPs per second (before the overlay scale is applied).</summary>
    private const double TickerSpeed = 240;

    /// <summary>Gap between repeats of the text, in DIPs.</summary>
    private const double TickerGap = 96;

    private readonly OverlayViewModel _viewModel;
    private readonly Window _overlayWindow;
    private readonly FileLog _log;
    private IntPtr _hwnd;
    private bool _sourceInitialized;

    public BannerWindow(OverlayViewModel viewModel, Window overlayWindow, FileLog log)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _overlayWindow = overlayWindow;
        _log = log;
        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _sourceInitialized = true;
        // Always click-through: the banner is never interactive, whatever the overlay's lock state.
        WindowStyles.ApplyExStyle(_hwnd, locked: true);
        ApplySettings();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OverlayViewModel.Settings):
                Dispatcher.Invoke(ApplySettings);
                break;
            case nameof(OverlayViewModel.CurrentBanner):
                Dispatcher.Invoke(RebuildTicker);
                break;
        }
    }

    private void ApplySettings()
    {
        if (!_sourceInitialized)
        {
            return;
        }

        var settings = _viewModel.Settings;

        var scale = settings.Scale <= 0 ? 1.0 : settings.Scale;
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;

        Visibility = settings.Enabled ? Visibility.Visible : Visibility.Hidden;

        // The overlay applies the same settings change (and its deferred default placement) at Loaded priority,
        // so wait for that before asking which monitor it ended up on.
        Dispatcher.InvokeAsync(PlaceOnOverlayScreen, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the strip along the top of the screen the overlay tiles are on (falls back to the display setting).</summary>
    private void PlaceOnOverlayScreen()
    {
        var overlayHandle = new WindowInteropHelper(_overlayWindow).Handle;
        var screen = ScreenEnumerator.GetScreenForWindow(overlayHandle)
            ?? ScreenPlacement.SelectScreen(ScreenEnumerator.GetScreens(), _viewModel.Settings.Display);

        var (left, top, width) = ScreenPlacement.TopStrip(screen);
        Left = left;
        Top = top;
        Width = width;
        var all = string.Join("; ", ScreenEnumerator.GetScreens().Select(s => $"({s.X:0},{s.Y:0}) {s.Width:0}x{s.Height:0} @{s.Scale:0.00}{(s.IsPrimary ? " primary" : "")}"));
        _log.Info($"Banner placed on screen at ({screen.X:0},{screen.Y:0}) {screen.Width:0}x{screen.Height:0} (primary: {screen.IsPrimary}); " +
                  $"overlay window at ({_overlayWindow.Left:0},{_overlayWindow.Top:0}) hwnd {overlayHandle}; screens: {all}");
    }

    /// <summary>
    /// Replaces the ticker content for the current banner (or clears it when there is none) and starts the
    /// scroll. Widths are only known after a layout pass, so the repeats and the animation are set up at
    /// Loaded priority once the first copy has been measured.
    /// </summary>
    private void RebuildTicker()
    {
        TickerShift.BeginAnimation(TranslateTransform.XProperty, null);
        TickerShift.X = 0;
        Ticker.Children.Clear();

        var banner = _viewModel.CurrentBanner;
        if (banner is null)
        {
            return;
        }

        var text = banner.DisplayText + (banner.IsTest ? " · TEST" : "");
        var first = MakeTickerItem(text);
        Ticker.Children.Add(first);

        Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_viewModel.CurrentBanner, banner))
            {
                return; // superseded before layout ran
            }

            var itemWidth = first.ActualWidth;
            var viewportWidth = TickerViewport.ActualWidth;
            if (itemWidth <= 0 || viewportWidth <= 0)
            {
                return;
            }

            // Enough copies that the viewport is always covered, plus one to scroll in from the right.
            var copies = (int)Math.Ceiling(viewportWidth / itemWidth) + 1;
            _log.Info($"Banner ticker: item {itemWidth:0} viewport {viewportWidth:0} strip {Strip.ActualWidth:0} window {ActualWidth:0} (Width {Width:0}) copies {copies}");
            for (var i = 1; i < copies; i++)
            {
                Ticker.Children.Add(MakeTickerItem(text));
            }

            var cycle = new DoubleAnimation(0, -itemWidth, TimeSpan.FromSeconds(itemWidth / TickerSpeed))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            TickerShift.BeginAnimation(TranslateTransform.XProperty, cycle);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>One repeat of the ticker: the banner text followed by a separator dot and gap.</summary>
    private static FrameworkElement MakeTickerItem(string text)
    {
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "●",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(TickerGap / 2, 0, TickerGap / 2, 0),
        });

        return panel;
    }
}
