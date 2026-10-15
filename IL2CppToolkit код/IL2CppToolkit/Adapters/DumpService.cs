 // language: C#, file: Adapters/DumpService.cs
using System;
using System.IO;
using System.Threading;

public static class DumpService
{
    // повний цикл: знайти гру → запустити dumper → розпарсити → зберегти snapshot
    public class Result
    {
        public bool Success;
        public string Error;
        public Dump Dump;
        public string SnapshotPath;
        public double TotalSeconds;
    }

    public static Result Acquire(
        Game game,
        string dumperExe,
        Action<string> onLine = null,
        CancellationToken cancel = default)
    {
        var res = new Result();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (game == null) { res.Error = "game is null"; return res; }
        if (string.IsNullOrEmpty(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
        { res.Error = $"executable not found: {game.ExecutablePath}"; return res; }
        if (string.IsNullOrEmpty(game.MetadataPath) || !File.Exists(game.MetadataPath))
        { res.Error = $"metadata not found: {game.MetadataPath}"; return res; }

        string outDir = Path.Combine(AppContext.BaseDirectory, "dump_out", game.Id);
        onLine?.Invoke($"▸ dumper: {dumperExe}");
        onLine?.Invoke($"▸ assembly: {game.ExecutablePath}");
        onLine?.Invoke($"▸ metadata: {game.MetadataPath}");
        onLine?.Invoke($"▸ output:  {outDir}");
        onLine?.Invoke("");

        var dr = Il2CppDumperAdapter.Run(dumperExe, game.ExecutablePath, game.MetadataPath, outDir, onLine, cancel);
        if (!dr.Success)
        {
            res.Error = dr.Error ?? "dumper failed";
            return res;
        }

        onLine?.Invoke("");
        onLine?.Invoke($"▸ parsing dump.cs...");

        var parseSw = System.Diagnostics.Stopwatch.StartNew();
        var dump = DumpParser.Parse(dr.DumpCsPath, game.Id);
        parseSw.Stop();
        onLine?.Invoke($"▸ parsed {dump.Classes.Count} classes in {parseSw.ElapsedMilliseconds}ms");

        // зберігаємо snapshot
        string snapDir = Paths.SnapshotDir(game.Id);
        string snapPath = Path.Combine(snapDir, dump.SuggestFilename());
        dump.Save(snapPath);
        onLine?.Invoke($"▸ snapshot saved: {snapPath}");

        sw.Stop();
        res.Success = true;
        res.Dump = dump;
        res.SnapshotPath = snapPath;
        res.TotalSeconds = sw.Elapsed.TotalSeconds;
        return res;
    }
}