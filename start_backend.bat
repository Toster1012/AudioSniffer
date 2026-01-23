@echo off
echo Starting AudioSniffer Python Backend...
echo ======================================

cd /d %~dp0AudioSniffer\Core

REM Check if Python is available
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: Python is not installed or not in PATH
    pause
    exit /b 1
)

REM Check if virtual environment exists
if not exist "..\..\venv" (
    echo Creating virtual environment...
    python -m venv ..\..\venv
)

REM Activate virtual environment
call ..\..\venv\Scripts\activate.bat

REM Install requirements if needed
if not exist "..\..\venv\requirements_installed" (
    echo Installing Python requirements...
    pip install -r python_requirements.txt
    echo. > ..\..\venv\requirements_installed
)

echo Starting FastAPI server on http://localhost:5000...
echo Press Ctrl+C to stop the server
echo.

python python_run.py