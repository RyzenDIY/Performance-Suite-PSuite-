# esp.py
import pygame

def draw_esp(overlay, detections, settings):
    """Отрисовывает прямоугольники и центры найденных объектов."""
    if not settings.get('esp_enabled', False):
        return
    for (x, y, w, h, cx, cy) in detections:
        overlay.draw_rect(x, y, w, h, (0, 255, 0), 2)
        overlay.draw_circle(cx, cy, 3, (255, 0, 0))
        overlay.draw_text(x, y - 18, "ЦЕЛЬ", (0, 255, 0))