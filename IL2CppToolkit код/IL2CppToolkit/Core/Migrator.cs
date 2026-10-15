// language: C#, file: Core/Migrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class Migrator
{
    public static void RunOnce()
    {
        try
        {
            string[] gameIds = { "rust", "among-us" };
            foreach (var gameId in gameIds)
                MigrateGame(gameId);
        }
        catch { }
    }

    static void MigrateGame(string gameId)
    {
        try
        {
            // ─── labels
            string oldLabels = Path.Combine(Paths.Root, "labels", gameId + ".json");
            string newLabels = Paths.LabelsFile(gameId);

            if (File.Exists(oldLabels) && CountLabels(oldLabels) > 0)
            {
                if (!File.Exists(newLabels) || CountLabels(newLabels) == 0)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newLabels));
                        File.Copy(oldLabels, newLabels, true);
                        Log($"migrated labels: {oldLabels} → {newLabels}");
                    }
                    catch (Exception ex) { Log("labels copy fail: " + ex.Message); }
                }
            }

            // ─── signatures
            string oldSigs = Path.Combine(Paths.Root, "signatures", gameId + ".json");
            string newSigs = Paths.SignaturesFile(gameId);

            if (File.Exists(oldSigs) && CountSigs(oldSigs) > 0)
            {
                if (!File.Exists(newSigs) || CountSigs(newSigs) == 0)
                {
                    try { File.Copy(oldSigs, newSigs, true); Log($"migrated signatures"); }
                    catch { }
                }
            }

            // ─── templates
            string oldTpl = Path.Combine(Paths.Root, "templates", gameId + ".json");
            string newTpl = Paths.TemplatesFile(gameId);
            if (File.Exists(oldTpl))
            {
                if (!File.Exists(newTpl) || new FileInfo(newTpl).Length < 30)
                {
                    try { File.Copy(oldTpl, newTpl, true); Log($"migrated templates"); }
                    catch { }
                }
            }

            // ─── snapshots
            string oldSnapDir = Path.Combine(Paths.Root, "snapshots", gameId);
            if (Directory.Exists(oldSnapDir))
            {
                string newSnapDir = Paths.SnapshotsDir(gameId);
                foreach (var f in Directory.GetFiles(oldSnapDir, "*.json"))
                {
                    string target = Path.Combine(newSnapDir, Path.GetFileName(f));
                    if (File.Exists(target)) continue;
                    try { File.Copy(f, target, false); Log($"migrated snapshot {Path.GetFileName(f)}"); }
                    catch { }
                }
            }
        }
        catch { }
    }

    // ─── рахуємо скільки реальних записів у JSON
    static int CountLabels(string path)
    {
        try
        {
            var col = JsonStore.Load<LabelCollection>(path);
            return col?.Labels?.Count ?? 0;
        }
        catch { return 0; }
    }

    static int CountSigs(string path)
    {
        try
        {
            var col = JsonStore.Load<SignatureCollection>(path);
            return col?.Signatures?.Count ?? 0;
        }
        catch { return 0; }
    }

    static void Log(string s)
    {
        try
        {
            File.AppendAllText(Path.Combine(Paths.Root, "logs", "migrator.log"),
                DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n");
        }
        catch { }
    }
}