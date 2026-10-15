// language: C#, file: Adapters/Il2CppDumperAdapter.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

public class DumperResult
{
    public bool Success;
    public int ExitCode;
    public double Seconds;
    public string DumpCsPath;
    public string OutputDir;
    public string Error;
    public string ConsoleLogPath;
}

public static class Il2CppDumperAdapter
{
    public static DumperResult Run(
        string dumperExe,
        string assemblyDll,
        string metadataDat,
        string outputDir,
        Action<string> onLine = null,
        CancellationToken cancel = default)
    {
        var result = new DumperResult { OutputDir = outputDir };
        void Emit(string s) { if (s != null) onLine?.Invoke(s); }

        if (!File.Exists(dumperExe)) { result.Error = $"dumper not found: {dumperExe}"; return result; }
        if (!File.Exists(assemblyDll)) { result.Error = $"assembly not found: {assemblyDll}"; return result; }
        if (!File.Exists(metadataDat)) { result.Error = $"metadata not found: {metadataDat}"; return result; }

        Directory.CreateDirectory(outputDir);
        string logPath = Path.Combine(outputDir, "dumper-console.log");
        try { File.WriteAllText(logPath, ""); } catch { }
        result.ConsoleLogPath = logPath;

        // Запуск через cmd /c з редиректом у файл.
        // lxraa-форк блокується на Console.Write коли RedirectStandardOutput=true —
        // тому редиректимо в файл на рівні cmd.
        //
        // Команда виглядає так:
        //   cmd /c ""dumper.exe" "asm" "md" "out" > "log" 2>&1"
        //
        // Зовнішні лапки для cmd /c обов'язкові, коли всередині є > редірект.

        string inner = $"\"{dumperExe}\" \"{assemblyDll}\" \"{metadataDat}\" \"{outputDir}\" > \"{logPath}\" 2>&1 < nul";
        string args = $"/c \"{inner}\"";

        Emit($"▸ cmd /c {inner}");
        Emit("");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(dumperExe)
        };

        var sw = Stopwatch.StartNew();
        Process p = null;
        try
        {
            p = Process.Start(psi);
            if (p == null) { result.Error = "Process.Start returned null"; return result; }

            // читаємо лог у таймері, щоб показувати прогрес у callback
            long lastSize = 0;
            var streamTimer = new Thread(() =>
            {
                while (p != null && !p.HasExited)
                {
                    try
                    {
                        if (File.Exists(logPath))
                        {
                            long sz = new FileInfo(logPath).Length;
                            if (sz > lastSize)
                            {
                                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                fs.Seek(lastSize, SeekOrigin.Begin);
                                using var sr = new StreamReader(fs);
                                string newText = sr.ReadToEnd();
                                lastSize = sz;
                                if (!string.IsNullOrEmpty(newText))
                                {
                                    foreach (var line in newText.Split('\n'))
                                        if (!string.IsNullOrEmpty(line)) Emit(line.TrimEnd('\r'));
                                }
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(500);
                }
            })
            { IsBackground = true };
            streamTimer.Start();

            while (!p.WaitForExit(500))
            {
                if (cancel.IsCancellationRequested)
                {
                    try { p.Kill(); } catch { }
                    result.Error = "cancelled by user";
                    sw.Stop(); result.Seconds = sw.Elapsed.TotalSeconds;
                    return result;
                }
            }
            p.WaitForExit();

            // фінальне дочитування логу
            Thread.Sleep(500);
            try
            {
                if (File.Exists(logPath))
                {
                    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.Seek(lastSize, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);
                    string tail = sr.ReadToEnd();
                    if (!string.IsNullOrEmpty(tail))
                        foreach (var line in tail.Split('\n'))
                            if (!string.IsNullOrEmpty(line)) Emit(line.TrimEnd('\r'));
                }
            }
            catch { }

            sw.Stop();
            result.Seconds = sw.Elapsed.TotalSeconds;
            result.ExitCode = p.ExitCode;

            string dump = Path.Combine(outputDir, "dump.cs");
            if (File.Exists(dump))
            {
                result.DumpCsPath = dump;
                result.Success = true;
            }
            else
            {
                result.Error = $"dump.cs not created (exit={p.ExitCode}). Див. {logPath}";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Seconds = sw.Elapsed.TotalSeconds;
            result.Error = ex.Message;
        }
        return result;
    }

    public static string AutoFind()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Il2CppDumper", "Il2CppDumper.exe"),
            Path.Combine(AppContext.BaseDirectory, "Il2CppDumper.exe"),
            @"C:\Il2CppDumper\Il2CppDumper.exe",
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        return null;
    }
}