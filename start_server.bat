@echo off
title AudioSniffer Full System Launcher
echo Starting AudioSniffer Full System...
echo ==================================

REM Check if we're running from the correct directory
if not exist "AudioSniffer.sln" (
    echo Error: Please run this script from the AudioSniffer project root directory
    pause
    exit /b 1
)

REM Start Python backend in a separate window
start "AudioSniffer Backend" /MIN cmd /c start_backend.bat

echo Waiting for backend to start...
timeout /t 15 /nobreak >nul

REM Check if backend is running with multiple retries
set retry_count=0
set max_retries=10
set backend_ready=0

:check_backend
curl -s http://localhost:5000/health >nul 2>&1
if %errorlevel% equ 0 (
    set backend_ready=1
    echo Backend is ready and responding!
    goto start_frontend
) else (
    set /a retry_count+=1
    if %retry_count% lss %max_retries% (
        echo Waiting for backend to initialize... (attempt %retry_count%/%max_retries%)
        timeout /t 5 /nobreak >nul
        goto check_backend
    )
    echo Warning: Backend not responding after multiple attempts, will try to start frontend anyway
    echo You may need to wait a bit longer for the backend to fully initialize
)

:start_frontend
echo Starting C# frontend on http://localhost:8000...
echo =============================================

REM Kill any existing processes that might block the port
taskkill /f /im AudioSniffer.exe >nul 2>&1
taskkill /f /im dotnet.exe >nul 2>&1

cd /d %~dp0AudioSniffer

REM Start frontend and wait for it to complete
echo Starting frontend (this window will stay open while the application is running)...
echo.
echo If you want to stop the application, press Ctrl+C in this window.
echo.
dotnet run

echo.
echo Frontend has stopped.
echo.
echo Press any key to exit...
pause >nul