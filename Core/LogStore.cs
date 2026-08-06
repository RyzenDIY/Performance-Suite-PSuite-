using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PSuite.Core
{
    public class LogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string ModuleId { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Details { get; set; }
    }

    public class LogStore
    {
        private readonly string _logFilePath;

        public LogStore()
        {
            var rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PSuite");
            Directory.CreateDirectory(rootDir);
            _logFilePath = Path.Combine(rootDir, "operations.log");
        }

        public void Append(LogEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(_logFilePath, json + Environment.NewLine);
        }

        public List<LogEntry> ReadRecent(int maxCount = 100)
        {
            if (!File.Exists(_logFilePath))
                return new List<LogEntry>();

            var entries = new List<LogEntry>();
            foreach (var line in File.ReadLines(_logFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<LogEntry>(line);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                    // Skip a corrupted line rather than failing the whole read.
                }
            }

            return entries
                .OrderByDescending(e => e.TimestampUtc)
                .Take(maxCount)
                .ToList();
        }

        public void Clear()
        {
            if (File.Exists(_logFilePath))
                File.Delete(_logFilePath);
        }
    }
}