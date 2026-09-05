# start-gameday.ps1
# One-shot launcher for the agreed TV setup: the App host, the always-on-top overlay on the TV
# (primary display), and the web dashboard opened full-screen on the laptop (secondary display) in
# Edge app mode. PowerShell 5.1 compatible (no && chaining). Safe to re-run: skips anything that's
# already up.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\start-gameday.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\start-gameday.ps1 -NoDashboard
#   powershell -ExecutionPolicy Bypass -File scripts\start-gameday.ps1 -NoOverlay
#   powershell -ExecutionPolicy Bypass -File scripts\start-gameday.ps1 -AppUrl http://localhost:5099
#
# Optional parameters:
#   -AppUrl        (default http://localhost:5055)
#   -NoDashboard   Skip opening the Edge dashboard window.
#   -NoOverlay     Skip starting the overlay.

param(
    [string]$AppUrl = "http://localhost:5055",
    [switch]$NoDashboard,
    [switch]$NoOverlay
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "lib\Common.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProjectDir = Join-Path $repoRoot "src\TouchdownAlert.App"
$appExe = Join-Path $appProjectDir "bin\Release\net10.0-windows\TouchdownAlert.App.exe"
$overlayExe = Join-Path $repoRoot "src\TouchdownAlert.Overlay\bin\Release\net10.0-windows\TouchdownAlert.Overlay.exe"

# --- App host -----------------------------------------------------------------------------------

if (Test-Health $AppUrl) {
    Write-Host "App already answering at $AppUrl - leaving it running."
} else {
    if (-not (Test-Path $appExe)) {
        Write-Host "App exe not found at $appExe - building (dotnet build -c Release) ..."
        Push-Location $repoRoot
        try {
            dotnet build -c Release
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet build failed with exit code $LASTEXITCODE"
            }
        } finally {
            Pop-Location
        }
    }

    Write-Host "Starting App from $appExe (WorkingDirectory=$appProjectDir) ..."
    Start-Process -FilePath $appExe -WorkingDirectory $appProjectDir -ArgumentList @("--urls=$AppUrl") | Out-Null

    Write-Host "Waiting up to 20s for $AppUrl/api/health ..."
    if (Wait-ForHealth $AppUrl 20) {
        Write-Host "App is up."
    } else {
        Write-Warning "App did not answer health check within 20s - continuing anyway; check the App window/log."
    }
}

# --- Overlay --------------------------------------------------------------------------------------

if ($NoOverlay) {
    Write-Host "Skipping overlay (-NoOverlay)."
} else {
    $existingOverlay = Get-Process -Name "TouchdownAlert.Overlay" -ErrorAction SilentlyContinue
    if ($existingOverlay) {
        Write-Host "Overlay already running (PID $($existingOverlay.Id -join ',')) - leaving it running."
    } elseif (-not (Test-Path $overlayExe)) {
        Write-Warning "Overlay exe not found at $overlayExe - run 'dotnet build -c Release' first. Skipping overlay."
    } else {
        Write-Host "Starting overlay from $overlayExe --app $AppUrl ..."
        Start-Process -FilePath $overlayExe -ArgumentList @("--app", $AppUrl) | Out-Null
    }
}

# --- Dashboard on the secondary (laptop) display, Edge app mode, full screen ----------------------

if ($NoDashboard) {
    Write-Host "Skipping dashboard (-NoDashboard)."
} else {
    Start-EdgeDashboard $AppUrl | Out-Null
}

Write-Host ""
Write-Host "Game day setup:"
Write-Host "  App:       $AppUrl (health: $(Test-Health $AppUrl))"
Write-Host "  Overlay:   $(if ($NoOverlay) { 'skipped' } else { 'started (or already running)' })"
Write-Host "  Dashboard: $(if ($NoDashboard) { 'skipped' } else { 'opened in Edge app mode on the secondary display' })"
Write-Host ""
Write-Host "To stop everything: powershell -ExecutionPolicy Bypass -File scripts\stop-gameday.ps1"
