@echo off
setlocal

title Leader Window Resizer
echo ============================================
echo   LEADER: BUILDING WINDOW RESIZER...
echo ============================================

pushd "%~dp0LeaderWindowResizer"
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    popd
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ============================================
echo   LAUNCHING WINDOW RESIZER...
echo ============================================

bin\Release\net9.0\LeaderWindowResizer.exe %*
set "EXIT_CODE=%ERRORLEVEL%"
popd
pause
exit /b %EXIT_CODE%
