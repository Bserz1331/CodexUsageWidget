@echo off
setlocal
set "DOTNET=%~dp0..\.tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
"%DOTNET%" test "%~dp0CodexUsageWidget.sln" -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false
if errorlevel 1 exit /b 1
"%DOTNET%" publish "%~dp0src\CodexUsageWidget\CodexUsageWidget.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0artifacts\publish" --disable-build-servers -p:UseSharedCompilation=false
if errorlevel 1 exit /b 1
copy /y "%~dp0artifacts\publish\CodexUsageWidget.exe" "%~dp0CodexUsageWidget.exe" >nul
echo Built and tested: %~dp0CodexUsageWidget.exe
endlocal
