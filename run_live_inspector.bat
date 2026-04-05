@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "LAUNCHER=%~nx0"
set "EXIT_CODE=0"

title 🛰️ Leader Live Inspector
echo ========================================
echo   🛰️ LEADER: BUILDING LIVE INSPECTOR...
echo ========================================

pushd "%ROOT%LeaderLiveInspector"
if errorlevel 1 (
    set "EXIT_CODE=1"
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "pushd" "1" "Failed to enter LeaderLiveInspector."
    echo [ERROR] Could not enter the LeaderLiveInspector folder.
    goto :finish
)

set "PUSHD_OK=1"
dotnet build -c Release
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "build" "%EXIT_CODE%" "dotnet build -c Release failed for LeaderLiveInspector."
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    goto :finish
)

echo.
echo ========================================
echo   🔎 LAUNCHING LIVE INSPECTOR...
echo ========================================

bin\Release\net9.0\LeaderLiveInspector.exe %*
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "launch" "%EXIT_CODE%" "LeaderLiveInspector.exe exited with a non-zero code."
)

:finish
if defined PUSHD_OK popd
pause
exit /b %EXIT_CODE%
