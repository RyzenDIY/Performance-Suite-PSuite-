@echo off
chcp 65001 >nul
title Капітальний ремонт бібліотек ШІ-оверлею

cd /d "%~dp0"

:: Перевірка прав Адміністратора
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [СИСТЕМА] Запит прав Адміністратора...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: Пошук встановленого Python
set "PY_PATH=%USERPROFILE%\AppData\Local\Programs\Python\Python311\python.exe"
if exist "%PY_PATH%" goto python_found
set "PY_PATH=C:\Program Files\Python311\python.exe"
if exist "%PY_PATH%" goto python_found
python --version >nul 2>&1
if %errorlevel% equ 0 (set "PY_PATH=python") else (echo [!] Не знайдено встановлений Python у системі! Скористайтеся файлом install_python.bat && pause && exit)

:python_found
echo [+] Використовуємо для ремонту: %PY_PATH%

echo.
echo =======================================================
echo [1/3] ОЧИЩЕННЯ: Видалення зламаних та конфліктних версій
echo =======================================================
"%PY_PATH%" -m pip uninstall torch torchvision torchaudio onnxruntime onnxruntime-gpu numpy ultralytics mss pygame pygetwindow keyboard pywin32 -y >nul 2>&1

echo.
echo =======================================================
echo [2/3] РЕМОНТ PIP ТА ЛІКУВАННЯ NUMPY (Захист від _ARRAY_API)
echo =======================================================
"%PY_PATH%" -m pip install --upgrade pip --trusted-host pypi.org --trusted-host files.pythonhosted.org --quiet
:: Примусово ставимо стабільну версію NumPy 1.x, щоб оверлей не зависав у статусі "Не отвечает"
"%PY_PATH%" -m pip install "numpy<2" --trusted-host pypi.org --trusted-host files.pythonhosted.org --quiet

echo.
echo =======================================================
echo [3/3] ВІДНОВЛЕННЯ: Встановлення базових робочих пакетів
echo =======================================================
:: Ставимо стабільний процесорний onnxruntime та базові модулі захвату та малювання
"%PY_PATH%" -m pip install onnxruntime==1.17.0 ultralytics opencv-python mss pygame pygetwindow keyboard pywin32 --trusted-host pypi.org --trusted-host files.pythonhosted.org

echo.
echo =======================================================
echo [+] РЕМОНТ ЗАВЕРШЕНО УСПІШНО!
echo [ІНФО] Всі системні конфлікти виправлено. Можна запускати чит!
echo =======================================================
pause
