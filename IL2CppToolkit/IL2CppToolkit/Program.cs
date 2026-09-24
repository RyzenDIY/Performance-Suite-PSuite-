// language: C#, file: Program.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int dwProcessId);

    const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    static void Main(string[] args)
    {
        // ═══════════════════════════════════════════════
        //   CLI режим
        // ═══════════════════════════════════════════════
        if (args.Length > 0 && args[0].StartsWith("--"))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

            try
            {
                var stdout = Console.OpenStandardOutput();
                var stderr = Console.OpenStandardError();
                Console.SetOut(new StreamWriter(stdout) { AutoFlush = true });
                Console.SetError(new StreamWriter(stderr) { AutoFlush = true });
            }
            catch { }

            int code = 0;
            try
            {
                if (args[0].Equals("--cli", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = new string[args.Length - 1];
                    Array.Copy(args, 1, rest, 0, rest.Length);
                    code = CliRunner.Run(rest);
                }
                else
                {
                    code = CliRunner.Run(args);
                }
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("FATAL: " + ex); } catch { }
                code = 3;
            }

            Thread.Sleep(300);
            Environment.Exit(code);
        }

        // ═══════════════════════════════════════════════
        //   GUI режим
        // ═══════════════════════════════════════════════
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (s, e) =>
        {
            TryLog("UI exception\n" + e.Exception);
            MessageBox.Show(e.Exception.Message + "\n\nДеталі у ui-error.log", "UI error");
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            TryLog("Domain exception\n" + e.ExceptionObject);
        };

        try
        {
            ApplicationConfiguration.Initialize();

            // ─── міграція зі старих шляхів
            try { Migrator.RunOnce(); } catch { }

            // ─── стартова довідка
            try
            {
                var cfg = AppConfig.Current;
                bool shouldShow = cfg.ShowStartupHelp &&
                    (cfg.ShowStartupAlways || string.IsNullOrEmpty(cfg.LastSeenVersion));

                if (shouldShow)
                {
                    using var help = new StartupForm();
                    help.ShowDialog();
                }
            }
            catch { }

            // ─── створюємо структуру папок для всіх ігор
            try
            {
                var reg = GameRegistry.Load();
                foreach (var g in reg.Games)
                {
                    try { GameFolders.EnsureStructure(g); }
                    catch { }
                }
            }
            catch { }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            TryLog("Main exception\n" + ex);
            MessageBox.Show(ex.Message, "Fatal error");
        }
    }

    static void TryLog(string s)
    {
        try
        {
            string p = Path.Combine(AppContext.BaseDirectory, "ui-error.log");
            File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + s + "\n\n");
        }
        catch { }
    }
}