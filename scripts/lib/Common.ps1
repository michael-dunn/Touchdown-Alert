# Common.ps1
# Helpers shared by run-sim-demo.ps1 and start-gameday.ps1. Dot-source it:
#   . (Join-Path $PSScriptRoot "lib\Common.ps1")
# PowerShell 5.1 compatible (no && chaining, no ternaries).

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

# Locates msedge.exe via the App Paths registry key, then the usual install folders. Returns $null if not found.
function Find-Edge {
    $appPathsKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"
    if (Test-Path $appPathsKey) {
        $fromRegistry = (Get-ItemProperty $appPathsKey).'(default)'
        if ($fromRegistry -and (Test-Path $fromRegistry)) {
            return $fromRegistry
        }
    }
    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }
    return $null
}

# The agreed layout puts the dashboard on the secondary (laptop) display; falls back to the primary
# display when only one is connected.
function Get-DashboardScreen {
    Add-Type -AssemblyName System.Windows.Forms
    $screens = [System.Windows.Forms.Screen]::AllScreens
    $target = $screens | Where-Object { -not $_.Primary } | Select-Object -First 1
    if (-not $target) {
        Write-Host "Only one display detected - opening dashboard on the primary display."
        $target = $screens | Where-Object { $_.Primary } | Select-Object -First 1
    }
    return $target
}

# Opens the dashboard at $appUrl in Edge app mode, full screen, on the dashboard display.
# Returns $true when a window was launched, $false when Edge could not be found.
function Start-EdgeDashboard([string]$appUrl) {
    $msedge = Find-Edge
    if (-not $msedge) {
        Write-Warning "Could not find msedge.exe - skipping dashboard. Open $appUrl manually."
        return $false
    }

    $bounds = (Get-DashboardScreen).Bounds
    Write-Host "Opening dashboard on display at ($($bounds.X),$($bounds.Y)) size $($bounds.Width)x$($bounds.Height) ..."
    Start-Process -FilePath $msedge -ArgumentList @(
        "--app=$appUrl/",
        "--window-position=$($bounds.X),$($bounds.Y)",
        "--window-size=$($bounds.Width),$($bounds.Height)",
        "--start-fullscreen"
    ) | Out-Null
    return $true
}
