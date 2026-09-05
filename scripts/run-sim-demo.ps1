# run-sim-demo.ps1
# Starts the TouchdownAlert Simulator and the App (pointed at it) each in their own window, then opens
# both web UIs in the default browser. PowerShell 5.1 compatible (no && chaining).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1 -WithYahoo
#
# Optional parameters:
#   -SimUrl        (default http://localhost:5199)
#   -AppUrl        (default http://localhost:5055)
#   -PollSeconds   (default 5)
#   -WithYahoo     Also configures a second league ("yahoo") pointed at the simulator's Yahoo
#                  emulation, and pre-seeds a fake login (code "sim") so it starts polling
#                  immediately - no browser login step needed for the demo.

param(
    [string]$SimUrl = "http://localhost:5199",
    [string]$AppUrl = "http://localhost:5055",
    [int]$PollSeconds = 5,
    [switch]$WithYahoo
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Starting simulator on $SimUrl ..."
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$repoRoot'; dotnet run --project src/TouchdownAlert.Simulator -- --Urls=$SimUrl"
)

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

Write-Host "Waiting a few seconds for both to come up..."
Start-Sleep -Seconds 6

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

Write-Host "Opening browser windows..."
Start-Process $SimUrl
Start-Process $AppUrl

Write-Host ""
Write-Host "Simulator console: $SimUrl"
Write-Host "App dashboard:     $AppUrl"
Write-Host ""
Write-Host "Click a TD button on the simulator console (or use its 'Random TD' button) and watch the App react."
