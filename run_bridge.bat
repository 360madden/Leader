@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "LAUNCHER=%~nx0"
set "EXIT_CODE=0"

title 🛰️ Leader Bridge Launcher
echo ========================================
echo   🛰️ LEADER: BUILDING BRIDGE...
echo ========================================

pushd "%ROOT%LeaderDecoder"
if errorlevel 1 (
    set "EXIT_CODE=1"
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "pushd" "1" "Failed to enter LeaderDecoder."
    echo [ERROR] Could not enter the LeaderDecoder folder.
    goto :finish
)

set "PUSHD_OK=1"
if not exist "debug" (
    mkdir "debug" >nul 2>&1
    if errorlevel 1 (
        set "EXIT_CODE=1"
        call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "mkdir_debug" "1" "Failed to create the debug folder in LeaderDecoder."
        echo [ERROR] Could not create the LeaderDecoder debug folder.
        goto :finish
    )
)
dotnet build -c Release
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "build" "%EXIT_CODE%" "dotnet build -c Release failed for LeaderDecoder."
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    goto :finish
)

echo.
echo ========================================
echo   🚀 LAUNCHING BRIDGE...
echo ========================================

bin\Release\net9.0\LeaderDecoder.exe %*
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "launch" "%EXIT_CODE%" "LeaderDecoder.exe exited with a non-zero code."
)

:finish
if defined PUSHD_OK popd
pause
exit /b %EXIT_CODE%
