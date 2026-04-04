@echo off
title 🛰️ Leader Live Inspector
echo ========================================
echo   🛰️ LEADER: BUILDING LIVE INSPECTOR...
echo ========================================
cd LeaderLiveInspector
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo ========================================
echo   🔎 LAUNCHING LIVE INSPECTOR...
echo ========================================
bin\Release\net9.0\LeaderLiveInspector.exe %*
pause
