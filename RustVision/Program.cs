using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

class Program
{
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public static ScreenCapture capture = new ScreenCapture();
    public static Detector detector = new Detector();
    public static Aimbot aimbot = new Aimbot();
    public static Overlay overlay = new Overlay();

    // Глобальні параметри чита (Синхронізовані!)
    public static int aimFov = 150;
    public static float smoothness = 18f;
    public static bool aimEnabled = true;
    public static int modelResolution = 300;
    public static bool noRecoilEnabled = true;
    public static bool antiScreenshotEnabled = true;
    public static float recoilPower = 1.0f;
    public static bool overlayVisible = true;
    public static int maxSoftwareFps = 60; // Фікс помилки лоадера конфігу!

    // Бінди клавіш за дефолтом
    public static int aimKey = 0x02; // ПКМ
    public static int toggleKey = 0x58; // 'X'

    [STAThread]
    static void Main(string[] args)
    {
        string protoPath = "deploy.prototxt";
        string caffePath = "mobilenet_iter_73000.caffemodel";

        if (!File.Exists(protoPath) || !File.Exists(caffePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ОШИБКА] Файли моделі deploy.prototxt або caffemodel не знайдені!");
            Console.ReadLine();
            return;
        }

        try
        {
            detector.LoadModel(protoPath, caffePath);
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ОШИБКА ІИ]: {e.Message}");
            Console.ReadLine();
            return;
        }

        // Розганяємо пріоритет процесу для стабільних FPS
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

        // Запуск логіки ШІ детекції
        Thread aiThread = new Thread(StartAiLoop) { IsBackground = true };
        aiThread.Start();

        // Запуск логіки керування кнопками
        Thread controlThread = new Thread(ControlLoop) { IsBackground = true };
        controlThread.Start();

        // Запуск прозорого оверлею
        Application.EnableVisualStyles();
        Application.Run(overlay);
    }

    public static void RestartCheat()
    {
        string currentExe = Process.GetCurrentProcess().MainModule.FileName;
        Process.Start(currentExe);
        Environment.Exit(0);
    }

    // Правильне виведення меню з системними кольорами Windows (Без помилки Lime!)
    private static void DrawConsoleMenu()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=====================================================");
        Console.WriteLine("      RUSTVISION CV v1.0 — CONSOLE EXPERT PANEL      ");
        Console.WriteLine("=====================================================");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($" [!] Аїмбот статус  : {(aimEnabled ? "УВІМКНЕНО" : "ВИМКНЕНО")}");
        Console.WriteLine($" [!] No-Recoil (АК) : {(noRecoilEnabled ? "АКТИВЕН" : "ВЫКЛЮЧЕН")}");
        Console.WriteLine($" [!] Anti-Screenshot: {(antiScreenshotEnabled ? "ЗАЩИТА ВКЛ" : "ОТКЛЮЧЕНА")}");
        Console.WriteLine($" [!] Кістка (Bone)   : {(Aimbot.aimBoneMode == 0 ? "Голова" : Aimbot.aimBoneMode == 1 ? "Шия" : "Тіло")}");
        ;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($" [▲/▼] Радіус FOV   : {aimFov} px");
        Console.WriteLine($" [◀/▶] Плавність    : {smoothness:F1}");
        Console.WriteLine($" [+/-] Сила Recoil  : {(int)(recoilPower * 100)}%");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("-----------------------------------------------------");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(" Керування: Натискайте стрілочки клавіатури прямо у грі!");
        Console.WriteLine(" Клавіші: [F1] - Кістка | [F2] - NoRecoil | [F3] - ESP | [F5] - Зберегти");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=====================================================");
    }

    private static void ControlLoop()
    {
        Config.Load(); // Завантажуємо збережений конфіг при старті
        DrawConsoleMenu();
        while (true)
        {
            bool updated = false;

            // Зміна FOV стрілочками Вгору/Вниз
            if ((GetAsyncKeyState(0x26) & 0x8000) != 0) { if (aimFov < 300) aimFov += 5; updated = true; Thread.Sleep(100); }
            if ((GetAsyncKeyState(0x28) & 0x8000) != 0) { if (aimFov > 20) aimFov -= 5; updated = true; Thread.Sleep(100); }

            // Зміна плавності стрілочками Вліво/Вправо
            if ((GetAsyncKeyState(0x27) & 0x8000) != 0) { if (smoothness < 80f) smoothness += 0.5f; updated = true; Thread.Sleep(100); }
            if ((GetAsyncKeyState(0x25) & 0x8000) != 0) { if (smoothness > 1f) smoothness -= 0.5f; updated = true; Thread.Sleep(100); }

            // Зміна сили No-Recoil клавішами Плюс (+) та Мінус (-) на клавіатурі
            if ((GetAsyncKeyState(0xBB) & 0x8000) != 0 || (GetAsyncKeyState(0x6B) & 0x8000) != 0) { if (recoilPower < 3.0f) recoilPower += 0.05f; updated = true; Thread.Sleep(100); }
            if ((GetAsyncKeyState(0xBD) & 0x8000) != 0 || (GetAsyncKeyState(0x6D) & 0x8000) != 0) { if (recoilPower > 0.1f) recoilPower -= 0.05f; updated = true; Thread.Sleep(100); }

            // F1 — Гаряче перемикання кісток (Голова/Шия/Тіло)
            if ((GetAsyncKeyState(0x70) & 0x8000) != 0) { Aimbot.aimBoneMode = (Aimbot.aimBoneMode + 1) % 3; updated = true; Thread.Sleep(250); }

            // F2 — Швидкий тумблер No-Recoil
            if ((GetAsyncKeyState(0x71) & 0x8000) != 0) { noRecoilEnabled = !noRecoilEnabled; updated = true; Thread.Sleep(250); }

            // F3 — Гасити / увімкнути ESP рамки на екрані
            if ((GetAsyncKeyState(0x72) & 0x8000) != 0) { overlayVisible = !overlayVisible; overlay.Invoke(new Action(() => overlay.Visible = overlayVisible)); updated = true; Thread.Sleep(250); }

            // F5 — Авто-збереження налаштувань у файл config.ini прямо під час гри!
            if ((GetAsyncKeyState(0x74) & 0x8000) != 0) { Config.Save(); updated = true; Thread.Sleep(300); }

            if (updated) DrawConsoleMenu();
            Thread.Sleep(10);
        }
    }

    private static void StartAiLoop()
    {
        int screenWidth = Screen.PrimaryScreen.Bounds.Width;
        int screenHeight = Screen.PrimaryScreen.Bounds.Height;
        int cx = screenWidth / 2;
        int cy = screenHeight / 2;
        Stopwatch frameTimer = new Stopwatch();

        while (true)
        {
            frameTimer.Restart();

            int roiX = cx - aimFov;
            int roiY = cy - aimFov;
            var frame = capture.CaptureRoi(cx, cy, aimFov);

            List<BoundingBox> detections = detector.Detect(frame, roiX, roiY, modelResolution, 250);

            bool isAimKeyPressed = (GetAsyncKeyState(aimKey) & 0x8000) != 0;

            if (isAimKeyPressed && aimEnabled)
            {
                aimbot.Run(detections, cx, cy, aimFov, smoothness);
                if (noRecoilEnabled && detections.Count > 0)
                {
                    Recoil.Compensate(Aimbot.recoilShotCounter, recoilPower);
                }
            }

            float elapsedMs = frameTimer.ElapsedMilliseconds;
            if (overlay != null && !overlay.IsDisposed)
            {
                overlay.UpdateData(detections, aimFov, aimEnabled, elapsedMs, antiScreenshotEnabled);
            }

            Thread.Sleep(1);
        }
    }
}
