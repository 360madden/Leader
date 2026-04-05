@echo off
title 🛰️ Leader Input Probe
echo ============================================
echo   🛰️ LEADER: BUILDING INPUT PROBE...
echo ============================================
cd LeaderInputProbe
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo ============================================
echo   ⌨️ LAUNCHING INPUT PROBE...
echo ============================================
bin\Release\net9.0\LeaderInputProbe.exe %*
pause
