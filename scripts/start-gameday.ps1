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

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProjectDir = Join-Path $repoRoot "src\TouchdownAlert.App"
$appExe = Join-Path $appProjectDir "bin\Release\net10.0-windows\TouchdownAlert.App.exe"
$overlayExe = Join-Path $repoRoot "src\TouchdownAlert.Overlay\bin\Release\net10.0-windows\TouchdownAlert.Overlay.exe"

function Test-Health([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri "$url/api/health" -UseBasicParsing -TimeoutSec 3
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Wait-ForHealth([string]$url, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Health $url) {
            return $true
        }
        Start-Sleep -Seconds 1
    }
    return $false
}

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
    $msedge = $null
    $appPathsKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"
    if (Test-Path $appPathsKey) {
        $msedge = (Get-ItemProperty $appPathsKey).'(default)'
    }
    if (-not $msedge -or -not (Test-Path $msedge)) {
        $candidate = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
        if (Test-Path $candidate) {
            $msedge = $candidate
        }
    }
    if (-not $msedge -or -not (Test-Path $msedge)) {
        $candidate = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
        if (Test-Path $candidate) {
            $msedge = $candidate
        }
    }

    if (-not $msedge) {
        Write-Warning "Could not find msedge.exe - skipping dashboard. Open $AppUrl manually."
    } else {
        Add-Type -AssemblyName System.Windows.Forms
        $screens = [System.Windows.Forms.Screen]::AllScreens
        $target = $screens | Where-Object { -not $_.Primary } | Select-Object -First 1
        if (-not $target) {
            Write-Host "Only one display detected - opening dashboard on the primary display."
            $target = $screens | Where-Object { $_.Primary } | Select-Object -First 1
        }
        $bounds = $target.Bounds

        Write-Host "Opening dashboard on display at ($($bounds.X),$($bounds.Y)) size $($bounds.Width)x$($bounds.Height) ..."
        Start-Process -FilePath $msedge -ArgumentList @(
            "--app=$AppUrl/",
            "--window-position=$($bounds.X),$($bounds.Y)",
            "--window-size=$($bounds.Width),$($bounds.Height)",
            "--start-fullscreen"
        ) | Out-Null
    }
}

Write-Host ""
Write-Host "Game day setup:"
Write-Host "  App:       $AppUrl (health: $(Test-Health $AppUrl))"
Write-Host "  Overlay:   $(if ($NoOverlay) { 'skipped' } else { 'started (or already running)' })"
Write-Host "  Dashboard: $(if ($NoDashboard) { 'skipped' } else { 'opened in Edge app mode on the secondary display' })"
Write-Host ""
Write-Host "To stop everything: powershell -ExecutionPolicy Bypass -File scripts\stop-gameday.ps1"
