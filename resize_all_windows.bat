@echo off
setlocal

if "%~1"=="" (
    call "%~dp0run_window_resizer.bat" --all --preset 640x360 --inspect
) else (
    call "%~dp0run_window_resizer.bat" --all %*
)

exit /b %ERRORLEVEL%
