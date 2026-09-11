using System.Windows;
using System.Windows.Threading;
using TouchdownAlert.Overlay.Contracts;
using TouchdownAlert.Overlay.Services;

namespace TouchdownAlert.Overlay;

/// <summary>
/// Entry point: parses --app, enforces a single running instance via a named mutex (a second launch exits
/// quietly), wires global exception logging (there's no console to see a crash on), and starts the hub
/// connection feeding the one OverlayViewModel/OverlayWindow.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\TouchdownAlert.Overlay.SingleInstance";

    private Mutex? _mutex;
    private FileLog? _log;
    private HubClient? _hubClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _log = new FileLog();
        _log.Info("TouchdownAlert.Overlay starting");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var appUrl = ParseAppUrl(e.Args);
        _log.Info($"App URL: {appUrl}");

        var viewModel = new OverlayViewModel(dispatch: action => Dispatcher.Invoke(action));
        var settingsClient = new OverlaySettingsClient(appUrl, _log);
        var window = new OverlayWindow(viewModel, settingsClient, _log);
        var bannerWindow = new BannerWindow(viewModel, window, _log);

        // Closing the overlay (its "x" when unlocked) ends the app; the banner window follows it down.
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        _hubClient = new HubClient(appUrl, _log);
        _hubClient.StateReceived += viewModel.ApplyState;
        _hubClient.AlertReceived += viewModel.ShowAlert;
        _hubClient.SettingsReceived += dto => viewModel.ApplySettings(dto.Overlay ?? new OverlaySettingsDto());
        _hubClient.ConnectionChanged += connected => viewModel.SetOffline(!connected);

        window.Show();
        bannerWindow.Show();

        _ = _hubClient.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hubClient?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _mutex?.ReleaseMutex();
        _log?.Info("TouchdownAlert.Overlay exiting");
        base.OnExit(e);
    }

    private static string ParseAppUrl(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--app", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            const string prefix = "--app=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[i][prefix.Length..];
            }
        }

        return "http://localhost:5055";
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled dispatcher exception", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled AppDomain exception", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
