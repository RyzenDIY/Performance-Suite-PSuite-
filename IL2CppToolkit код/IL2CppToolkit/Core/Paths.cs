// language: C#, file: Core/Paths.cs
using System;
using System.IO;

public static class Paths
{
    // ═══════════════════════════════════════════════════════════
    //   корінь
    // ═══════════════════════════════════════════════════════════

    public static string Root
    {
        get
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appdata, "IL2CppToolkit");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //   глобальні файли
    // ═══════════════════════════════════════════════════════════

    public static string GamesFile => Path.Combine(Root, "games.json");

    public static string LogsDir
    {
        get
        {
            var d = Path.Combine(Root, "logs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //   корінь всіх ігор (може бути перевизначений у Settings)
    // ═══════════════════════════════════════════════════════════

    public static string GamesRoot
    {
        get
        {
            try
            {
                string custom = AppConfig.Current?.CustomGamesRoot;
                if (!string.IsNullOrEmpty(custom))
                {
                    Directory.CreateDirectory(custom);
                    return custom;
                }
            }
            catch { }

            var def = Path.Combine(Root, "games");
            Directory.CreateDirectory(def);
            return def;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //   per-game структура
    // ═══════════════════════════════════════════════════════════

    public static string GameDir(string gameId)
    {
        string dir = Path.Combine(GamesRoot, gameId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string InfoFile(string gameId) => Path.Combine(GameDir(gameId), "info.json");
    public static string LabelsFile(string gameId) => Path.Combine(GameDir(gameId), "labels.json");
    public static string SignaturesFile(string gameId) => Path.Combine(GameDir(gameId), "signatures.json");
    public static string TemplatesFile(string gameId) => Path.Combine(GameDir(gameId), "templates.json");
    public static string ProfileFile(string gameId) => Path.Combine(GameDir(gameId), "profile.json");

    public static string SnapshotsDir(string gameId)
    {
        var d = Path.Combine(GameDir(gameId), "snapshots");
        Directory.CreateDirectory(d);
        return d;
    }

    public static string DumpDir(string gameId)
    {
        var d = Path.Combine(GameDir(gameId), "dump");
        Directory.CreateDirectory(d);
        return d;
    }

    public static string ExportsDir(string gameId)
    {
        var d = Path.Combine(GameDir(gameId), "exports");
        Directory.CreateDirectory(d);
        return d;
    }

    public static string GameLogsDir(string gameId)
    {
        var d = Path.Combine(GameDir(gameId), "logs");
        Directory.CreateDirectory(d);
        return d;
    }

    public static string SnapshotPath(string gameId, string filename)
        => Path.Combine(SnapshotsDir(gameId), filename);

    public static string ExportPath(string gameId, string filename)
        => Path.Combine(ExportsDir(gameId), filename);

    public static string DumpPath(string gameId, string filename)
        => Path.Combine(DumpDir(gameId), filename);

    // ═══════════════════════════════════════════════════════════
    //   backwards compatibility
    // ═══════════════════════════════════════════════════════════

    public static string SnapshotDir(string gameId) => SnapshotsDir(gameId);

    public static string LabelsDir
    {
        get
        {
            Directory.CreateDirectory(GamesRoot);
            return GamesRoot;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //   helpers
    // ═══════════════════════════════════════════════════════════

    public static string SafeFileName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unnamed";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Trim();
    }

    public static long DirSize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    public static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {units[i]}";
    }
}