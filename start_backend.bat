@echo off
setlocal enabledelayedexpansion
title AudioSniffer Backend
echo Starting AudioSniffer Backend...
echo ======================================

cd /d %~dp0AudioSniffer\Core

REM ── Python check ──────────────────────────────────────────────────────────────
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Python is not installed or not in PATH.
    echo         Download: https://www.python.org/downloads/
    pause
    exit /b 1
)

REM ── Virtual environment ───────────────────────────────────────────────────────
if not exist "..\..\venv" (
    echo [SETUP] Creating virtual environment...
    python -m venv ..\..\venv
)

call ..\..\venv\Scripts\activate.bat

REM ── pip upgrade ───────────────────────────────────────────────────────────────
echo [SETUP] Upgrading pip...
python -m pip install --upgrade pip --quiet

REM ── Python requirements ───────────────────────────────────────────────────────
if not exist "..\..\venv\requirements_installed" (
    echo [SETUP] Installing Python requirements...
    pip install -r python_requirements.txt
    if %errorlevel% neq 0 (
        echo [ERROR] Failed to install Python requirements.
        pause
        exit /b 1
    )
    echo. > ..\..\venv\requirements_installed
    echo [SETUP] Python requirements installed.
)

REM ── FFmpeg check & auto-install ───────────────────────────────────────────────
ffmpeg -version >nul 2>&1
if %errorlevel% neq 0 (
    echo [SETUP] FFmpeg not found in PATH. Attempting auto-install...

    REM Check known locations first
    set FFMPEG_FOUND=0
    for %%P in (
        "C:\ffmpeg\bin\ffmpeg.exe"
        "C:\Program Files\ffmpeg\bin\ffmpeg.exe"
        "C:\tools\ffmpeg\bin\ffmpeg.exe"
        "C:\ProgramData\chocolatey\bin\ffmpeg.exe"
    ) do (
        if exist %%P (
            set FFMPEG_FOUND=1
            echo [SETUP] FFmpeg found at %%P
            for %%D in (%%~dpP) do set FFMPEG_BIN=%%D
        )
    )

    if !FFMPEG_FOUND! equ 0 (
        REM Try winget (Windows 11 / updated Win10)
        winget --version >nul 2>&1
        if !errorlevel! equ 0 (
            echo [SETUP] Installing FFmpeg via winget...
            winget install --id Gyan.FFmpeg --silent --accept-package-agreements --accept-source-agreements
            if !errorlevel! equ 0 (
                set FFMPEG_BIN=C:\Program Files\FFmpeg\bin
                set FFMPEG_FOUND=1
                echo [SETUP] FFmpeg installed via winget.
            )
        )
    )

    if !FFMPEG_FOUND! equ 0 (
        REM Try choco
        choco --version >nul 2>&1
        if !errorlevel! equ 0 (
            echo [SETUP] Installing FFmpeg via Chocolatey...
            choco install ffmpeg -y
            if !errorlevel! equ 0 (
                set FFMPEG_FOUND=1
                echo [SETUP] FFmpeg installed via Chocolatey.
            )
        )
    )

    if !FFMPEG_FOUND! equ 0 (
        REM Manual download fallback
        echo [SETUP] Downloading FFmpeg manually...
        if not exist "C:\ffmpeg" mkdir "C:\ffmpeg"
        powershell -NoProfile -ExecutionPolicy Bypass -Command ^
            "$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip';" ^
            "$zip = 'C:\ffmpeg\ffmpeg.zip';" ^
            "Write-Host 'Downloading FFmpeg...';" ^
            "Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing;" ^
            "Write-Host 'Extracting...';" ^
            "Expand-Archive -Path $zip -DestinationPath 'C:\ffmpeg\' -Force;" ^
            "$inner = Get-ChildItem 'C:\ffmpeg\' -Directory | Where-Object { $_.Name -like 'ffmpeg*' } | Select-Object -First 1;" ^
            "if ($inner) { Copy-Item -Path ($inner.FullName + '\bin\*') -Destination 'C:\ffmpeg\bin\' -Recurse -Force };" ^
            "Remove-Item $zip -Force;" ^
            "Write-Host 'Done.'"
        if exist "C:\ffmpeg\bin\ffmpeg.exe" (
            set FFMPEG_BIN=C:\ffmpeg\bin
            set FFMPEG_FOUND=1
            echo [SETUP] FFmpeg downloaded to C:\ffmpeg\bin
        ) else (
            echo [WARNING] Automatic FFmpeg install failed.
            echo           AAC files will not be supported.
            echo           To fix: download ffmpeg from https://ffmpeg.org/download.html
            echo           and extract to C:\ffmpeg\bin\
        )
    )

    if !FFMPEG_FOUND! equ 1 (
        REM Add to PATH for this session
        set PATH=!FFMPEG_BIN!;!PATH!
        REM Persist to user PATH
        powershell -NoProfile -ExecutionPolicy Bypass -Command ^
            "$p = [Environment]::GetEnvironmentVariable('PATH','User');" ^
            "if ($p -notlike '*!FFMPEG_BIN!*') {" ^
            "  [Environment]::SetEnvironmentVariable('PATH', $p + ';!FFMPEG_BIN!', 'User');" ^
            "  Write-Host '[SETUP] FFmpeg added to user PATH permanently.' }"
    )
) else (
    echo [OK] FFmpeg is available.
)

REM ── Start server ──────────────────────────────────────────────────────────────
echo.
echo [OK] Starting FastAPI server on http://localhost:5000...
echo      Press Ctrl+C to stop.
echo.

python python_run.py