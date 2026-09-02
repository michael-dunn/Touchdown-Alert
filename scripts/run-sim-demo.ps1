# run-sim-demo.ps1
# Starts the TouchdownAlert Simulator and the App (pointed at it) each in their own window, then opens
# both web UIs in the default browser. PowerShell 5.1 compatible (no && chaining).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1
#
# Optional parameters:
#   -SimUrl        (default http://localhost:5199)
#   -AppUrl        (default http://localhost:5055)
#   -PollSeconds   (default 5)

param(
    [string]$SimUrl = "http://localhost:5199",
    [string]$AppUrl = "http://localhost:5055",
    [int]$PollSeconds = 5
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Starting simulator on $SimUrl ..."
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$repoRoot'; dotnet run --project src/TouchdownAlert.Simulator -- --Urls=$SimUrl"
)

Write-Host "Starting App on $AppUrl (Leagues:0:BaseUrl=$SimUrl, Polling:IntervalSeconds=$PollSeconds) ..."
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$repoRoot'; dotnet run --project src/TouchdownAlert.App -- --Urls=$AppUrl --Leagues:0:BaseUrl=$SimUrl --Polling:IntervalSeconds=$PollSeconds"
)

Write-Host "Waiting a few seconds for both to come up..."
Start-Sleep -Seconds 6

Write-Host "Opening browser windows..."
Start-Process $SimUrl
Start-Process $AppUrl

Write-Host ""
Write-Host "Simulator console: $SimUrl"
Write-Host "App dashboard:     $AppUrl"
Write-Host ""
Write-Host "Click a TD button on the simulator console (or use its 'Random TD' button) and watch the App react."
