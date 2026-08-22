@echo off
chcp 65001 >nul
title Налаштування CPU оверлею

cd /d "%~dp0"

:: Перевірка прав адміна
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "PY_PATH=python"

echo [/] Очищення старих бібліотек...
%PY_PATH% -m pip uninstall torch torchvision torchaudio onnxruntime onnxruntime-gpu numpy -y >nul 2>&1

echo [/] Встановлення стабільної версії для CPU...
%PY_PATH% -m pip install "numpy<2" --quiet
%PY_PATH% -m pip install torch torchvision torchaudio --index-url https://pytorch.org --no-cache-dir
%PY_PATH% -m pip install onnxruntime==1.17.0 ultralytics opencv-python mss pygame pywin32 keyboard pillow

echo =======================================================
echo [+] НАЛАШТУВАННЯ CPU ЗАВЕРШЕНО!
echo =======================================================
pause
