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
import win32ui
import pygetwindow as gw
import gc

warnings.filterwarnings("ignore", category=DeprecationWarning)
warnings.filterwarnings("ignore", category=UserWarning)
import os
os.environ["YOLO_VERBOSE"] = "False"

# Автогенерация промпта-памятки в папку
PROMPT_TEXT = """Act as an elite Python AI developer and Low-Level Windows System Data-Type Engineer.
I am running a real-time YOLOv8 object detection transparent overlay on a Windows PC using Pygame and OpenCV.
The bounding boxes must be flattened via .cpu().numpy().flatten() to bypass CPU nesting bugs."""
try:
    with open("PROMPT_FIX_CPU_MATRICES.txt", "w", encoding="utf-8") as f:
        f.write(PROMPT_TEXT)
except: pass

from ultralytics import YOLO

# Всеядный список целей
CLASS_NAMES = {
    0: "Player",     
    15: "Boar/Cat",  
    16: "Wolf/Dog",
    17: "Cat"
}

screen_w = win32api.GetSystemMetrics(0)
screen_h = win32api.GetSystemMetrics(1)

esp_active = True
SCAN_MODE = "Aim"
SCAN_SIZE = 300 
FULL_SCREEN_MODE = False
PERMANENT_CIRCLE = True 

current_fps = 0
latency_ms = 0.0
last_f5, last_f6, last_f7, last_f10 = 0, 0, 0, 0

def log_status(action_text):
    print(f"[КЕРУВАННЯ] {action_text} | Mode: {SCAN_MODE} | FOV: {SCAN_SIZE} | Circle: {PERMANENT_CIRCLE}")

def win32_screenshot(top, left, width, height):
    hdesktop = win32gui.GetDesktopWindow()
    desktop_dc = win32gui.GetWindowDC(hdesktop)
    img_dc = win32ui.CreateDCFromHandle(desktop_dc)
    mem_dc = img_dc.CreateCompatibleDC()
    
    screenshot = win32ui.CreateBitmap()
    screenshot.CreateCompatibleBitmap(img_dc, width, height)
    mem_dc.SelectObject(screenshot)
    
    mem_dc.BitBlt((0, 0), (width, height), img_dc, (left, top), win32con.SRCCOPY)
    signed_ints_array = screenshot.GetBitmapBits(True)
    img = np.frombuffer(signed_ints_array, dtype='uint8')
    img.shape = (height, width, 4)
    
    win32gui.DeleteObject(screenshot.GetHandle())
    mem_dc.DeleteDC()
    img_dc.DeleteDC()
    win32gui.ReleaseDC(hdesktop, desktop_dc)
    return cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

try:
    print("[СИСТЕМА] Запуск финальной стабильной сборки...")
    model = YOLO("yolov8n", task='detect')
    print("[ОК] Модель yolov8n.pt успешно загружена.")

    pygame.init()
    pygame.mixer.quit()
    
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
    
    clahe = cv2.createCLAHE(clipLimit=4.0, tileGridSize=(6, 6))
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
            SCAN_MODE = "Aim"
            last_f5 = current_time
            log_status("Режим прицела")
            
        if (win32api.GetAsyncKeyState(win32con.VK_F6) & 0x8000) and (current_time - last_f6 > 0.3):
            FULL_SCREEN_MODE = True
            SCAN_MODE = "Full"
            last_f6 = current_time
            log_status("Режим весь экран")
            
        if (win32api.GetAsyncKeyState(win32con.VK_F7) & 0x8000) and (current_time - last_f7 > 0.3):
            PERMANENT_CIRCLE = not PERMANENT_CIRCLE
            last_f7 = current_time
            log_status("Кольцо изменено")
            
        if (win32api.GetAsyncKeyState(win32con.VK_F10) & 0x8000) and (current_time - last_f10 > 0.3):
            esp_active = not esp_active
            last_f10 = current_time
            log_status("Пауза чита")

        try:
            win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0, 0, screen_w, screen_h, win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
        except: pass

        screen.fill((0, 0, 0))
        phys_center_x = screen_w // 2
        phys_center_y = screen_h // 2

        if esp_active and not FULL_SCREEN_MODE and PERMANENT_CIRCLE:
            pygame.draw.circle(screen, (0, 255, 0), (phys_center_x, phys_center_y), SCAN_SIZE // 2, 3)

        if not esp_active:
            paused_surface = fps_font.render("AI ESP: PAUSED (F10)", True, (255, 0, 0))
            screen.blit(paused_surface, (13, 11))
            pygame.display.update()
            clock.tick(30)
            continue

        if FULL_SCREEN_MODE:
            crop_left, crop_top = 0, 0
            crop_w, crop_h = screen_w, screen_h
        else:
            crop_left = phys_center_x - (SCAN_SIZE // 2)
            crop_top = phys_center_y - (SCAN_SIZE // 2)
            crop_w, crop_h = SCAN_SIZE, SCAN_SIZE
        
        try:
            frame = win32_screenshot(crop_top, crop_left, crop_w, crop_h)
            
            lab = cv2.cvtColor(frame, cv2.COLOR_BGR2LAB)
            l_char, a_char, b_char = cv2.split(lab)
            frame = cv2.merge((clahe.apply(l_char), a_char, b_char))
            frame = cv2.cvtColor(frame, cv2.COLOR_LAB2BGR)
            
            results = model(frame, device='cpu', stream=False, conf=0.25, iou=0.45, verbose=False, max_det=3)
            
            for r in results:
                for box in r.boxes:
                    class_id = int(box.cls)
                    if class_id in CLASS_NAMES:
                        xyxy = box.xyxy.cpu().numpy().flatten()
                        x1, y1, x2, y2 = map(int, xyxy)
                            
                        screen_x1 = x1 + crop_left
                        screen_y1 = y1 + crop_top
                        w_box = x2 - x1
                        h_box = y2 - y1
                        
                        pygame.draw.rect(screen, (0, 255, 0), (screen_x1, screen_y1, w_box, h_box), 3)
                        text_surface = font.render(f"{CLASS_NAMES[class_id]}", True, (0, 255, 0))
                        screen.fill((0, 0, 0), (screen_x1, screen_y1 - 18, text_surface.get_width() + 4, 16))
                        screen.blit(text_surface, (screen_x1 + 2, screen_y1 - 18 if (screen_y1 - 18) > 0 else 0))
                        
        except Exception:
            continue

        t_end = time.time()
        latency_ms = (t_end - t_start) * 1000
        current_fps = int(1.0 / (t_end - t_start)) if (t_end - t_start) > 0 else 0

        # СЖАТЫЙ КОМПАКТНЫЙ ИНФО-БАР (Mode | ms | esp FPS)
        fps_text = f"Mode: {SCAN_MODE} | {latency_ms:.1f} ms | esp FPS: {current_fps}"
        fps_surface = fps_font.render(fps_text, True, (0, 255, 0))
        pygame.draw.rect(screen, (0, 0, 0), (10, 10, fps_surface.get_width() + 10, 22))
        screen.blit(fps_surface, (15, 13))

        pygame.display.update()
        clock.tick(60)

        frame_counter += 1
        if frame_counter >= 20:
            pygame.event.pump()
            try: del frame
            except: pass
            gc.collect()
            frame_counter = 0

except Exception as critical_error:
    traceback.print_exc()
    input("\nНажмите ENTER для выхода...")

finally:
    try: sct.close()
    except: pass
    pygame.quit()
    sys.exit(0)
