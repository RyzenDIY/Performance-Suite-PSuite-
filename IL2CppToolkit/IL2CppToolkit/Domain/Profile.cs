// language: C#, file: Domain/Profile.cs
using System;
using System.Collections.Generic;

public class ExportTarget
{
    public string Format { get; set; }   // "rust-txt", "cpp-header", "json", "cs-class"
    public string OutputPath { get; set; }
    public bool Enabled { get; set; } = true;
}

public class Profile
{
    public string GameId { get; set; }
    public string Name { get; set; }         // "default", "cheat-build"
    public string Description { get; set; }

    // які семантичні мітки потрібні цьому профілю
    public List<string> WantedSemantics { get; set; } = new();

    // куди експортувати
    public List<ExportTarget> Exports { get; set; } = new();

    // hook-назви (кастомні дії, реалізуються в коді)
    public List<string> Hooks { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static Profile Load(string gameId)
    {
        var p = JsonStore.Load<Profile>(Paths.ProfileFile(gameId));
        if (p == null)
        {
            p = CreateDefault(gameId);
        }
        return p;
    }

    public static Profile CreateDefault(string gameId)
    {
        var p = new Profile
        {
            GameId = gameId,
            Name = "default",
            Description = "auto-generated default profile",
            WantedSemantics = new List<string>
            {
                "clientEntities", "vals", "baseTransform", "position",
                "displayName", "health", "playerFlags", "viewMatrix"
            },
            Exports = new List<ExportTarget>
            {
                new ExportTarget { Format = "json", OutputPath = "offsets.json" }
            }
        };
        p.Save();
        return p;
    }

    public void Save() => JsonStore.Save(Paths.ProfileFile(GameId), this);
}