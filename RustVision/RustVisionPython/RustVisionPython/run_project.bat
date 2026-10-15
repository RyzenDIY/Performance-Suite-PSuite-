@echo off
chcp 65001 > nul
title Запуск RustVision через VENV

:: Активация виртуального окружения, где стоят правильные библиотеки
if exist "venv\Scripts\activate.bat" (
    call venv\Scripts\activate.bat
    echo [OK] Виртуальное окружение активировано!
) else (
    echo [ОШИБКА] Папка venv не найдена! Сначала запустите установочный батник.
    pause
    exit /b
)

echo [INFO] Запуск главного скрипта main.py...
python main.py
pause
