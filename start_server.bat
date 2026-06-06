@echo off
title AudioSniffer System Launcher

echo Starting AudioSniffer System (HTTPS)...
echo ==================================

if not exist "AudioSniffer.sln" (
    echo Error: Please run this script from the AudioSniffer project root directory
    pause
    exit /b 1
)

start "AudioSniffer Backend" /MIN cmd /c start_backend.bat

echo Waiting for backend...
timeout /t 15 /nobreak >nul

cd /d "%~dp0AudioSniffer"
echo Starting frontend on HTTPS...
dotnet run --urls "https://localhost:8000;http://localhost:8000"

echo.
echo If you want to stop - press Ctrl+C
pause