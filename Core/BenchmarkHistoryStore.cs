using System;
using System.IO;
using System.Text.Json;

namespace PSuite.Core
{
    // Owns %LOCALAPPDATA%\PSuite\benchmark-last.json — the single most
    // recent full benchmark run. Without this, every run is just numbers
    // in a vacuum; with it, a run can say "12% faster than last time",
    // which is the entire point of a before/after tweak-comparison tool.
    //
    // Also owns benchmark-baseline.json — the FIRST run ever made on this
    // machine, kept forever (until explicitly reset). The Performance
    // Score is computed against this fixed baseline rather than the
    // previous run, so it doesn't drift over time and always answers one
    // question: "compared to when I started using PSuite, how is this
    // machine doing right now?"
    public static class BenchmarkHistoryStore
    {
        private static string RootDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PSuite");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string LastFilePath => Path.Combine(RootDir, "benchmark-last.json");
        private static string BaselineFilePath => Path.Combine(RootDir, "benchmark-baseline.json");
        private static string BestScoreFilePath => Path.Combine(RootDir, "benchmark-best-score.json");

        public static BenchmarkSuiteResult? LoadLast() => LoadFrom(LastFilePath);

        public static void SaveLast(BenchmarkSuiteResult result) => SaveTo(LastFilePath, result);

        public static BenchmarkSuiteResult? LoadBaseline() => LoadFrom(BaselineFilePath);

        // Only ever writes the baseline if one doesn't exist yet — that's
        // what makes it a stable reference point instead of just another
        // "last run".
        public static void SaveBaselineIfMissing(BenchmarkSuiteResult result)
        {
            if (LoadBaseline() != null) return;
            SaveTo(BaselineFilePath, result);
        }

        public static void ResetBaseline()
        {
            try { if (File.Exists(BaselineFilePath)) File.Delete(BaselineFilePath); }
            catch { /* best-effort */ }
        }

        // The best Performance Score ever measured on this machine since
        // the baseline was set. Real, persisted, comparable — used only to
        // decide whether to show a "new record" moment in the UI. Never
        // fabricated: if nothing was ever measured, this returns null and
        // the UI simply won't claim a record.
        public static double? LoadBestScore()
        {
            try
            {
                if (!File.Exists(BestScoreFilePath)) return null;
                var json = File.ReadAllText(BestScoreFilePath);
                return JsonSerializer.Deserialize<BestScoreData>(json)?.Score;
            }
            catch
            {
                return null;
            }
        }

        // Returns true only if `score` genuinely beats the previously
        // recorded best (or is the first score ever recorded after a
        // baseline exists). Saves the new best as a side effect.
        public static bool SaveBestScoreIfHigher(double score)
        {
            var current = LoadBestScore();
            if (current.HasValue && score <= current.Value) return false;

            try
            {
                var json = JsonSerializer.Serialize(new BestScoreData { Score = score },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(BestScoreFilePath, json);
            }
            catch
            {
                // Best-effort — a failed save shouldn't block the UI moment.
            }

            return true;
        }

        public static void ResetBestScore()
        {
            try { if (File.Exists(BestScoreFilePath)) File.Delete(BestScoreFilePath); }
            catch { /* best-effort */ }
        }

        private class BestScoreData
        {
            public double Score { get; set; }
        }

        private static BenchmarkSuiteResult? LoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<BenchmarkSuiteResult>(json);
            }
            catch
            {
                // Corrupt/missing history should never block a new run.
                return null;
            }
        }

        private static void SaveTo(string path, BenchmarkSuiteResult result)
        {
            try
            {
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Best-effort — failing to save history shouldn't fail the run.
            }
        }
    }
}
