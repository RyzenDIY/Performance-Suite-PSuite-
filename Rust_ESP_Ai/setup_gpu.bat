@echo off
chcp 65001 >nul
title Полное автоматическое настраивание CUDA GPU

cd /d "%~dp0"

:: 1. АВТО-ЗАПРОС ПРАВ АДМИНИСТРАТОРА (Для полной установки без блокировок)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [СИСТЕМА] Запит прав Адміністратора...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: Поиск Python 3.11
set "PY_PATH=%USERPROFILE%\AppData\Local\Programs\Python\Python311\python.exe"
if exist "%PY_PATH%" goto python_found
set "PY_PATH=C:\Program Files\Python311\python.exe"
if exist "%PY_PATH%" goto python_found
python --version >nul 2>&1
if %errorlevel% equ 0 (set "PY_PATH=python") else (echo [!] Не знайдено Python 3.11! && pause && exit)

:python_found
echo [+] Робочий Python знайдено: %PY_PATH%

echo.
echo =======================================================
echo [1/4] НАЧИСТО УДАЛЯЕМ ВСЕ СТАРЫЕ КОНФЛИКТУЮЩИЕ ПАКЕТЫ
echo =======================================================
"%PY_PATH%" -m pip uninstall torch torchvision torchaudio onnxruntime onnxruntime-gpu numpy ultralytics -y

echo.
echo =======================================================
echo [2/4] УСТАНОВКА СТАБИЛЬНОГО NUMPY ДЛЯ ЗАЩИТЫ ОТ КРАШЕЙ
echo =======================================================
"%PY_PATH%" -m pip install "numpy<2" --trusted-host pypi.org --trusted-host files.pythonhosted.org

echo.
echo =======================================================
echo [3/4] УСТАНОВКА ОФИЦИАЛЬНОГО PYTORCH CUDA 12.1 (С ЖЕСТКИМ ФИКСИРОВАНИЕМ)
echo =======================================================
:: Флаг --no-cache-dir исключает установку битых или недокачанных старых файлов из кэша
"%PY_PATH%" -m pip install torch torchvision torchaudio --index-url https://pytorch.org --trusted-host download.pytorch.org --no-cache-dir

echo.
echo =======================================================
echo [4/4] ДОУСТАНОВКА РАБОЧИХ МОДУЛЕЙ ШИ И ГРАФИКИ
echo =======================================================
"%PY_PATH%" -m pip install onnxruntime-gpu==1.17.0 ultralytics opencv-python mss pygame pygetwindow keyboard pywin32 --trusted-host pypi.org --trusted-host files.pythonhosted.org

echo.
echo =======================================================
echo [+] ВСЕ КОМПОНЕНТЫ УСПЕШНО УСТАНОВЛЕНЫ!
echo [ПРОЦЕС] Автоматический запуск теста видеокарты...
echo =======================================================
"%PY_PATH%" main_gpu.py
pause
