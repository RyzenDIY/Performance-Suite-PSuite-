// language: C#, file: Adapters/SteamFinder.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

public static class SteamFinder
{
    // ─── відомі AppID
    public const int APPID_RUST = 252490;
    public const int APPID_AMONG_US = 945360;

    public static string FindSteamRoot()
    {
        string[] keys =
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
        };
        foreach (var k in keys)
        {
            try
            {
                object v = Registry.GetValue(k, "SteamPath", null)
                        ?? Registry.GetValue(k, "InstallPath", null);
                if (v is string s && Directory.Exists(s)) return s;
            }
            catch { }
        }
        string[] common =
        {
            @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam",
            @"C:\Steam", @"D:\Steam", @"D:\SteamLibrary", @"E:\SteamLibrary",
        };
        foreach (var p in common) if (Directory.Exists(p)) return p;
        return null;
    }

    public static List<string> FindLibraryFolders()
    {
        var result = new List<string>();
        string steam = FindSteamRoot();
        if (steam == null) return result;
        result.Add(steam);

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return result;
        try
        {
            string text = File.ReadAllText(vdf);
            foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
            {
                string p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (!result.Contains(p)) result.Add(p);
            }
        }
        catch { }
        return result;
    }

    // шукає папку гри за appId (через appmanifest_<appid>.acf)
    public static string FindGameFolder(int appId)
    {
        foreach (var lib in FindLibraryFolders())
        {
            string manifest = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest)) continue;
            try
            {
                string text = File.ReadAllText(manifest);
                var m = Regex.Match(text, "\"installdir\"\\s*\"([^\"]+)\"");
                if (!m.Success) continue;
                string dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
                if (Directory.Exists(dir)) return dir;
            }
            catch { }
        }
        return null;
    }

    // ─── таблиця відомих ігор для автодетекту
    public static List<(string id, string name, string process, string module, int appId, string launcher)> KnownGames()
    {
        return new List<(string, string, string, string, int, string)>
        {
            ("rust",     "Rust",     "RustClient", "GameAssembly.dll", APPID_RUST,     "steam"),
            ("among-us", "Among Us", "Among Us",   "GameAssembly.dll", APPID_AMONG_US, "steam"),
        };
    }
}