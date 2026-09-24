# overlay.py – оверлей с прозрачностью, меню и управлением клик-сквозь
import pygame
import win32gui
import win32con
import win32api
from ctypes import windll

class Overlay:
    def __init__(self, width=None, height=None):
        # Инициализация Pygame
        pygame.init()
        self.width = width or win32api.GetSystemMetrics(0)
        self.height = height or win32api.GetSystemMetrics(1)
        self.screen = pygame.display.set_mode(
            (self.width, self.height),
            pygame.NOFRAME | pygame.DOUBLEBUF | pygame.HWSURFACE
        )
        pygame.display.set_caption("RustVision CV (тестовый режим)")
        self.clock = pygame.time.Clock()
        self.transparent_color = (0, 0, 0)  # чёрный – прозрачный
        self.running = True

        # Получаем HWND окна
        self.hwnd = pygame.display.get_wm_info()['window']

        # Устанавливаем изначально клик-сквозь и поверх всех
        self.set_click_through(True)

        # Защита от захвата экрана (стрим-пруф)
        try:
            windll.user32.SetWindowDisplayAffinity(self.hwnd, 0x00000011)
        except:
            pass

        # Шрифты
        self.font = pygame.font.Font(None, 24)
        self.big_font = pygame.font.Font(None, 32)

        # Состояния
        self.menu_visible = False
        self.click_through = True

        # Настройки (будут обновляться из детектора)
        self.fps_limit = 60
        self.max_distance = 150
        self.aim_fov = 100
        self.aim_smooth = 5
        self.esp_enabled = False
        self.aim_enabled = False

        # Для слайдеров
        self.slider_dragging = False
        self.slider_id = None
        self.detector_ref = None   # ссылка на детектор для сохранения настроек

    def set_click_through(self, enabled):
        """Включает/выключает режим клик-сквозь через Win32 API."""
        self.click_through = enabled
        style = win32gui.GetWindowLong(self.hwnd, win32con.GWL_EXSTYLE)
        if enabled:
            style |= win32con.WS_EX_TRANSPARENT | win32con.WS_EX_LAYERED
        else:
            style &= ~win32con.WS_EX_TRANSPARENT
        win32gui.SetWindowLong(self.hwnd, win32con.GWL_EXSTYLE, style)
        win32gui.SetLayeredWindowAttributes(
            self.hwnd,
            win32api.RGB(0, 0, 0),
            0,
            win32con.LWA_COLORKEY
        )

    def clear(self):
        """Заливает фон прозрачным цветом."""
        self.screen.fill(self.transparent_color)

    def draw_rect(self, x, y, w, h, color, thickness=1):
        if w > 0 and h > 0:
            pygame.draw.rect(self.screen, color, (x, y, w, h), thickness)

    def draw_filled_rect(self, x, y, w, h, color):
        pygame.draw.rect(self.screen, color, (x, y, w, h))

    def draw_circle(self, cx, cy, radius, color, thickness=1):
        pygame.draw.circle(self.screen, color, (cx, cy), radius, thickness)

    def draw_text(self, x, y, text, color=(255,255,255), font=None):
        if font is None:
            font = self.font
        surf = font.render(text, True, color)
        self.screen.blit(surf, (x, y))

    def draw_fov_circle(self):
        """Рисует красное кольцо FOV, если аимбот включён."""
        if self.aim_enabled:
            cx, cy = self.width // 2, self.height // 2
            self.draw_circle(cx, cy, self.aim_fov, (255, 0, 0), 1)

    def draw_slider(self, x, y, w, h, value, min_val, max_val, label, color=(200,0,0)):
        """
        Рисует интерактивный слайдер и возвращает новое значение.
        Работает только когда меню открыто и клик-сквозь отключён.
        """
        # Фон слайдера
        self.draw_filled_rect(x, y, w, h, (60, 60, 60))
        # Заполнение
        frac = (value - min_val) / (max_val - min_val) if max_val > min_val else 0
        fill_w = int(w * frac)
        self.draw_filled_rect(x, y, fill_w, h, color)
        # Ручка
        handle_x = x + fill_w - 4
        self.draw_rect(handle_x, y - 2, 8, h + 4, (255, 255, 255), 1)
        # Надпись с текущим значением
        label_text = f"{label}: {value}"
        self.draw_text(x + w + 10, y - 2, label_text, (200, 200, 200))

        # Логика перетаскивания (только при открытом меню и без клик-сквозь)
        if self.menu_visible and not self.click_through:
            mx, my = pygame.mouse.get_pos()
            if self.slider_dragging and self.slider_id == id(self):
                new_val = min_val + (mx - x) / w * (max_val - min_val)
                new_val = max(min_val, min(max_val, new_val))
                return new_val
            elif pygame.mouse.get_pressed()[0]:
                if (x <= mx <= x + w) and (y - 5 <= my <= y + h + 5):
                    self.slider_dragging = True
                    self.slider_id = id(self)
                    new_val = min_val + (mx - x) / w * (max_val - min_val)
                    new_val = max(min_val, min(max_val, new_val))
                    return new_val
        return value

    def draw_menu(self):
        """Рисует тёмное меню со всеми настройками (все надписи на русском)."""
        if not self.menu_visible:
            return

        # Полупрозрачный фон
        menu_rect = (20, 20, 340, 420)
        self.draw_filled_rect(
            menu_rect[0], menu_rect[1], menu_rect[2], menu_rect[3],
            (30, 30, 30, 200)
        )
        self.draw_rect(menu_rect[0], menu_rect[1], menu_rect[2], menu_rect[3], (200, 0, 0), 2)

        y = 30
        self.draw_text(30, y, "RUSTVISION CV", (255, 0, 0), self.big_font)
        y += 40

        # ---- FPS лимит (кнопки) ----
        fps_options = ["15", "30", "60", "Безлимит"]
        self.draw_text(30, y, "Лимит FPS:", (255, 255, 255))
        for i, opt in enumerate(fps_options):
            x_btn = 160 + i * 55
            active = False
            if opt == "Безлимит" and self.fps_limit == 0:
                active = True
            elif opt.isdigit() and int(opt) == self.fps_limit:
                active = True
            color = (200, 0, 0) if active else (80, 80, 80)
            self.draw_filled_rect(x_btn, y - 2, 50, 22, color)
            self.draw_text(x_btn + 5, y - 2, opt, (255, 255, 255))
            if not self.click_through and self.menu_visible:
                mx, my = pygame.mouse.get_pos()
                if pygame.mouse.get_pressed()[0] and (x_btn <= mx <= x_btn + 50 and y - 2 <= my <= y + 20):
                    new_val = 0 if opt == "Безлимит" else int(opt)
                    self.fps_limit = new_val
                    if self.detector_ref:
                        self.detector_ref.update_setting("fps_limit", new_val)
        y += 30

        # ---- Дальность детекции ----
        new_dist = self.draw_slider(30, y, 200, 18, self.max_distance, 50, 500, "Дальность (м)")
        if new_dist != self.max_distance:
            self.max_distance = int(new_dist)
            if self.detector_ref:
                self.detector_ref.update_setting("max_distance", self.max_distance)
        y += 30

        # ---- FOV аимбота ----
        new_fov = self.draw_slider(30, y, 200, 18, self.aim_fov, 20, 400, "FOV")
        if new_fov != self.aim_fov:
            self.aim_fov = int(new_fov)
            if self.detector_ref:
                self.detector_ref.update_setting("aim_fov", self.aim_fov)
        y += 30

        # ---- Плавность аимбота ----
        new_smooth = self.draw_slider(30, y, 200, 18, self.aim_smooth, 1, 20, "Плавность")
        if new_smooth != self.aim_smooth:
            self.aim_smooth = int(new_smooth)
            if self.detector_ref:
                self.detector_ref.update_setting("aim_smooth", self.aim_smooth)
        y += 30

        # ---- Чекбокс ESP ----
        self.draw_text(30, y, "ESP:", (255, 255, 255))
        color = (0, 255, 0) if self.esp_enabled else (100, 100, 100)
        self.draw_filled_rect(120, y - 2, 20, 20, color)
        if not self.click_through and self.menu_visible:
            mx, my = pygame.mouse.get_pos()
            if pygame.mouse.get_pressed()[0] and (120 <= mx <= 140 and y - 2 <= my <= y + 18):
                self.esp_enabled = not self.esp_enabled
                if self.detector_ref:
                    self.detector_ref.update_setting("esp_enabled", self.esp_enabled)
        y += 25

        # ---- Чекбокс Аимбот ----
        self.draw_text(30, y, "Аимбот:", (255, 255, 255))
        color = (0, 255, 0) if self.aim_enabled else (100, 100, 100)
        self.draw_filled_rect(120, y - 2, 20, 20, color)
        if not self.click_through and self.menu_visible:
            mx, my = pygame.mouse.get_pos()
            if pygame.mouse.get_pressed()[0] and (120 <= mx <= 140 and y - 2 <= my <= y + 18):
                self.aim_enabled = not self.aim_enabled
                if self.detector_ref:
                    self.detector_ref.update_setting("aim_enabled", self.aim_enabled)
        y += 25

        # ---- Кнопка переключения клик-сквозь ----
        btn_text = "Клик-сквозь: ВКЛ" if self.click_through else "Клик-сквозь: ВЫКЛ"
        color = (0, 200, 0) if self.click_through else (200, 0, 0)
        self.draw_filled_rect(30, y, 200, 25, color)
        self.draw_text(40, y + 3, btn_text, (255, 255, 255))
        if not self.click_through and self.menu_visible:
            mx, my = pygame.mouse.get_pos()
            if pygame.mouse.get_pressed()[0] and (30 <= mx <= 230 and y <= my <= y + 25):
                self.set_click_through(not self.click_through)

        # ---- Информация о FPS ----
        y += 35
        self.draw_text(30, y, f"FPS: {int(self.clock.get_fps())}", (200, 200, 200))

    def update(self):
        """Обновляет экран (вызов pygame.display.flip())."""
        pygame.display.flip()

    def close(self):
        """Закрывает окно и завершает Pygame."""
        self.running = False
        pygame.quit()