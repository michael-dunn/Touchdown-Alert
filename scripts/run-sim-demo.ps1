# run-sim-demo.ps1
# Full dev-time rehearsal of the game-day setup against the Simulator instead of ESPN/Yahoo: starts the
# Simulator and the App (pointed at it) each in their own window, starts the TV overlay, opens the
# dashboard full-screen in Edge app mode on the secondary display, and opens the simulator console in
# the default browser. PowerShell 5.1 compatible (no && chaining).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1 -WithYahoo
#   powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1 -NoOverlay -NoDashboard
#
# Optional parameters:
#   -SimUrl        (default http://localhost:5199)
#   -AppUrl        (default http://localhost:5055)
#   -PollSeconds   (default 5)
#   -WithYahoo     Also configures a second league ("yahoo") pointed at the simulator's Yahoo
#                  emulation, and pre-seeds a fake login (code "sim") so it starts polling
#                  immediately - no browser login step needed for the demo.
#   -NoOverlay     Skip starting the overlay.
#   -NoDashboard   Skip opening the Edge dashboard window (the App URL is still printed).
#
# To stop everything afterwards: powershell -ExecutionPolicy Bypass -File scripts\stop-gameday.ps1
# (it also stops the Simulator). The dev windows and browser windows are left for you to close.

param(
    [string]$SimUrl = "http://localhost:5199",
    [string]$AppUrl = "http://localhost:5055",
    [int]$PollSeconds = 5,
    [switch]$WithYahoo,
    [switch]$NoOverlay,
    [switch]$NoDashboard
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "lib\Common.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot

# --- Simulator ------------------------------------------------------------------------------------

Write-Host "Starting simulator on $SimUrl ..."
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$repoRoot'; dotnet run --project src/TouchdownAlert.Simulator -- --Urls=$SimUrl"
)

# --- App ------------------------------------------------------------------------------------------

$appArgs = "--Urls=$AppUrl --Leagues:0:BaseUrl=$SimUrl --Polling:IntervalSeconds=$PollSeconds"
if ($WithYahoo) {
    $yahooBaseUrl = "$SimUrl/fantasy/v2/"
    $yahooTokenUrl = "$SimUrl/oauth2/get_token"
    $appArgs += " --Leagues:1:Key=yahoo --Leagues:1:Provider=Yahoo --Leagues:1:LeagueId=nfl.l.1" +
                " --Leagues:1:BaseUrl=$yahooBaseUrl" +
                " --Yahoo:TokenUrl=$yahooTokenUrl --Yahoo:ClientId=sim --Yahoo:ClientSecret=sim"
}

Write-Host "Starting App on $AppUrl (Leagues:0:BaseUrl=$SimUrl, Polling:IntervalSeconds=$PollSeconds$(if ($WithYahoo) { ', +Yahoo league' })) ..."
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$repoRoot'; dotnet run --project src/TouchdownAlert.App -- $appArgs"
)

# dotnet run has to build first, so allow a generous window before giving up on the health check.
Write-Host "Waiting up to 60s for $AppUrl/api/health ..."
$appUp = Wait-ForHealth $AppUrl 60
if ($appUp) {
    Write-Host "App is up."
} else {
    Write-Warning "App did not answer health check within 60s - continuing anyway; check the App window."
}

if ($WithYahoo) {
    Write-Host "Pre-seeding a fake Yahoo login (code 'sim') against $AppUrl/api/yahoo/code ..."
    $deadline = (Get-Date).AddSeconds(20)
    $loggedIn = $false
    while ((Get-Date) -lt $deadline -and -not $loggedIn) {
        try {
            Invoke-RestMethod -Method Post "$AppUrl/api/yahoo/code" -ContentType "application/json" -Body '{"code":"sim"}' | Out-Null
            $loggedIn = $true
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    if ($loggedIn) {
        Write-Host "Yahoo login seeded."
    } else {
        Write-Host "Could not reach the App to seed a Yahoo login yet - open $AppUrl/setup/yahoo manually if needed."
    }
}

# --- Overlay (TV, primary display) ----------------------------------------------------------------

if ($NoOverlay) {
    Write-Host "Skipping overlay (-NoOverlay)."
} else {
    $existingOverlay = Get-Process -Name "TouchdownAlert.Overlay" -ErrorAction SilentlyContinue
    if ($existingOverlay) {
        Write-Host "Overlay already running (PID $($existingOverlay.Id -join ',')) - leaving it running."
    } else {
        Write-Host "Starting overlay (dotnet run) pointed at $AppUrl ..."
        Start-Process powershell -ArgumentList @(
            "-NoExit",
            "-Command",
            "cd '$repoRoot'; dotnet run --project src/TouchdownAlert.Overlay -- --app $AppUrl"
        )
    }
}

# --- Dashboard (laptop, secondary display) + simulator console -------------------------------------

$dashboardOpened = $false
if ($NoDashboard) {
    Write-Host "Skipping dashboard (-NoDashboard)."
} else {
    $dashboardOpened = Start-EdgeDashboard $AppUrl
}

Write-Host "Opening simulator console in the default browser..."
Start-Process $SimUrl

$dashboardStatus = "not opened (Edge not found)"
if ($NoDashboard) {
    $dashboardStatus = "skipped"
} elseif ($dashboardOpened) {
    $dashboardStatus = "opened in Edge app mode on the secondary display"
}

Write-Host ""
Write-Host "Sim demo setup:"
Write-Host "  Simulator console: $SimUrl"
Write-Host "  App dashboard:     $AppUrl (health: $(Test-Health $AppUrl))"
Write-Host "  Overlay:           $(if ($NoOverlay) { 'skipped' } else { 'started (or already running)' })"
Write-Host "  Dashboard window:  $dashboardStatus"
Write-Host ""
Write-Host "Click a TD button on the simulator console (or use its 'Random TD' button) and watch the dashboard and overlay react."
Write-Host "To stop everything: powershell -ExecutionPolicy Bypass -File scripts\stop-gameday.ps1"
