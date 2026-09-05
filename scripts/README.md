# scripts/

- **run-sim-demo.ps1** - dev-time demo: simulator + App in dev windows, browser tabs. See its own header comment.
- **start-gameday.ps1** - game-day launcher for the real TV setup: starts the App (if not already
  answering `/api/health`), starts the overlay (unless already running), and opens the web dashboard
  full-screen on the secondary (laptop) display in Edge app mode. Safe to re-run - skips anything
  already up. Switches: `-NoDashboard`, `-NoOverlay`, `-AppUrl <url>`.
  Run with `powershell -ExecutionPolicy Bypass -File scripts\start-gameday.ps1`, or double-click
  **Start Game Day.bat** (pin that to the desktop/taskbar for one-click game day).
- **stop-gameday.ps1** - stops `TouchdownAlert.App.exe` and `TouchdownAlert.Overlay.exe` by name only.
  Never touches Edge or Firefox, so the game keeps playing on the TV.
- **Start Game Day.bat** - double-click shortcut for `start-gameday.ps1` (runs with
  `-ExecutionPolicy Bypass` so no execution-policy setup is needed on the machine).

Both `start-gameday.ps1` and `stop-gameday.ps1` assume `dotnet build -c Release` has already been run
for the solution (or been done by the launcher automatically if the App's exe is missing).
