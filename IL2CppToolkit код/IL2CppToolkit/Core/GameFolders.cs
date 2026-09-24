// language: C#, file: Core/GameFolders.cs
using System;
using System.IO;

public static class GameFolders
{
    // створює повну структуру папок для гри + info.json
    // викликається при додаванні/оновленні гри
    public static string EnsureStructure(Game game)
    {
        if (game == null || string.IsNullOrEmpty(game.Id))
            throw new ArgumentException("game is null or has no Id");

        string root = Paths.GameDir(game.Id);

        // підпапки
        Directory.CreateDirectory(Paths.SnapshotsDir(game.Id));
        Directory.CreateDirectory(Paths.DumpDir(game.Id));
        Directory.CreateDirectory(Paths.ExportsDir(game.Id));
        Directory.CreateDirectory(Paths.GameLogsDir(game.Id));

        // info.json — копія метаданих
        try
        {
            JsonStore.Save(Paths.InfoFile(game.Id), game);
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(Paths.GameLogsDir(game.Id), "errors.log"),
                DateTime.Now.ToString("HH:mm:ss") + " info.json fail: " + ex.Message + "\n");
        }

        // README.txt — коротка довідка
        try
        {
            string readme = Path.Combine(root, "README.txt");
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme,
                    $"IL2CppToolkit — game folder for '{game.Name}'\r\n" +
                    $"created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"\r\n" +
                    $"structure:\r\n" +
                    $"  info.json          — game metadata (paths, launcher, appid)\r\n" +
                    $"  labels.json        — semantic labels (health, position, ...)\r\n" +
                    $"  templates.json     — chain templates (LocalPlayer → health)\r\n" +
                    $"  signatures.json    — byte patterns for statics\r\n" +
                    $"  profile.json       — per-game settings\r\n" +
                    $"  snapshots\\        — historical dumps (2026-09-19_...json)\r\n" +
                    $"  dump\\             — raw Il2CppDumper output (dump.cs + il2cpp.h)\r\n" +
                    $"  exports\\          — generated offset files (RustOffsets.txt, offsets.hpp, ...)\r\n" +
                    $"  logs\\             — error logs\r\n");
            }
        }
        catch { }

        return root;
    }

    // перевіряє чи папка має всі потрібні файли, повертає список відсутніх
    public static string[] CheckIntegrity(string gameId)
    {
        var missing = new System.Collections.Generic.List<string>();

        if (!File.Exists(Paths.LabelsFile(gameId))) missing.Add("labels.json");
        if (!File.Exists(Paths.SignaturesFile(gameId))) missing.Add("signatures.json");
        if (!File.Exists(Paths.TemplatesFile(gameId))) missing.Add("templates.json");

        return missing.ToArray();
    }

    // виводить дерево вмісту для показу в UI
    public static string GetTreeString(string gameId)
    {
        try
        {
            string root = Paths.GameDir(gameId);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(root);

            var files = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly);
            var dirs = Directory.GetDirectories(root);

            for (int i = 0; i < files.Length; i++)
            {
                string mark = (i == files.Length - 1 && dirs.Length == 0) ? "└── " : "├── ";
                var fi = new FileInfo(files[i]);
                sb.AppendLine($"{mark}{fi.Name,-30}  {fi.Length,8} b");
            }

            for (int i = 0; i < dirs.Length; i++)
            {
                string mark = (i == dirs.Length - 1) ? "└── " : "├── ";
                string name = Path.GetFileName(dirs[i]);
                int count = Directory.GetFiles(dirs[i]).Length;
                sb.AppendLine($"{mark}{name,-30}  [{count} files]");
            }

            return sb.ToString();
        }
        catch (Exception ex) { return "error: " + ex.Message; }
    }
}