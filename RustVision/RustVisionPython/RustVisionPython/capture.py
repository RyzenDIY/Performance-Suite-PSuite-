# capture.py
import win32gui
import win32ui
import win32con
import numpy as np

class ScreenCapture:
    def __init__(self):
        self.hwnd = win32gui.GetDesktopWindow()
        self.dc = win32gui.GetWindowDC(self.hwnd)
        self.mfc_dc = win32ui.CreateDCFromHandle(self.dc)
        self.save_dc = self.mfc_dc.CreateCompatibleDC()
        self.bitmap = None
        self.width = 0
        self.height = 0

    def capture_roi(self, x, y, w, h):
        """Захватывает область экрана (ROI) и возвращает BGR-изображение numpy."""
        if w <= 0 or h <= 0:
            return None
        if self.bitmap is None or self.width != w or self.height != h:
            if self.bitmap:
                self.save_dc.DeleteObject(self.bitmap)
            self.bitmap = win32ui.CreateBitmap()
            self.bitmap.CreateCompatibleBitmap(self.mfc_dc, w, h)
            self.save_dc.SelectObject(self.bitmap)
            self.width = w
            self.height = h
        self.save_dc.BitBlt((0, 0), (w, h), self.mfc_dc, (x, y), win32con.SRCCOPY)
        bmpinfo = self.bitmap.GetInfo()
        bmpstr = self.bitmap.GetBitmapBits(True)
        img = np.frombuffer(bmpstr, dtype=np.uint8).reshape((h, w, 4))[:, :, :3]
        return img[:, :, ::-1]  # BGRA -> BGR

    def release(self):
        if self.bitmap:
            self.save_dc.DeleteObject(self.bitmap)
        self.save_dc.DeleteDC()
        self.mfc_dc.DeleteDC()
        win32gui.ReleaseDC(self.hwnd, self.dc)