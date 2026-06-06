@echo off
setlocal enabledelayedexpansion
title AudioSniffer Backend
echo Starting AudioSniffer Backend...
echo ======================================

REM ── Переходим в папку Core (там лежит python_run.py и app/) ──────────────────
cd /d "%~dp0Core"
if %errorlevel% neq 0 (
    echo [ERROR] Папка Core не найдена рядом с bat-файлом.
    pause
    exit /b 1
)

REM ── Python check ──────────────────────────────────────────────────────────────
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Python не установлен или не прописан в PATH.
    echo         Скачайте: https://www.python.org/downloads/
    pause
    exit /b 1
)

REM ── Virtual environment ───────────────────────────────────────────────────────
if not exist "..\venv" (
    echo [SETUP] Создание виртуального окружения...
    python -m venv ..\venv
)

call ..\venv\Scripts\activate.bat

REM ── pip upgrade ───────────────────────────────────────────────────────────────
echo [SETUP] Обновление pip...
python -m pip install --upgrade pip --quiet

REM ── Python requirements ───────────────────────────────────────────────────────
if not exist "..\venv\requirements_installed" (
    echo [SETUP] Установка зависимостей...
    pip install -r python_requirements.txt
    if %errorlevel% neq 0 (
        echo [ERROR] Не удалось установить зависимости.
        pause
        exit /b 1
    )
    echo. > ..\venv\requirements_installed
    echo [SETUP] Зависимости установлены.
)

REM ── FFmpeg check ──────────────────────────────────────────────────────────────
ffmpeg -version >nul 2>&1
if %errorlevel% neq 0 (
    echo [WARNING] FFmpeg не найден. AAC-файлы не будут поддерживаться.
    echo           Скачайте ffmpeg с https://ffmpeg.org/download.html
) else (
    echo [OK] FFmpeg доступен.
)

REM ── Start server ──────────────────────────────────────────────────────────────
echo.
echo [OK] Запуск FastAPI сервера на http://localhost:5000...
echo      Нажмите Ctrl+C для остановки.
echo.

python python_run.py
