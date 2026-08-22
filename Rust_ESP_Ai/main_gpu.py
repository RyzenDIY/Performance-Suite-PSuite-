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
import keyboard

warnings.filterwarnings("ignore", category=UserWarning)
warnings.filterwarnings("ignore", category=DeprecationWarning)
import os
os.environ["YOLO_VERBOSE"] = "False"

# Mandatory Rule: Initialize standard PyTorch hooks first
import torch
from ultralytics import YOLO

# 1. LEGACY HARDWARE & COMPATIBILITY CHECK
CHOSEN_DEVICE = "cpu"
cuda_stream = None

print("=======================================================")
print("[HARDWARE DIAGNOSTIC] Scanning for Legacy GPU architecture...")
print("=======================================================")

if torch.cuda.is_available():
    # Verify Compute Capability limits safely
    try:
        major, minor = torch.cuda.get_device_capability(0)
        device_name = torch.cuda.get_device_name(0)
        print(f"[FOUND] {device_name} (Compute Capability {major}.{minor})")
        
        # Enforce legacy optimization pathways
        CHOSEN_DEVICE = "cuda:0"
        cuda_stream = torch.cuda.Stream(device=CHOSEN_DEVICE)
        print(f"[SUCCESS] Native CUDA Pipeline unlocked with asynchronous streams.")
    except Exception as hardware_warn:
        print(f"[FALLBACK] Verification error: {hardware_warn}. Routing to CPU safely.")
        CHOSEN_DEVICE = "cpu"
else:
    print("[FALLBACK] CUDA Toolkit 11.8 or GPU driver not hooked. Using CPU.")
print("=======================================================")

CLASS_NAMES = {0: "Player", 15: "Boar/Cat", 16: "Wolf/Dog"}

def get_rust_window_geometry():
    try:
        windows = gw.getWindowsWithTitle('Rust')
        if windows:
            rust_win = windows[0]
            if rust_win.width > 0 and rust_win.height > 0:
                return rust_win.left, rust_win.top, rust_win.width, rust_win.height
    except: pass
    return 0, 0, 1920, 1080

def win32_move_mouse(x, y, smooth_factor=4.0):
    dx = int(x / smooth_factor)
    dy = int(y / smooth_factor)
    if abs(dx) > 1 or abs(dy) > 1:
        dx += np.random.randint(-1, 2)
        dy += np.random.randint(-1, 2)
    win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, dx, dy, 0, 0)

esp_active = True
SCAN_MODE = f"Aim Assist ({'GTX 960 CUDA' if 'cuda' in CHOSEN_DEVICE else 'CPU Fallback'})"
SCAN_SIZE = 280 
FULL_SCREEN_MODE = False

keyboard.add_hotkey('F10', lambda: globals().update(esp_active=not esp_active))
keyboard.add_hotkey('F5', lambda: globals().update(SCAN_SIZE=280, FULL_SCREEN_MODE=False))
keyboard.add_hotkey('F6', lambda: globals().update(FULL_SCREEN_MODE=True))

try:
    # Explicitly load model infrastructure
    model = YOLO("yolov8n.pt", task='detect')
    print("[OK] Neural network structures loaded.")

    pygame.init()
    pygame.mixer.quit()
    r_left, r_top, r_width, r_height = get_rust_window_geometry()
    screen = pygame.display.set_mode((r_width, r_height), pygame.NOFRAME)

    hwnd = pygame.display.get_wm_info()['window']
    win32gui.SetWindowLong(hwnd, win32con.GWL_EXSTYLE, 
                           win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE) | 
                           win32con.WS_EX_TRANSPARENT | win32con.WS_EX_LAYERED)
    win32gui.SetLayeredWindowAttributes(hwnd, 0x000000, 0, win32con.LWA_COLORKEY)
    win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, r_left, r_top, r_width, r_height, win32con.SWP_SHOWWINDOW)

    font = pygame.font.SysFont("Arial", 12, bold=True)
    fps_font = pygame.font.SysFont("Consolas", 14, bold=True)
    clock = pygame.time.Clock()
    running = True
    frame_counter = 0

    with mss.mss() as sct:
        while running:
            t_start = time.time()
            pygame.event.pump()
            
            r_left, r_top, r_width, r_height = get_rust_window_geometry()
            try: win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, r_left, r_top, r_width, r_height, win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
            except: pass

            screen.fill((0, 0, 0))
            phys_center_x, phys_center_y = r_width // 2, r_height // 2

            if esp_active and not FULL_SCREEN_MODE:
                pygame.draw.circle(screen, (0, 255, 0), (phys_center_x, phys_center_y), SCAN_SIZE // 2, 2)

            if not esp_active:
                paused_surface = fps_font.render("AI ESP: PAUSED (F10)", True, (255, 0, 0))
                screen.blit(paused_surface, (13, 11))
                pygame.display.update()
                clock.tick(30)
                continue

            if FULL_SCREEN_MODE:
                monitor = {"top": r_top, "left": r_left, "width": r_width, "height": r_height}
                crop_left, crop_top = r_left, r_top
            else:
                crop_left = r_left + phys_center_x - (SCAN_SIZE // 2)
                crop_top = r_top + phys_center_y - (SCAN_SIZE // 2)
                monitor = {"top": crop_top, "left": crop_left, "width": SCAN_SIZE, "height": SCAN_SIZE}
            
            try:
                screenshot = np.array(sct.grab(monitor))
                frame = cv2.cvtColor(screenshot, cv2.COLOR_BGRA2BGR)
                
                # 2. INFERENCE OPTIMIZATION FOR MAXWELL ARCHITECTURE
                # - half=False ensures FP32 mode to bypass legacy architecture compute faults
                # - max_det=2 directly limits memory indexing latency on 2GB VRAM cards
                if cuda_stream:
                    with torch.cuda.stream(cuda_stream):
                        results = model(frame, device=CHOSEN_DEVICE, stream=False, half=False, conf=0.42, verbose=False, max_det=2)
                else:
                    results = model(frame, device=CHOSEN_DEVICE, stream=False, half=False, conf=0.42, verbose=False, max_det=2)
                
                targets_found = []
                
                for r in results:
                    for box in r.boxes:
                        class_id = int(box.cls)
                        if class_id in CLASS_NAMES:
                            coords = box.xyxy.tolist()
                            x1, y1, x2, y2 = map(int, coords[0]) if isinstance(coords[0], list) else map(int, coords)
                            conf = float(box.conf) * 100
                            
                            screen_x1 = x1 + crop_left - r_left
                            screen_y1 = y1 + crop_top - r_top
                            w, h = x2 - x1, y2 - y1
                            
                            pygame.draw.rect(screen, (0, 255, 0), (screen_x1, screen_y1, w, h), 2)
                            text_surface = font.render(f"{CLASS_NAMES[class_id]}: {conf:.1f}%", True, (0, 255, 0))
                            screen.blit(text_surface, (screen_x1 + 2, screen_y1 - 16 if (screen_y1 - 16) > 0 else 0))
                            
                            if class_id == 0 and not FULL_SCREEN_MODE:
                                t_cx = screen_x1 + (w // 2)
                                t_cy = screen_y1 + (h // 6)
                                dist = np.hypot(t_cx - phys_center_x, t_cy - phys_center_y)
                                targets_found.append((dist, t_cx - phys_center_x, t_cy - phys_center_y))

                if targets_found and (win32api.GetAsyncKeyState(0x02) & 0x8000):
                    targets_found.sort()
                    aim_dist, aim_x, aim_y = targets_found[0]
                    win32_move_mouse(aim_x, aim_y, smooth_factor=4.5)
                            
            except Exception:
                continue

            t_end = time.time()
            ai_speed_ms = (t_end - t_start) * 1000
            real_ai_fps = int(1.0 / (t_end - t_start)) if (t_end - t_start) > 0 else 0

            fps_text = f"Engine: {SCAN_MODE} | Latency: {ai_speed_ms:.1f}ms | FPS: {real_ai_fps}"
            fps_surface = fps_font.render(fps_text, True, (0, 255, 0))
            pygame.draw.rect(screen, (0, 0, 0), (10, 10, fps_surface.get_width() + 10, 22))
            screen.blit(fps_surface, (15, 13))

            pygame.display.update()
            clock.tick(60)

            frame_counter += 1
            if frame_counter >= 30:
                try: del frame; del screenshot
                except: pass
                if CHOSEN_DEVICE != "cpu":
                    torch.cuda.empty_cache() # Clear legacy hardware cache pointer
                gc.collect()
                frame_counter = 0

except Exception:
    traceback.print_exc()
    input("\nPress ENTER to escape...")
finally:
    pygame.quit()
