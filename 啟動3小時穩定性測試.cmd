@echo off
start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0資源穩定性測試.ps1" -DurationHours 3 -IntervalSeconds 60
