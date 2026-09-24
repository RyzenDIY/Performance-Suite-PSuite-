# main.py – точка входа, подъём прав, потоки и главный цикл
import sys
import ctypes
import os
import threading
import time
import pygame
import win32api
from capture import ScreenCapture
from detector import Detector
from overlay import Overlay
from esp import draw_esp
from aimbot import run_aimbot

# ------------------------------------------------------------
# 1. АВТОМАТИЧЕСКОЕ ПОВЫШЕНИЕ ПРИВИЛЕГИЙ (UAC)
# ------------------------------------------------------------
def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False

if not is_admin():
    print("[СИСТЕМА] Запуск без прав администратора. Перезапускаем с повышением...")
    ctypes.windll.shell32.ShellExecuteW(
        None, "runas", sys.executable, " ".join(sys.argv), None, 1
    )
    sys.exit()

# ------------------------------------------------------------
# 2. ГЛОБАЛЬНЫЕ ДАННЫЕ ДЛЯ ПОТОКОВ
# ------------------------------------------------------------
shared = {
    'running': True,
    'detections': [],
    'frame': None,
}

# ------------------------------------------------------------
# 3. ПОТОК ЗАХВАТА И ДЕТЕКЦИИ
# ------------------------------------------------------------
def capture_detection_thread(capture, detector, overlay, shared):
    """Бесконечный захват ROI и запуск CV-детектора."""
    while shared['running']:
        # Вычисляем область интереса (ROI) вокруг центра экрана
        cx, cy = overlay.width // 2, overlay.height // 2
        fov = overlay.aim_fov
        padding = max(10, detector.roi_padding if hasattr(detector, 'roi_padding') else 30)
        roi_x = max(0, cx - fov - padding)
        roi_y = max(0, cy - fov - padding)
        roi_w = min(overlay.width, 2 * (fov + padding))
        roi_h = min(overlay.height, 2 * (fov + padding))
        frame = capture.capture_roi(roi_x, roi_y, roi_w, roi_h)
        if frame is not None:
            dets = detector.detect(frame)
            # Корректируем координаты относительно всего экрана
            corrected = []
            for (x, y, w, h, cx_obj, cy_obj) in dets:
                global_x = x + roi_x
                global_y = y + roi_y
                global_cx = cx_obj + roi_x
                global_cy = cy_obj + roi_y
                corrected.append((global_x, global_y, w, h, global_cx, global_cy))
            shared['detections'] = corrected
        else:
            shared['detections'] = []
        time.sleep(0.001)  # ~1 мс задержка для снижения нагрузки

# ------------------------------------------------------------
# 4. ПОТОК АИМБОТА
# ------------------------------------------------------------
def aimbot_thread(overlay, shared):
    """Проверяет горячие клавиши и двигает мышь."""
    while shared['running']:
        if overlay.aim_enabled:
            # Используем keyboard (требует админских прав)
            try:
                import keyboard
                if keyboard.is_pressed('capslock') or keyboard.is_pressed('lbutton'):
                    screen_center = (overlay.width // 2, overlay.height // 2)
                    settings = {
                        'aim_enabled': overlay.aim_enabled,
                        'aim_fov': overlay.aim_fov,
                        'aim_smooth': overlay.aim_smooth,
                    }
                    run_aimbot(shared['detections'], settings, screen_center)
            except:
                # Если keyboard не установлен – пропускаем
                pass
        time.sleep(0.001)

# ------------------------------------------------------------
# 5. ГЛАВНАЯ ФУНКЦИЯ
# ------------------------------------------------------------
def main():
    print("[ИНФО] Запуск RustVision CV (режим рабочего стола)...")

    # 5.1 Инициализация оверлея (он создаёт прозрачное окно сразу)
    overlay = Overlay()
    print("[ИНФО] Оверлей создан.")

    # 5.2 Инициализация захвата экрана (захватывает весь экран)
    capture = ScreenCapture()
    print("[ИНФО] Захват экрана готов.")

    # 5.3 Инициализация детектора (создаёт конфиг при необходимости)
    detector = Detector()
    print("[ИНФО] Детектор загружен.")

    # 5.4 Синхронизируем настройки оверлея с детектором
    overlay.detector_ref = detector
    overlay.fps_limit = detector.fps_limit
    overlay.max_distance = detector.max_distance
    overlay.aim_fov = detector.aim_fov
    overlay.aim_smooth = detector.aim_smooth
    overlay.esp_enabled = detector.esp_enabled
    overlay.aim_enabled = detector.aim_enabled

    # 5.5 Запуск потоков
    t_capture = threading.Thread(
        target=capture_detection_thread,
        args=(capture, detector, overlay, shared),
        daemon=True
    )
    t_capture.start()
    print("[ИНФО] Поток захвата запущен.")

    t_aim = threading.Thread(
        target=aimbot_thread,
        args=(overlay, shared),
        daemon=True
    )
    t_aim.start()
    print("[ИНФО] Поток аимбота запущен.")

    # 5.6 Основной цикл рендеринга (pygame-ce)
    clock = pygame.time.Clock()
    print("[ИНФО] Главный цикл рендеринга запущен. Нажмите INSERT или F2 для меню.")
    while shared['running']:
        # Обработка событий Pygame
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                shared['running'] = False
            elif event.type == pygame.KEYDOWN:
                if event.key == pygame.K_INSERT or event.key == pygame.K_F2:
                    # Переключение меню
                    overlay.menu_visible = not overlay.menu_visible
                    if overlay.menu_visible:
                        overlay.set_click_through(False)
                        print("[МЕНЮ] Открыто – клики перехватываются.")
                    else:
                        overlay.set_click_through(True)
                        print("[МЕНЮ] Закрыто – клики проходят сквозь.")
                elif event.key == pygame.K_ESCAPE:
                    shared['running'] = False

        # Очистка экрана (фон становится прозрачным)
        overlay.clear()

        # Рисуем круг FOV (если аимбот включён)
        overlay.draw_fov_circle()

        # Рисуем ESP (если включено)
        draw_esp(overlay, shared['detections'], {
            'esp_enabled': overlay.esp_enabled,
        })

        # Если меню открыто – рисуем его
        if overlay.menu_visible:
            overlay.draw_menu()

        # Обновление экрана
        overlay.update()

        # Ограничение FPS (настройка из меню)
        if overlay.fps_limit > 0:
            clock.tick(overlay.fps_limit)
        else:
            clock.tick()  # безлимит

    # 5.7 Очистка ресурсов перед завершением
    capture.release()
    overlay.close()
    print("[ИНФО] Программа завершена.")
    sys.exit(0)

if __name__ == "__main__":
    main()