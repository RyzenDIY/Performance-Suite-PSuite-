using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PSuite.Core
{
    public class ModuleStateEntry
    {
        [JsonPropertyName("manifestVersion")]
        public string ManifestVersion { get; set; } = string.Empty;

        [JsonPropertyName("lastKnownState")]
        public ModuleState LastKnownState { get; set; } = ModuleState.Unknown;

        [JsonPropertyName("lastAppliedUtc")]
        public DateTime? LastAppliedUtc { get; set; }

        [JsonPropertyName("lastVerifiedUtc")]
        public DateTime? LastVerifiedUtc { get; set; }

        [JsonPropertyName("lastError")]
        public string? LastError { get; set; }

        [JsonPropertyName("capturePath")]
        public string? CapturePath { get; set; }
    }

    // Owns %LOCALAPPDATA%\PSuite\state.json and %LOCALAPPDATA%\PSuite\captures\.
    // This is the only class allowed to read/write these paths — per the
    // master context, Core is the sole owner of persisted state.
    public class StateStore
    {
        private readonly string _rootDir;
        private readonly string _stateFilePath;
        private readonly string _capturesDir;

        private Dictionary<string, ModuleStateEntry> _entries = new();

        public StateStore()
        {
            _rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PSuite");
            _stateFilePath = Path.Combine(_rootDir, "state.json");
            _capturesDir = Path.Combine(_rootDir, "captures");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_capturesDir);

            Load();
        }

        public ModuleStateEntry? Get(string moduleId)
            => _entries.TryGetValue(moduleId, out var entry) ? entry : null;

        public string GetCapturePath(string moduleId)
            => Path.Combine(_capturesDir, $"{moduleId}.json");

        public void RecordApplied(string moduleId, string manifestVersion, string capturePath)
        {
            _entries[moduleId] = new ModuleStateEntry
            {
                ManifestVersion = manifestVersion,
                LastKnownState = ModuleState.Applied,
                LastAppliedUtc = DateTime.UtcNow,
                LastVerifiedUtc = DateTime.UtcNow,
                LastError = null,
                CapturePath = capturePath
            };
            Save();
        }

        public void RecordRolledBack(string moduleId)
        {
            if (_entries.TryGetValue(moduleId, out var entry))
            {
                entry.LastKnownState = ModuleState.NotApplied;
                entry.LastVerifiedUtc = DateTime.UtcNow;
                entry.LastError = null;
            }
            Save();
        }

        public void RecordError(string moduleId, string error)
        {
            if (!_entries.TryGetValue(moduleId, out var entry))
            {
                entry = new ModuleStateEntry();
                _entries[moduleId] = entry;
            }
            entry.LastError = error;
            Save();
        }

        private void Load()
        {
            if (!File.Exists(_stateFilePath))
            {
                _entries = new Dictionary<string, ModuleStateEntry>();
                return;
            }

            try
            {
                var json = File.ReadAllText(_stateFilePath);
                _entries = JsonSerializer.Deserialize<Dictionary<string, ModuleStateEntry>>(json)
                           ?? new Dictionary<string, ModuleStateEntry>();
            }
            catch
            {
                // Corrupt state.json must not crash PSuite on startup.
                // Losing rollback bookkeeping is bad, but refusing to launch is worse.
                _entries = new Dictionary<string, ModuleStateEntry>();
            }
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
    }
}