@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "LAUNCHER=%~nx0"
set "EXIT_CODE=0"

title 🛰️ Leader Trace Bundle
echo ============================================
echo   🛰️ LEADER: BUILDING TRACE BUNDLE...
echo ============================================

pushd "%ROOT%LeaderTraceBundle"
if errorlevel 1 (
    set "EXIT_CODE=1"
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "pushd" "1" "Failed to enter LeaderTraceBundle."
    echo [ERROR] Could not enter the LeaderTraceBundle folder.
    goto :finish
)

set "PUSHD_OK=1"
if not exist "debug" (
    mkdir "debug" >nul 2>&1
    if errorlevel 1 (
        set "EXIT_CODE=1"
        call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "mkdir_debug" "1" "Failed to create the debug folder in LeaderTraceBundle."
        echo [ERROR] Could not create the LeaderTraceBundle debug folder.
        goto :finish
    )
)

dotnet build -c Release
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "build" "%EXIT_CODE%" "dotnet build -c Release failed for LeaderTraceBundle."
    echo [ERROR] Build failed. Please check your .NET 9 SDK installation.
    goto :finish
)

echo.
echo ============================================
echo   📦 LAUNCHING TRACE BUNDLE...
echo ============================================

bin\Release\net9.0\LeaderTraceBundle.exe %*
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "launch" "%EXIT_CODE%" "LeaderTraceBundle.exe exited with a non-zero code."
)

:finish
if defined PUSHD_OK popd
pause
exit /b %EXIT_CODE%
