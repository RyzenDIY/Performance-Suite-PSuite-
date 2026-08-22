@echo off
chcp 65001 >nul
title Повне видалення компонентів ШІ-оверлею

cd /d "%~dp0"

:: Перевірка прав Адміністратора
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [СИСТЕМА] Запит прав Адміністратора...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "PY_PATH=%USERPROFILE%\AppData\Local\Programs\Python\Python311\python.exe"
if exist "%PY_PATH%" goto python_found
set "PY_PATH=C:\Program Files\Python311\python.exe"
if exist "%PY_PATH%" goto python_found
python --version >nul 2>&1
if %errorlevel% equ 0 (set "PY_PATH=python") else (goto skip_pip)

:python_found
echo [1/2] Стираємо всі встановлені бібліотеки з пам'яті Python...
"%PY_PATH%" -m pip uninstall torch torchvision torchaudio onnxruntime onnxruntime-gpu numpy ultralytics mss pygame pygetwindow keyboard pywin32 onnxslim pillow -y

:skip_pip
echo.
echo [2/2] Видалення асоціацій файлів та контекстного меню Windows...
:: Очищаємо реєстр Windows від зв'язків з файлами .pt та .onnx, які робив наш софт
assoc .pt= >nul 2>&1
ftype YoloPtModel= >nul 2>&1
assoc .onnx= >nul 2>&1
ftype YoloOnnxModel= >nul 2>&1

echo =======================================================
echo [+] ВИДАЛЕННЯ ЗАВЕРШЕНО!
echo [ІНФО] Папка повністю очищена від системних зв'язків.
echo =======================================================
pause
