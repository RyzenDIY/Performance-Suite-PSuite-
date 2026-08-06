using System.Collections.ObjectModel;

namespace PSuite.Models
{
    public enum AnalysisLineStatus
    {
        // No color — plain spec/info line (e.g. "CPU: Intel i7-2600").
        // There's no "good/bad" for a model name, so it stays neutral.
        Neutral,
        Ok,
        Warning,
        Unknown
    }

    public class AnalysisLine
    {
        public string Text { get; set; } = string.Empty;
        public AnalysisLineStatus Status { get; set; } = AnalysisLineStatus.Neutral;
        // Shown on hover — what this line actually means, in plain
        // language. Null/empty means no tooltip (most spec lines don't
        // need one — "CPU: Intel i7-2600" is self-explanatory).
        public string? Tooltip { get; set; }
    }

    public static class AnalysisLineExtensions
    {
        // Every existing call site was ".Lines.Add(someString)" — this
        // lets all 46+ of them keep working completely unchanged
        // (defaulting to a plain neutral line, no tooltip), while new
        // code can opt into status/tooltip explicitly where it's
        // actually meaningful.
        public static void AddLine(this ObservableCollection<AnalysisLine> lines, string text,
            AnalysisLineStatus status = AnalysisLineStatus.Neutral, string? tooltip = null)
        {
            lines.Add(new AnalysisLine { Text = text, Status = status, Tooltip = tooltip });
        }
    }

    // A single titled card on the "Анализ" page — a heading plus a clean,
    // evenly-spaced list of lines underneath (CPU / RAM / GPU / Windows /
    // Безопасность etc, one card each) instead of one dense multi-column
    // block of text.
    public class AnalysisCardView
    {
        public string Title { get; set; } = string.Empty;
        public ObservableCollection<AnalysisLine> Lines { get; set; } = new();
    }
}
