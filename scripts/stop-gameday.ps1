# stop-gameday.ps1
# Stops the TouchdownAlert App and Overlay processes started by start-gameday.ps1. Only ever touches
# processes by these two exact names - never Edge/Firefox, so the game keeps playing.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\stop-gameday.ps1

$ErrorActionPreference = "Continue"

$names = @("TouchdownAlert.App", "TouchdownAlert.Overlay")

foreach ($name in $names) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Host "$name.exe is not running."
        continue
    }

    foreach ($proc in $procs) {
        Write-Host "Stopping $name.exe (PID $($proc.Id)) ..."
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Done. The dashboard browser window and the game stream were left alone."
