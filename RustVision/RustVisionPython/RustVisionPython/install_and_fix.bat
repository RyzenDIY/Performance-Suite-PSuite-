@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================================
echo RustVision CV – Чистая установка под Python 3.14
echo ============================================================

:: Проверка наличия Python
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ОШИБКА] Python не найден! Установите Python 3.14 и добавьте в PATH.
    pause
    exit /b 1
)
echo [OK] Python найден: 
python --version

:: Создание виртуального окружения (опционально)
if not exist "venv" (
    echo [INFO] Создание виртуального окружения venv...
    python -m venv venv
)

:: Активация venv
if exist "venv\Scripts\activate.bat" (
    call venv\Scripts\activate.bat
    echo [INFO] venv активирован.
)

:: Обновление pip и инструментов сборки
echo [INFO] Обновление pip, setuptools, wheel...
python -m pip install --upgrade pip setuptools wheel

:: Ставим совместимую пару: самый новый NumPy и OpenCV 5+
echo [INFO] Установка актуального numpy для Python 3.14...
pip install --only-binary :all: numpy --upgrade --no-cache-dir

echo [INFO] Установка совместимого opencv-python...
pip install --only-binary :all: opencv-python --upgrade --no-cache-dir

echo [INFO] Установка остальных модулей (оверлей, клавиши, окна)...
pip install pywin32 keyboard pygame-ce

echo.
echo ============================================================
echo Проверка работоспособности CV-движка...
echo ============================================================
python -c "import cv2; import numpy; print('>>> СУПЕР: OpenCV ' + cv2.__version__ + ' и NumPy успешно запущены на Python 3.14! <<<')" 2>nul

if %errorlevel% neq 0 (
    echo [INFO] Ошибка импорта осталась. Пробуем финальный фикс совместимости...
    pip install "numpy>=2.3.2" "opencv-python>=5.0.0" --upgrade --force-reinstall
    python -c "import cv2; import numpy; print('>>> Фикс сработал! Все библиотеки готовы. <<<')" 2>nul
)

echo ============================================================
pause
