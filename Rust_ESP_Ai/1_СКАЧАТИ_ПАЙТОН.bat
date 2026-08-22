@echo off
chcp 65001 >nul
title Автоматичне скачування Python + Реєстрація "Відкрити за допомогою"

cd /d "%~dp0"

:: 1. ПЕРЕВІРКА ПРАВ АДМІНІСТРАТОРА (Обов'язково для додавання в контекстне меню Windows)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [СИСТЕМА] Запит прав Адміністратора...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo [1/3] Скачування офіційного інсталятора Python 3.11.9 з сайту python.org...
powershell -Command "Invoke-WebRequest -Uri 'https://python.org' -OutFile 'python_3.11_installer.exe'"

if not exist "python_3.11_installer.exe" (
    echo [ПОМИЛКА] Не вдалося завантажити інсталятор! Перевір інтернет-з'єднання.
    pause
    exit /b 1
)

echo.
echo [2/3] Встановлення Python у тихий режим (зачекайте близько хвилини)...
:: quiet - тихе встановлення, InstallAllUsers - для всіх користувачів, PrependPath - авто-додавання в змінні PATH
start /wait python_3.11_installer.exe /quiet InstallAllUsers=1 PrependPath=1 Include_test=0

echo [ПРОЦЕС] Очищення тимчасових інсталяційних файлів...
del python_3.11_installer.exe

echo.
echo =======================================================
echo [3/3] НАЛАШТУВАННЯ КОНТЕКСТНОГО МЕНЮ "ВІДКРИТИ ЗА ДОПОМОГОЮ"
echo =======================================================
:: Реєструємо асоціації в реєстрі Windows на запуск через наш головний батник
assoc .pt=YoloPtModel >nul 2>&1
ftype YoloPtModel="%~dp0run_tracker.bat" "%%1" >nul 2>&1

assoc .onnx=YoloOnnxModel >nul 2>&1
ftype YoloOnnxModel="%~dp0run_tracker.bat" "%%1" >nul 2>&1

echo [УСПІШНО] Меню "Відкрити за допомогою" активовано для файлів .pt та .onnx!
echo =======================================================
echo [+] ВСЕ ЗАВЕРШЕНО! Чистий Python встановлено та налаштовано.
echo [ІНФО] Перезавантажте комп'ютер для оновлення системних шляхів.
echo =======================================================
pause
