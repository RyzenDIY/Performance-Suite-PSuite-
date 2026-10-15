# detector.py
import cv2
import numpy as np
import json
import os

class Detector:
    def __init__(self, config_path="config.json"):
        self.config_path = config_path
        self.config = self._load_or_create_config()
        self._parse_config()

    def _load_or_create_config(self):
        """Загружает конфиг, а при ошибке создаёт новый с настройками по умолчанию."""
        default_config = {
            "fps_limit": 60,
            "max_distance": 150,
            "aim_fov": 100,
            "aim_smooth": 5,
            "aim_enabled": False,
            "esp_enabled": False,
            "player_colors": [
                {"lower": [0, 40, 40], "upper": [25, 255, 255]},
                {"lower": [100, 50, 50], "upper": [140, 255, 255]},
                {"lower": [30, 50, 50], "upper": [60, 255, 255]},
                {"lower": [0, 50, 50], "upper": [10, 255, 255]}
            ],
            "min_contour_area": 60,
            "max_contour_area": 1200,
            "aspect_ratio_min": 0.3,
            "aspect_ratio_max": 1.8,
            "roi_padding": 30
        }
        try:
            with open(self.config_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            # Проверяем, что все ключи присутствуют (на случай ручной правки)
            for key in default_config:
                if key not in data:
                    data[key] = default_config[key]
            return data
        except (FileNotFoundError, json.JSONDecodeError, ValueError):
            print("[ДЕТЕКТОР] Файл config.json повреждён или отсутствует. Создаём новый с настройками по умолчанию.")
            with open(self.config_path, "w", encoding="utf-8") as f:
                json.dump(default_config, f, indent=4, ensure_ascii=False)
            return default_config

    def _parse_config(self):
        """Извлекает параметры из конфига для быстрого доступа."""
        cfg = self.config
        self.fps_limit = cfg.get("fps_limit", 60)
        self.max_distance = cfg.get("max_distance", 150)
        self.aim_fov = cfg.get("aim_fov", 100)
        self.aim_smooth = cfg.get("aim_smooth", 5)
        self.aim_enabled = cfg.get("aim_enabled", False)
        self.esp_enabled = cfg.get("esp_enabled", False)
        self.roi_padding = cfg.get("roi_padding", 30)
        # Цветовые диапазоны
        self.color_ranges = []
        for item in cfg.get("player_colors", []):
            lower = np.array(item["lower"], dtype=np.uint8)
            upper = np.array(item["upper"], dtype=np.uint8)
            self.color_ranges.append((lower, upper))
        # Параметры фильтрации контуров
        self.min_area = cfg.get("min_contour_area", 60)
        self.max_area = cfg.get("max_contour_area", 1200)
        self.aspect_min = cfg.get("aspect_ratio_min", 0.3)
        self.aspect_max = cfg.get("aspect_ratio_max", 1.8)

    def save_config(self):
        """Сохраняет текущие настройки в JSON-файл."""
        try:
            with open(self.config_path, "w", encoding="utf-8") as f:
                json.dump(self.config, f, indent=4, ensure_ascii=False)
            return True
        except Exception as e:
            print(f"[ДЕТЕКТОР] Ошибка сохранения конфига: {e}")
            return False

    def detect(self, frame):
        """
        Принимает BGR-кадр (numpy array), возвращает список детекций:
        каждый элемент: (x, y, w, h, центр_x, центр_y)
        """
        if frame is None or frame.size == 0:
            return []
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = np.zeros(hsv.shape[:2], dtype=np.uint8)
        for lower, upper in self.color_ranges:
            mask |= cv2.inRange(hsv, lower, upper)
        # Морфологическая очистка
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel, iterations=1)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        detections = []
        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < self.min_area or area > self.max_area:
                continue
            x, y, w, h = cv2.boundingRect(cnt)
            aspect = w / h if h > 0 else 0
            if aspect < self.aspect_min or aspect > self.aspect_max:
                continue
            cx = x + w // 2
            cy = y + h // 2
            detections.append((x, y, w, h, cx, cy))
        return detections

    def update_setting(self, key, value):
        """Обновляет настройку в памяти и сохраняет конфиг."""
        self.config[key] = value
        self._parse_config()   # перечитываем все параметры
        self.save_config()