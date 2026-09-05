# scripts/

- **run-sim-demo.ps1** - dev-time rehearsal of the full game-day setup against the Simulator: starts the
  simulator and the App (pointed at it, `dotnet run`) in dev windows, waits for `/api/health`, starts
  the overlay (`dotnet run`, unless already running), opens the dashboard full-screen on the secondary
  (laptop) display in Edge app mode, and opens the simulator console in the default browser.
  Switches: `-WithYahoo`, `-NoOverlay`, `-NoDashboard`, `-SimUrl <url>`, `-AppUrl <url>`, `-PollSeconds <n>`.
- **start-gameday.ps1** - game-day launcher for the real TV setup: starts the App (if not already
  answering `/api/health`), starts the overlay (unless already running), and opens the web dashboard
  full-screen on the secondary (laptop) display in Edge app mode. Safe to re-run - skips anything
  already up. Switches: `-NoDashboard`, `-NoOverlay`, `-AppUrl <url>`.
  Run with `powershell -ExecutionPolicy Bypass -File scripts\start-gameday.ps1`, or double-click
  **Start Game Day.bat** (pin that to the desktop/taskbar for one-click game day).
- **stop-gameday.ps1** - stops `TouchdownAlert.App.exe`, `TouchdownAlert.Overlay.exe` and
  `TouchdownAlert.Simulator.exe` by name only. Never touches Edge or Firefox, so the game keeps
  playing on the TV. Works for both the game-day and the sim-demo setups.
- **Start Game Day.bat** - double-click shortcut for `start-gameday.ps1` (runs with
  `-ExecutionPolicy Bypass` so no execution-policy setup is needed on the machine).
- **lib/Common.ps1** - helpers shared by the launchers: health check/wait, Edge discovery, dashboard
  display selection and the Edge app-mode dashboard window.

`start-gameday.ps1` assumes `dotnet build -c Release` has already been run for the solution (or does it
automatically if the App's exe is missing). `run-sim-demo.ps1` uses `dotnet run`, so it builds Debug on
the fly.
