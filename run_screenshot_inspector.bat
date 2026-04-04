@echo off
title 🛰️ Leader Screenshot Inspector
echo ==============================================
echo   🛰️ LEADER: BUILDING SCREENSHOT INSPECTOR...
echo ==============================================
cd LeaderScreenshotInspector
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo ==============================================
echo   🔎 LAUNCHING SCREENSHOT INSPECTOR...
echo ==============================================
bin\Release\net9.0\LeaderScreenshotInspector.exe %*
pause
