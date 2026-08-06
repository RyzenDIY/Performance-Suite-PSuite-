using System;
using System.IO;
using System.Text.Json;

namespace PSuite.Core
{
    public class AppSettings
    {
        public bool ShowExperimentalModules { get; set; } = true;
        public string AccentColor { get; set; } = "#FF1D9E75";
        public bool CreateRestorePointBeforeApply { get; set; } = false;
        public bool ColorFilterEnabled { get; set; } = false;
        public int ColorFilterBrightness { get; set; } = 0;
        public int ColorFilterContrastPercent { get; set; } = 100;
        public double ColorFilterGamma { get; set; } = 1.0;
    }

    // Owns %LOCALAPPDATA%\PSuite\settings.json — app-wide preferences,
    // separate from per-module state in StateStore.
    public class SettingsStore
    {
        private readonly string _filePath;
        public AppSettings Current { get; private set; }

        public SettingsStore()
        {
            var rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PSuite");
            Directory.CreateDirectory(rootDir);
            _filePath = Path.Combine(rootDir, "settings.json");
            Current = Load();
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        private AppSettings Load()
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}