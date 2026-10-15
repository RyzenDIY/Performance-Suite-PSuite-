using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public class ScreenCapture : IDisposable
{
    // Системный хак DirectX для захвата кадра прямо из буфера видеокарты (пробивает Fullscreen)
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    public Bitmap CaptureRoi(int cx, int cy, int fov)
    {
        int size = fov * 2;
        Bitmap roiBitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        // Берем контекст Direct3D/GDI всего рабочего стола
        IntPtr hwndDesktop = GetDesktopWindow();
        IntPtr hdcScreen = GetWindowDC(hwndDesktop);

        using (Graphics graphics = Graphics.FromImage(roiBitmap))
        {
            IntPtr hdcBmp = graphics.GetHdc();
            // Скоростное попиксельное копирование 1 к 1
            BitBlt(hdcBmp, 0, 0, size, size, hdcScreen, cx - fov, cy - fov, 0x00CC0020); // SRCCOPY
            graphics.ReleaseHdc(hdcBmp);
        }

        ReleaseDC(hwndDesktop, hdcScreen);

        // Принудительно очищаем память, чтобы разгрузить ядра процессора
        GC.Collect(0, GCCollectionMode.Optimized);
        return roiBitmap;
    }

    public void Dispose() { }
}
