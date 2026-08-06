using Microsoft.UI.Xaml.Media;

namespace PSuite.Models
{
    // One square card per benchmark test — mirrors AnalysisCardView's
    // "title + lines" look so the Benchmark and Анализ pages feel like
    // the same product instead of two different UI styles.
    public class BenchmarkResultCardView
    {
        public string Title { get; set; } = string.Empty;
        public string ValueText { get; set; } = string.Empty;
        public string DeltaText { get; set; } = string.Empty;
        public Brush DeltaBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        public string Tooltip { get; set; } = string.Empty;
    }
}
