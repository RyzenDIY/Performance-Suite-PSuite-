// language: C#, file: CliRunner.cs
using System;
using System.IO;
using System.Linq;

public static class CliRunner
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "IL2CppToolkit — CLI";

        C(ConsoleColor.Cyan, "IL2CppToolkit CLI");
        Console.WriteLine();

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintHelp();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "--dump": return CmdDump(args);
                case "--diff": return CmdDiff(args);
                case "--export": return CmdExport(args);
                case "--list": return CmdList(args);
                case "--info": return CmdInfo(args);
                default:
                    Err($"Unknown command: {cmd}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Err("EX: " + ex.Message);
            return 2;
        }
    }

    // ═══════════════════════════════════════════
    //   --dump <gameId>
    // ═══════════════════════════════════════════

    static int CmdDump(string[] args)
    {
        string gameId = args.Length >= 2 ? args[1] : "rust";
        H($"dump · {gameId}");

        var reg = GameRegistry.Load();
        var game = reg.Find(gameId);
        if (game == null) { Err($"game '{gameId}' not registered"); return 1; }

        GameFolders.EnsureStructure(game);
        Info($"folder: {Paths.GameDir(game.Id)}");

        string dumperExe = Il2CppDumperAdapter.AutoFind();
        if (dumperExe == null) { Err("Il2CppDumper.exe not found"); return 1; }

        if (game.ExecutablePath == null || !File.Exists(game.ExecutablePath))
        { Err("GameAssembly.dll missing"); return 1; }
        if (game.MetadataPath == null || !File.Exists(game.MetadataPath))
        { Err("global-metadata.dat missing"); return 1; }

        // вихід у per-game dump folder
        string outDir = Paths.DumpDir(game.Id);
        Info($"output: {outDir}");

        var res = DumpService.Acquire(game, dumperExe, Console.WriteLine);
        if (!res.Success) { Err(res.Error); return 1; }

        Ok($"dump done in {res.TotalSeconds:F1}s · {res.Dump.Classes.Count} classes");
        Ok($"snapshot: {res.SnapshotPath}");
        return 0;
    }

    // ═══════════════════════════════════════════
    //   --diff <gameId> [--apply]
    // ═══════════════════════════════════════════

    static int CmdDiff(string[] args)
    {
        string gameId = args.Length >= 2 ? args[1] : "rust";
        bool apply = args.Contains("--apply");
        H($"diff · {gameId}  {(apply ? "[APPLY]" : "")}");

        var reg = GameRegistry.Load();
        var game = reg.Find(gameId);
        if (game == null) { Err("game not found"); return 1; }

        string snapDir = Paths.SnapshotsDir(game.Id);
        var files = Directory.GetFiles(snapDir, "*.json").OrderByDescending(x => x).ToList();
        if (files.Count < 2) { Err("need 2+ snapshots"); return 1; }

        var newDump = Dump.Load(files[0]);
        var oldDump = Dump.Load(files[1]);
        Info($"old: {Path.GetFileName(files[1])}");
        Info($"new: {Path.GetFileName(files[0])}");
        Console.WriteLine();

        var labels = LabelCollection.Load(game.Id);
        var changes = DiffService.Compare(labels, newDump);

        int ok = changes.Count(x => x.Status == "ok");
        int moved = changes.Count(x => x.Status == "moved" || x.Status == "renamed");
        int gone = changes.Count(x => x.Status == "missing" || x.Status == "class-gone");

        foreach (var c in changes.OrderBy(x => x.Status))
        {
            var col = c.Status switch
            {
                "ok" => ConsoleColor.Green,
                "moved" => ConsoleColor.Yellow,
                "renamed" => ConsoleColor.DarkYellow,
                "missing" => ConsoleColor.Red,
                "class-gone" => ConsoleColor.DarkRed,
                _ => ConsoleColor.Gray
            };
            Console.ForegroundColor = col;
            Console.WriteLine($"  {c.Semantic,-24} {c.Status,-12} old=0x{c.OldOffset:X}  new=0x{c.NewOffset:X}  score={c.Score:F2}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Info($"total={changes.Count}  ok={ok}  moved={moved}  missing={gone}");

        if (apply)
        {
            int n = DiffService.Apply(changes, labels);
            Ok($"applied {n} labels → {Paths.LabelsFile(game.Id)}");
        }
        return 0;
    }

    // ═══════════════════════════════════════════
    //   --export <gameId> [--format=txt,hpp,cs,json]
    // ═══════════════════════════════════════════

    static int CmdExport(string[] args)
    {
        string gameId = "rust";
        for (int i = 1; i < args.Length; i++)
            if (!args[i].StartsWith("--")) { gameId = args[i]; break; }

        string fmt = "txt,hpp,cs";
        foreach (var a in args)
            if (a.StartsWith("--format="))
                fmt = a.Substring("--format=".Length);

        H($"export · {gameId}  [{fmt}]");

        var reg = GameRegistry.Load();
        var game = reg.Find(gameId);
        if (game == null) { Err("game not found"); return 1; }

        GameFolders.EnsureStructure(game);
        var col = LabelCollection.Load(game.Id);
        if (col.Labels.Count == 0) { Err("no labels"); return 1; }

        string dir = Paths.ExportsDir(game.Id);
        var fmts = fmt.Split(',').Select(x => x.Trim().ToLowerInvariant()).ToArray();
        int n = 0;

        if (fmts.Contains("txt")) { File.WriteAllText(Path.Combine(dir, "RustOffsets.txt"), ExportService.RenderTxt(col)); Ok("exports/RustOffsets.txt"); n++; }
        if (fmts.Contains("hpp")) { File.WriteAllText(Path.Combine(dir, "offsets.hpp"), ExportService.RenderCppHeader(col)); Ok("exports/offsets.hpp"); n++; }
        if (fmts.Contains("cs")) { File.WriteAllText(Path.Combine(dir, "Offsets.cs"), ExportService.RenderCsClass(col)); Ok("exports/Offsets.cs"); n++; }
        if (fmts.Contains("json")) { File.WriteAllText(Path.Combine(dir, "offsets.json"), ExportService.RenderJson(col)); Ok("exports/offsets.json"); n++; }

        Console.WriteLine();
        Ok($"{n} files → {dir}");
        return 0;
    }

    // ═══════════════════════════════════════════
    //   --list
    // ═══════════════════════════════════════════

    static int CmdList(string[] args)
    {
        H("registered games");
        var reg = GameRegistry.Load();

        if (reg.Games.Count == 0)
        {
            Console.WriteLine("  (no games registered)");
            return 0;
        }

        foreach (var g in reg.Games.OrderBy(x => x.Id))
        {
            var labels = LabelCollection.Load(g.Id);
            var snapshots = Directory.Exists(Paths.SnapshotsDir(g.Id))
                ? Directory.GetFiles(Paths.SnapshotsDir(g.Id), "*.json").Length
                : 0;

            Console.Write($"  {g.Id,-12}  {g.Name,-20}  labels={labels.Labels.Count,-3}  snapshots={snapshots,-3}  ");
            Console.ForegroundColor = g.ExecutablePath != null && File.Exists(g.ExecutablePath) ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(g.ExecutablePath != null && File.Exists(g.ExecutablePath) ? "✓" : "✗");
            Console.ResetColor();
            Console.WriteLine();
        }
        return 0;
    }

    // ═══════════════════════════════════════════
    //   --info <gameId>
    // ═══════════════════════════════════════════

    static int CmdInfo(string[] args)
    {
        string gameId = args.Length >= 2 ? args[1] : "rust";
        H($"info · {gameId}");

        var reg = GameRegistry.Load();
        var game = reg.Find(gameId);
        if (game == null) { Err("game not found"); return 1; }

        Console.WriteLine($"  Name:          {game.Name}");
        Console.WriteLine($"  Id:            {game.Id}");
        Console.WriteLine($"  Process:       {game.Process}");
        Console.WriteLine($"  Module:        {game.Module}");
        Console.WriteLine($"  Launcher:      {game.Launcher}  appid={game.SteamAppId}");
        Console.WriteLine($"  Install dir:   {game.InstallDir}");
        Console.WriteLine($"  GameAssembly:  {game.ExecutablePath}");
        Console.WriteLine($"  Metadata:      {game.MetadataPath}");
        Console.WriteLine();
        Console.WriteLine($"  Folder:        {Paths.GameDir(game.Id)}");
        Console.WriteLine();
        Console.WriteLine(GameFolders.GetTreeString(game.Id));

        return 0;
    }

    // ═══════════════════════════════════════════
    //   helpers

    static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  IL2CppToolkit.exe [--cli] <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  --dump <gameId>                          run Il2CppDumper, save snapshot");
        Console.WriteLine("  --diff <gameId> [--apply]                compare last 2 snapshots");
        Console.WriteLine("  --export <gameId> [--format=txt,hpp,cs,json]");
        Console.WriteLine("  --list                                   list registered games");
        Console.WriteLine("  --info <gameId>                          show game details + folder tree");
        Console.WriteLine("  --help, -h                               this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  IL2CppToolkit.exe --cli --dump rust");
        Console.WriteLine("  IL2CppToolkit.exe --cli --diff rust --apply");
        Console.WriteLine("  IL2CppToolkit.exe --cli --export rust --format=txt,cs");
        Console.WriteLine("  IL2CppToolkit.exe --cli --info rust");
    }

    static void H(string s)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("── " + s + " " + new string('─', Math.Max(2, 60 - s.Length)));
        Console.ResetColor();
    }
    static void Info(string s) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine("▸ " + s); Console.ResetColor(); }
    static void Ok(string s) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("✓ " + s); Console.ResetColor(); }
    static void Err(string s) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("✗ " + s); Console.ResetColor(); }
    static void C(ConsoleColor c, string s) { Console.ForegroundColor = c; Console.WriteLine(s); Console.ResetColor(); }
}