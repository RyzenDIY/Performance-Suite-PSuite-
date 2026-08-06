using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;

namespace PSuite
{
    public partial class App : Application
    {
        // Utrimuye eksklyuzivne blokuvannya na ves chas zhyttya protsesu.
        // Yakshcho druga kopiya PSuite sprobuye startuvaty — vona odrazu
        // pobachyt, shcho m'yuteks zaynyatyy, i zavershytsya bez vikna.
        private static Mutex? _singleInstanceMutex;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private const uint MB_OK = 0x0;
        private const uint MB_ICONWARNING = 0x30;
        private const uint MB_ICONERROR = 0x10;

        public App()
        {
            this.InitializeComponent();
        }

        // Called only by the intentional self-elevate relaunch flow
        // (MainWindow.PromptRelaunchElevatedAsync) right before starting
        // the elevated copy of PSuite. Without this, the new elevated
        // process races the old one for the mutex — Application.Exit()
        // is asynchronous and doesn't release it instantly, so the new
        // process can see it still held, assume PSuite is "already
        // running", and quit itself. Net effect: both processes close
        // and nothing reopens. Releasing explicitly here removes that
        // race entirely.
        public static void ReleaseSingleInstanceMutexForRelaunch()
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch
            {
                // Best-effort — if this fails, Environment.Exit(0) right
                // after still tears the process (and its mutex handle)
                // down fast enough in practice.
            }
            finally
            {
                _singleInstanceMutex = null;
            }
        }

        // Called when a relaunch was attempted (mutex already released)
        // but the new elevated process turned out to have died immediately
        // — meaning this instance is staying alive after all and needs its
        // single-instance guard back. Returns false only in the
        // vanishingly unlikely case something else grabbed the name in
        // that gap, which is treated as "someone else is now the instance"
        // rather than crashing.
        public static bool ReacquireSingleInstanceMutex()
        {
            try
            {
                _singleInstanceMutex = new Mutex(true, "PSuite_SingleInstance_Mutex", out bool acquired);
                return acquired;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _singleInstanceMutex = new Mutex(true, "PSuite_SingleInstance_Mutex", out bool isFirstInstance);

                if (!isFirstInstance)
                {
                    // This used to be a silent Environment.Exit(0) — which
                    // is exactly what makes "the window just doesn't open"
                    // impossible to diagnose. If this ever fires because a
                    // previous PSuite process didn't fully exit (e.g. an
                    // interrupted elevation relaunch), the person now sees
                    // why instead of a blank nothing.
                    MessageBoxW(IntPtr.Zero,
                        "PSuite уже запущен (проверьте Диспетчер задач → Подробности → PSuite.exe и завершите его, если окна не видно). Это окно закроется.",
                        "PSuite", MB_OK | MB_ICONWARNING);
                    Environment.Exit(0);
                    return;
                }

                _window = new MainWindow();
                _window.Activate();
            }
            catch (Exception ex)
            {
                // Any unhandled exception during startup used to mean the
                // exact same symptom: the window never appears, with zero
                // explanation. This is a last-resort safety net — it can't
                // fix the underlying cause, but it guarantees the person
                // sees SOMETHING instead of silence.
                try
                {
                    var details = new System.Text.StringBuilder();
                    Exception? current = ex;
                    int depth = 0;
                    while (current != null && depth < 6)
                    {
                        details.AppendLine($"[{depth}] {current.GetType().FullName}: {current.Message}");
                        if (!string.IsNullOrEmpty(current.StackTrace))
                            details.AppendLine(current.StackTrace);
                        details.AppendLine();
                        current = current.InnerException;
                        depth++;
                    }

                    MessageBoxW(IntPtr.Zero,
                        $"PSuite не смог запуститься:\n\n{details}",
                        "PSuite — ошибка запуска", MB_OK | MB_ICONERROR);
                }
                catch
                {
                    // If even MessageBoxW fails, there's nothing further
                    // we can do to surface this.
                }

                Environment.Exit(1);
            }
        }

        private Window? _window;
    }
}