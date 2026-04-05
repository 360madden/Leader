@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~1"
set "LAUNCHER=%~2"
set "STAGE=%~3"
set "EXIT_CODE=%~4"
set "DETAIL=%~5"

if "%ROOT%"=="" set "ROOT=%~dp0"
set "LOG_DIR=%ROOT%debug"
set "LOG_FILE=%LOG_DIR%\launcher_failures.csv"

if not exist "%LOG_DIR%" (
    mkdir "%LOG_DIR%" >nul 2>&1
)

if not exist "%LOG_FILE%" (
    >"%LOG_FILE%" echo Timestamp,Launcher,Stage,ExitCode,Detail
)

set "TIMESTAMP=%date% %time%"
>>"%LOG_FILE%" echo "%TIMESTAMP%","%LAUNCHER%","%STAGE%","%EXIT_CODE%","%DETAIL%"

endlocal
exit /b 0
