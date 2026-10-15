// language: C#, file: Core/AppConfig.cs
using System;
using System.IO;

public class AppConfig
{
    public string CustomGamesRoot { get; set; }   // null = default %APPDATA%\IL2CppToolkit\games
    public string Il2CppDumperPath { get; set; }   // null = auto-find
    public bool ShowStartupHelp { get; set; } = true;
    public bool ShowStartupAlways { get; set; } = true;  // false = тільки перший запуск
    public string LastSeenVersion { get; set; }
    public DateTime FirstRun { get; set; } = DateTime.UtcNow;

    static AppConfig _current;

    public static AppConfig Current
    {
        get
        {
            if (_current == null) _current = Load();
            return _current;
        }
    }

    static string ConfigPath => Path.Combine(Paths.Root, "config.json");

    public static AppConfig Load()
    {
        try { return JsonStore.Load<AppConfig>(ConfigPath, () => new AppConfig()); }
        catch { return new AppConfig(); }
    }

    public void Save()
    {
        try { JsonStore.Save(ConfigPath, this); }
        catch { }
    }

    public static void Reload()
    {
        _current = Load();
    }
}