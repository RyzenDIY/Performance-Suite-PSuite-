namespace PSuite.Models
{
    // Display-ready wrapper around Core.LogEntry for the Логи tab.
    public class LogEntryView
    {
        public string IconGlyph { get; set; } = "\uE73E"; // checkmark
        public string StatusBrushKey { get; set; } = "PSuiteStatusSafeFgBrush";
        public string Title { get; set; } = string.Empty;      // "Отключение трекинга — Применить"
        public string Subtitle { get; set; } = string.Empty;   // "09.07.2026 22:40 · успешно"
    }
}