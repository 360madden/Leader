@echo off
title 🛰️ Leader Bridge Launcher
echo ========================================
echo   🛰️ LEADER: BUILDING BRIDGE...
echo ========================================
cd LeaderDecoder
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo ========================================
echo   🚀 LAUNCHING BRIDGE...
echo ========================================
bin\Release\net9.0\LeaderDecoder.exe %*
pause
