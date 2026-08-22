@echo off
chcp 65001 >nul
title Системний ремонт CUDA бібліотек для ONNX

cd /d "%~dp0"

:: 1. Проверка прав Администратора
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Потрібні права Адміністратора! Перезапуск...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "SITE_PACKAGES=%USERPROFILE%\AppData\Local\Programs\Python\Python311\Lib\site-packages"

echo =======================================================
echo [ПРОЦЕС] Пошук та примусове копіювання CUDA у Windows...
echo =======================================================

:: Проверяем, где лежат скачанные питоном библиотеки
if exist "%SITE_PACKAGES%\nvidia\cuda_runtime\bin" (
    echo [+] Знайдено cuda_runtime. Копіюю файли...
    xcopy "%SITE_PACKAGES%\nvidia\cuda_runtime\bin\*.dll" "C:\Windows\System32\" /Y /Q >nul 2>&1
)

if exist "%SITE_PACKAGES%\nvidia\cublas\bin" (
    echo [+] Знайдено cublas. Копіюю файли...
    xcopy "%SITE_PACKAGES%\nvidia\cublas\bin\*.dll" "C:\Windows\System32\" /Y /Q >nul 2>&1
)

if exist "%SITE_PACKAGES%\nvidia\cudnn\bin" (
    echo [+] Знайдено cudnn. Копіюю файли...
    xcopy "%SITE_PACKAGES%\nvidia\cudnn\bin\*.dll" "C:\Windows\System32\" /Y /Q >nul 2>&1
)

if exist "%SITE_PACKAGES%\nvidia\cufft\bin" (
    xcopy "%SITE_PACKAGES%\nvidia\cufft\bin\*.dll" "C:\Windows\System32\" /Y /Q >nul 2>&1
)

if exist "%SITE_PACKAGES%\nvidia\curand\bin" (
    xcopy "%SITE_PACKAGES%\nvidia\curand\bin\*.dll" "C:\Windows\System32\" /Y /Q >nul 2>&1
)

echo =======================================================
echo [+] РЕМОНТ СИСТЕМИ ЗАВЕРШЕНО!
echo [ІНФО] Тепер Windows бачить CUDA файли. Запускаю чит...
echo =======================================================
timeout /t 2 >nul

:: Запускаем наш main_gpu.py
"%USERPROFILE%\AppData\Local\Programs\Python\Python311\python.exe" main_gpu.py
pause
