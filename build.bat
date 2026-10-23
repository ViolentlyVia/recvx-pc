@echo off
rem Builds recvx-pc and its randomizer in one go (see build.ps1). Double-click it, or run it
rem from a command prompt with build.ps1's options (-NoRando, -Yes, -Version, -Dir).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set code=%errorlevel%
echo.
pause
exit /b %code%
