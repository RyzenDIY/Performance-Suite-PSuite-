// language: C#, file: Domain/Dump.cs
using System;
using System.Collections.Generic;
using System.IO;

public enum DumpSource { Offline, Runtime }

public class Dump
{
    public string GameId { get; set; }
    public int Version { get; set; }        // Unity metadata version, 0 = unknown
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public DumpSource Source { get; set; } = DumpSource.Offline;
    public string Note { get; set; }
    public string SourceFile { get; set; }        // шлях до dump.cs або "runtime"
    public string ModuleHash { get; set; }        // SHA256 перших 1MB GameAssembly.dll — для матчингу версій
    public Dictionary<string, ClassModel> Classes { get; set; } = new();

    public ClassModel GetClass(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Classes.TryGetValue(name, out var c);
        return c;
    }

    // швидкий save/load — snapshot
    public void Save(string path) => JsonStore.Save(path, this);

    public static Dump Load(string path) => JsonStore.Load<Dump>(path);
    public static Dump LoadForGame(string gameId, string filename)
        => Load(Path.Combine(Paths.SnapshotDir(gameId), filename));

    public string SuggestFilename()
    {
        string stamp = CapturedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"{stamp}.json";
    }
}