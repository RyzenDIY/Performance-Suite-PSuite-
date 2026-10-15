// language: C#, file: Domain/Game.cs
using System;
using System.Collections.Generic;

public class Game
{
    public string Id { get; set; }   // "rust", "tarkov"
    public string Name { get; set; }   // "Rust"
    public string Process { get; set; }   // "RustClient"
    public string Module { get; set; }   // "GameAssembly.dll"
    public string MetadataPath { get; set; }   // повний шлях до global-metadata.dat
    public string ExecutablePath { get; set; }   // повний шлях до GameAssembly.dll
    public string InstallDir { get; set; }   // корінь гри
    public string Launcher { get; set; }   // "steam" | "epic" | "standalone"
    public int SteamAppId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public List<string> Profiles { get; set; } = new();  // назви профілів у profiles/

    public override string ToString() => $"{Name} ({Id})";
}

public class GameRegistry
{
    public List<Game> Games { get; set; } = new();

    public Game Find(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return null;
        foreach (var g in Games)
            if (string.Equals(g.Id, idOrName, StringComparison.OrdinalIgnoreCase)) return g;
        foreach (var g in Games)
            if (string.Equals(g.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return g;
        return null;
    }

    public void Upsert(Game g)
    {
        for (int i = 0; i < Games.Count; i++)
        {
            if (Games[i].Id == g.Id) { Games[i] = g; return; }
        }
        Games.Add(g);
    }

    public static GameRegistry Load() => JsonStore.Load(Paths.GamesFile, () => new GameRegistry());
    public void Save() => JsonStore.Save(Paths.GamesFile, this);
}