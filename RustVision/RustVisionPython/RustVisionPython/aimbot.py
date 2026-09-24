# aimbot.py
import math
import win32api
import win32con

def run_aimbot(detections, settings, screen_center):
    """
    Плавно перемещает мышь к ближайшей цели в пределах FOV.
    settings: словарь с ключами 'aim_fov', 'aim_smooth', 'aim_enabled'.
    screen_center: (cx, cy) – центр экрана.
    """
    if not settings.get('aim_enabled', False):
        return
    fov = settings.get('aim_fov', 100)
    smooth = max(1, settings.get('aim_smooth', 5))
    if not detections:
        return
    cx, cy = screen_center
    best_dist = fov
    best_target = None
    for (x, y, w, h, cx_obj, cy_obj) in detections:
        dx = cx_obj - cx
        dy = cy_obj - cy
        dist = math.hypot(dx, dy)
        if dist < best_dist:
            best_dist = dist
            best_target = (cx_obj, cy_obj)
    if best_target is None:
        return
    target_x, target_y = best_target
    delta_x = target_x - cx
    delta_y = target_y - cy
    if abs(delta_x) < 0.5 and abs(delta_y) < 0.5:
        return
    if smooth > 1:
        delta_x /= smooth
        delta_y /= smooth
    win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, int(delta_x), int(delta_y), 0, 0)