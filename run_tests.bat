@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "LAUNCHER=%~nx0"
set "EXIT_CODE=0"

title 🧪 Leader Bridge — Roundtrip Tests
echo ========================================
echo   🧪 LEADER: RUNNING ENCODE/DECODE TESTS
echo ========================================

pushd "%ROOT%LeaderDecoder"
if errorlevel 1 (
    set "EXIT_CODE=1"
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "pushd" "1" "Failed to enter LeaderDecoder."
    echo [ERROR] Could not enter the LeaderDecoder folder.
    goto :finish
)

set "PUSHD_OK=1"
if not exist "bin\Release\net9.0\LeaderDecoder.exe" (
    set "EXIT_CODE=2"
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "missing_binary" "2" "LeaderDecoder.exe was not found. Run run_bridge.bat first or build LeaderDecoder."
    echo [ERROR] Missing bin\Release\net9.0\LeaderDecoder.exe. Run run_bridge.bat first.
    goto :finish
)

bin\Release\net9.0\LeaderDecoder.exe --test %*
set "EXIT_CODE=%ERRORLEVEL%"
if %EXIT_CODE% NEQ 0 (
    call "%ROOT%log_launcher_failure.bat" "%ROOT%" "%LAUNCHER%" "launch" "%EXIT_CODE%" "LeaderDecoder.exe --test exited with a non-zero code."
)

:finish
if defined PUSHD_OK popd
pause
exit /b %EXIT_CODE%
