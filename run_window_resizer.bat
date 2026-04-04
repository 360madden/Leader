@echo off
title 🛰️ Leader Window Resizer
echo ============================================
echo   🛰️ LEADER: BUILDING WINDOW RESIZER...
echo ============================================
cd LeaderWindowResizer
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo ============================================
echo   📐 LAUNCHING WINDOW RESIZER...
echo ============================================
bin\Release\net9.0\LeaderWindowResizer.exe %*
pause
