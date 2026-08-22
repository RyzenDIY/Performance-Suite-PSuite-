using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;

public class Overlay : Form
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    public static extern uint SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private List<BoundingBox> _drawTargets = new List<BoundingBox>();
    private int _fov;
    private bool _aimEnabled;
    private Stopwatch _fpsTimer = Stopwatch.StartNew();
    private int _frameCount = 0;

    public float CurrentFps { get; private set; } = 0;
    public float CurrentMs { get; private set; } = 0;
    public Color FovColor { get; set; } = Color.Red;

    public Overlay()
    {
        this.DoubleBuffered = true;
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.Manual;
        this.Bounds = Screen.PrimaryScreen.Bounds;
        this.TopMost = true;
        this.BackColor = Color.Magenta;
        this.TransparencyKey = Color.Magenta;
        this.Text = "RustVisionOverlay";
        this.ShowInTaskbar = false;

        int initialStyle = (int)GetWindowLong(this.Handle, -20);
        SetWindowLong(this.Handle, -20, initialStyle | 0x80000 | 0x20);
        Show();
    }

    public void UpdateData(List<BoundingBox> detections, int fov, bool aimEnabled, float frameTimeMs, bool antiScreenshot)
    {
        _drawTargets = detections;
        _fov = fov;
        _aimEnabled = aimEnabled;
        CurrentMs = frameTimeMs;

        _frameCount++;
        if (_fpsTimer.ElapsedMilliseconds >= 1000)
        {
            CurrentFps = _frameCount;
            _frameCount = 0;
            _fpsTimer.Restart();
        }

        // Захист Anti-Screenshot (EAC/Discord бачать чистий екран)
        SetWindowDisplayAffinity(this.Handle, antiScreenshot ? (uint)0x00000011 : (uint)0x00000000);

        if (this.IsHandleCreated) this.BeginInvoke(new Action(() => { this.Invalidate(); this.Update(); }));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int cx = this.Width / 2;
        int cy = this.Height / 2;

        if (_aimEnabled && _fov > 0)
        {
            using (Pen fovPen = new Pen(FovColor, 1)) e.Graphics.DrawEllipse(fovPen, cx - _fov, cy - _fov, _fov * 2, _fov * 2);
        }

        using (Pen greenPen = new Pen(Color.Lime, 2))
        using (Font espFont = new Font("Arial", 10, FontStyle.Bold))
        {
            foreach (var box in _drawTargets)
            {
                // Малюємо рамку навколо супротивника
                e.Graphics.DrawRectangle(greenPen, box.X, box.Y, box.Width, box.Height);
                e.Graphics.DrawString("Игрок", espFont, Brushes.Lime, box.X, box.Y - 18);

                // 🔄 АВТО-СИНХРОНІЗАЦІЯ КРАПКИ НА ОВЕРЛЕЇ:
                int dotY = box.Cy;
                if (Aimbot.aimBoneMode == 0) dotY -= (int)(box.Height * 0.36f);     // Точка пливе на голову
                else if (Aimbot.aimBoneMode == 1) dotY -= (int)(box.Height * 0.16f); // Точка сідає на шию
                else dotY += (int)(box.Height * 0.05f);                             // Точка стає по центру тіла

                // Малюємо точну червону крапку наведення ШІ
                e.Graphics.FillRectangle(Brushes.Red, box.Cx - 2, dotY - 2, 4, 4);
            }
        }

        // Твоє фірмове екранне міні-меню зверху зліва
        using (Font menuFont = new Font("Consolas", 10, FontStyle.Bold))
        using (Brush bgBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
        {
            e.Graphics.FillRectangle(bgBrush, 12, 12, 280, 95);
            e.Graphics.DrawRectangle(Pens.Lime, 12, 12, 280, 95);

            string status = _aimEnabled ? "ACTIVE (RMB)" : "DISABLED";
            e.Graphics.DrawString($" RUSTVISION CV v1.0 [C#]", menuFont, Brushes.Cyan, 15, 18);
            e.Graphics.DrawString($"----------------------------", menuFont, Brushes.Gray, 15, 32);
            e.Graphics.DrawString($" Швидкість : {CurrentMs:F1} ms | FPS: {CurrentFps}", menuFont, Brushes.Lime, 15, 48);
            e.Graphics.DrawString($" Радіус FOV: {_fov} px", menuFont, Brushes.Yellow, 15, 66);
            e.Graphics.DrawString($" Аїмбот    : {status}", menuFont, Brushes.White, 15, 84);
        }
    }

    protected override CreateParams CreateParams
    {
        get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x8 | 0x20 | 0x80000; return cp; }
    }
}
