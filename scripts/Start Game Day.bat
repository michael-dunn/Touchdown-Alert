@echo off
rem Double-click (or pin to the desktop) to start game day: App + overlay + dashboard.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0start-gameday.ps1" %*
