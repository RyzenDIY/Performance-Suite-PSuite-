import sys
import time
import traceback
import warnings
import cv2
import mss
import numpy as np
import pygame
import win32gui
import win32con
import win32api
import pygetwindow as gw
import gc

warnings.filterwarnings("ignore", category=DeprecationWarning)
warnings.filterwarnings("ignore", category=UserWarning)
import os
os.environ["YOLO_VERBOSE"] = "False"

import torch
from ultralytics import YOLO

CHOSEN_DEVICE = "cpu"

print("=======================================================")
print("[СТАРТ] Запуск финальной сборки CPU с фиксом координат...")
print("=======================================================")

CLASS_NAMES = {
    0: "Player",     
    15: "Boar/Cat",  
    16: "Wolf/Dog",
    17: "Cat"
}

def get_rust_window_geometry():
    try:
        windows = gw.getWindowsWithTitle('Rust')
        if windows:
            rust_win = windows
            if rust_win.width > 0 and rust_win.height > 0:
                return rust_win.left, rust_win.top, rust_win.width, rust_win.height
    except: pass
    return 0, 0, win32api.GetSystemMetrics(0), win32api.GetSystemMetrics(1)

esp_active = True
SCAN_MODE = "Aim Only (CPU TURBO)"
SCAN_SIZE = 300 
FULL_SCREEN_MODE = False
PERMANENT_CIRCLE = True 

current_fps = 0
latency_ms = 0.0
last_f5, last_f6, last_f7, last_f10 = 0, 0, 0, 0

try:
    model = YOLO("yolov8n.pt", task='detect')
    print("[ОК] Оригинальная модель .pt успешно загружена.")

    pygame.init()
    pygame.mixer.quit()
    
    screen_w = win32api.GetSystemMetrics(0)
    screen_h = win32api.GetSystemMetrics(1)
    screen = pygame.display.set_mode((screen_w, screen_h), pygame.NOFRAME)

    hwnd = pygame.display.get_wm_info()['window']
    win32gui.SetWindowLong(hwnd, win32con.GWL_EXSTYLE, 
                           win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE) | 
                           win32con.WS_EX_TRANSPARENT | win32con.WS_EX_LAYERED)
    win32gui.SetLayeredWindowAttributes(hwnd, 0x000000, 0, win32con.LWA_COLORKEY)
    win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0, 0, screen_w, screen_h, win32con.SWP_SHOWWINDOW)

    font = pygame.font.SysFont("Arial", 14, bold=True)
    fps_font = pygame.font.SysFont("Consolas", 14, bold=True)
    clock = pygame.time.Clock()
    running = True
    frame_counter = 0

    sct = mss.mss()

    while running:
        t_start = time.time()
        pygame.event.pump()
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                running = False

        current_time = time.time()
        
        if (win32api.GetAsyncKeyState(win32con.VK_F5) & 0x8000) and (current_time - last_f5 > 0.3):
            SCAN_SIZE = 300
            FULL_SCREEN_MODE = False
            SCAN_MODE = "Aim Only (CPU TURBO)"
            last_f5 = current_time
            
        if (win32api.GetAsyncKeyState(win32con.VK_F6) & 0x8000) and (current_time - last_f6 > 0.3):
            FULL_SCREEN_MODE = True
            SCAN_MODE = "Full Screen (Slow CPU)"
            last_f6 = current_time
            
        if (win32api.GetAsyncKeyState(win32con.VK_F7) & 0x8000) and (current_time - last_f7 > 0.3):
            PERMANENT_CIRCLE = not PERMANENT_CIRCLE
            last_f7 = current_time
            
        if (win32api.GetAsyncKeyState(win32con.VK_F10) & 0x8000) and (current_time - last_f10 > 0.3):
            esp_active = not esp_active
            last_f10 = current_time

        try:
            win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0, 0, screen_w, screen_h, win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
            win32gui.UpdateWindow(hwnd)
        except: pass

        screen.fill((0, 0, 0))
        phys_center_x, phys_center_y = screen_w // 2, screen_h // 2

        if esp_active and not FULL_SCREEN_MODE and PERMANENT_CIRCLE:
            pygame.draw.circle(screen, (0, 255, 0), (phys_center_x, phys_center_y), SCAN_SIZE // 2, 3)

        if not esp_active:
            paused_surface = fps_font.render("AI ESP: PAUSED (F10)", True, (255, 0, 0))
            screen.blit(paused_surface, (13, 11))
            pygame.display.update()
            clock.tick(30)
            continue

        if FULL_SCREEN_MODE:
            monitor = {"top": 0, "left": 0, "width": screen_w, "height": screen_h}
            crop_left, crop_top = 0, 0
        else:
            crop_left = phys_center_x - (SCAN_SIZE // 2)
            crop_top = phys_center_y - (SCAN_SIZE // 2)
            monitor = {"top": crop_top, "left": crop_left, "width": SCAN_SIZE, "height": SCAN_SIZE}
        
        try:
            screenshot = np.array(sct.grab(monitor))
            frame = cv2.cvtColor(screenshot, cv2.COLOR_BGRA2BGR)
            
            results = model(frame, device=CHOSEN_DEVICE, stream=False, conf=0.25, verbose=False, max_det=3)
            
            for r in results:
                for box in r.boxes:
                    class_id = int(box.cls)
                    
                    if class_id in CLASS_NAMES:
                        # ЖЕСТКИЙ ФИКС ДЛЯ CPU: Извлекаем плоские координаты через одномерный срез NumPy
                        xyxy = box.xyxy.cpu().numpy().flatten()
                        x1, y1, x2, y2 = map(int, xyxy)
                            
                        screen_x1 = x1 + crop_left
                        screen_y1 = y1 + crop_top
                        w, h = x2 - x1, y2 - y1
                        
                        # Отрисовка жирного каркаса
                        pygame.draw.rect(screen, (0, 255, 0), (screen_x1, screen_y1, w, h), 3)
                        text_surface = font.render(f"{CLASS_NAMES[class_id]}", True, (0, 255, 0))
                        screen.blit(text_surface, (screen_x1 + 2, screen_y1 - 18 if (screen_y1 - 18) > 0 else 0))
                        
        except Exception:
            continue

        t_end = time.time()
        latency_ms = (t_end - t_start) * 1000
        current_fps = int(1.0 / (t_end - t_start)) if (t_end - t_start) > 0 else 0

        fps_text = f"Stable Mode | Latency: {latency_ms:.1f}ms | Real AI FPS: {current_fps}"
        fps_surface = fps_font.render(fps_text, True, (0, 255, 0))
        pygame.draw.rect(screen, (0, 0, 0), (10, 10, fps_surface.get_width() + 10, 22))
        screen.blit(fps_surface, (15, 13))

        pygame.display.update()
        clock.tick(60)

        frame_counter += 1
        if frame_counter >= 30:
            pygame.event.pump()
            try: del frame; del screenshot
            except: pass
            gc.collect()
            frame_counter = 0

except Exception:
    traceback.print_exc()
    input("\nНажмите ENTER для выхода...")
finally:
    try: sct.close()
    except: pass
    pygame.quit()
    sys.exit(0)
