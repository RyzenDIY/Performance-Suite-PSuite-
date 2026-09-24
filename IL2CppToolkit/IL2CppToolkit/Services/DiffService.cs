// language: C#, file: Services/DiffService.cs
using System;
using System.Collections.Generic;
using System.Linq;

public class FieldChange
{
    public string Semantic;      // health, position
    public string Class;
    public string OldField;      // старе ім'я (може бути %hash)
    public int OldOffset;
    public string OldType;

    public string NewField;
    public int NewOffset;
    public string NewType;

    public string Status;        // ok / moved / renamed / missing / class-gone
    public float Score;         // 0..1
    public string Note;
}

public static class DiffService
{
    public static List<FieldChange> Compare(LabelCollection labels, Dump newDump)
    {
        var result = new List<FieldChange>();
        if (labels == null || newDump == null) return result;

        foreach (var lb in labels.Labels)
        {
            var fc = new FieldChange
            {
                Semantic = lb.Semantic,
                Class = lb.Class,
                OldField = lb.Field,
                OldOffset = lb.Offset,
                OldType = lb.Type,
                Status = "missing",
                Score = 0,
            };
            result.Add(fc);

            if (!newDump.Classes.TryGetValue(lb.Class, out var cls)) { fc.Status = "class-gone"; continue; }

            // 1. той самий field + offset
            var exact = cls.Fields.FirstOrDefault(f => f.Name == lb.Field && f.Offset == lb.Offset);
            if (exact != null)
            {
                fc.NewField = exact.Name; fc.NewOffset = exact.Offset; fc.NewType = exact.Type;
                fc.Status = "ok"; fc.Score = 1.0f;
                continue;
            }

            // 2. той самий field, інший offset
            var sameName = cls.Fields.FirstOrDefault(f => f.Name == lb.Field);
            if (sameName != null)
            {
                fc.NewField = sameName.Name; fc.NewOffset = sameName.Offset; fc.NewType = sameName.Type;
                fc.Status = "moved"; fc.Score = 0.9f;
                fc.Note = $"offset зсунувся на {sameName.Offset - lb.Offset:+#;-#;0}";
                continue;
            }

            // 3. fuzzy: того ж типу + близько до старого offset
            var candidates = cls.Fields
                .Where(f => f.Type == lb.Type)
                .Select(f => new { f, delta = Math.Abs(f.Offset - lb.Offset) })
                .OrderBy(x => x.delta)
                .ToList();

            if (candidates.Count > 0)
            {
                var best = candidates[0];
                float score = best.delta == 0 ? 0.85f
                            : best.delta <= 0x20 ? 0.7f
                            : best.delta <= 0x80 ? 0.5f
                            : 0.3f;
                if (score >= 0.5f)
                {
                    fc.NewField = best.f.Name; fc.NewOffset = best.f.Offset; fc.NewType = best.f.Type;
                    fc.Status = "renamed"; fc.Score = score;
                    fc.Note = $"схоже на те саме ({lb.Type}, delta={best.delta:X})";
                    continue;
                }
            }

            fc.Status = "missing";
        }
        return result;
    }

    public static int Apply(List<FieldChange> changes, LabelCollection labels)
    {
        int n = 0;
        foreach (var c in changes)
        {
            if (c.Status != "moved" && c.Status != "renamed" && c.Status != "ok") continue;
            var lb = labels.Labels.FirstOrDefault(x => x.Semantic == c.Semantic && x.Class == c.Class);
            if (lb == null) continue;

            lb.PushAlias();  // зберігаємо старе як alias
            lb.Field = c.NewField;
            lb.Offset = c.NewOffset;
            lb.Type = c.NewType;
            lb.Verified = DateTime.UtcNow;
            n++;
        }
        labels.Save();
        return n;
    }
}