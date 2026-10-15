// language: C#, file: Adapters/DumpParser.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

public static class DumpParser
{
    static readonly Regex ClassRx = new(
        @"\b(?:public|private|internal|protected)?\s*(?:sealed\s+|abstract\s+|static\s+)?(class|struct)\s+([A-Za-z_][\w`<>]*)",
        RegexOptions.Compiled);

    static readonly Regex AddrRx = new(
        @"\[Address\(RVA\s*=\s*""(0x[0-9A-Fa-f]+)""", RegexOptions.Compiled);

    static readonly Regex HexRx = new(
        @"//\s*0x([0-9A-Fa-f]+)", RegexOptions.Compiled);

    static readonly Regex TypeDefRx = new(
        @"TypeDefIndex:\s*(\d+)", RegexOptions.Compiled);

    // ═══════════════════════════════════════════════════════════
    //   парсинг dump.cs → Domain.Dump
    // ═══════════════════════════════════════════════════════════

    public static Dump Parse(string path, string gameId)
    {
        var dump = new Dump
        {
            GameId = gameId,
            Source = DumpSource.Offline,
            SourceFile = path,
            CapturedAt = DateTime.UtcNow,
        };

        if (!File.Exists(path)) return dump;

        ClassModel current = null;
        string pendingRva = null;
        int depth = 0;

        foreach (var raw in File.ReadLines(path))
        {
            string line = raw.TrimEnd();
            string t = line.TrimStart();
            if (t.Length == 0) continue;

            // ─── клас / struct
            var cm = ClassRx.Match(t);
            if (cm.Success && (t.Contains("class ") || t.Contains("struct ")))
            {
                string name = cm.Groups[2].Value;

                if (!dump.Classes.TryGetValue(name, out var cls))
                {
                    cls = new ClassModel { Name = name };
                    var td = TypeDefRx.Match(t);
                    if (td.Success && int.TryParse(td.Groups[1].Value, out int tdIdx))
                        cls.TypeDefIndex = tdIdx;
                    dump.Classes[name] = cls;
                }
                current = cls;
                depth = 0;
                continue;
            }

            // ─── фігурні дужки
            if (t == "{" || t.StartsWith("{")) { depth++; continue; }
            if (t == "}" || t.StartsWith("}")) { depth--; if (depth <= 0) current = null; continue; }

            // ─── Address(RVA="...")
            var am = AddrRx.Match(t);
            if (am.Success) { pendingRva = am.Groups[1].Value; continue; }

            if (current == null) continue;

            // ─── поле — шукаємо ';' і коментар з offset
            int semi = t.IndexOf(';');
            if (semi < 0) continue;
            string decl = t.Substring(0, semi);

            string hex = null;
            var hm = HexRx.Match(t, semi);
            if (hm.Success && hm.Index > semi) hex = hm.Groups[1].Value;
            else if (pendingRva != null)
                hex = pendingRva.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? pendingRva.Substring(2)
                    : pendingRva;
            pendingRva = null;

            if (hex == null) continue;

            // ─── парсимо "public System.String _displayName"
            string body = decl.Trim();
            int lastSpace = body.LastIndexOf(' ');
            if (lastSpace < 0) continue;

            string fieldName = body.Substring(lastSpace + 1).TrimStart('*', '&');

            // витягуємо чистий тип (без модифікаторів)
            string fieldType = ExtractType(body, lastSpace);

            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int off))
                continue;

            bool isStatic = decl.Contains(" static ");

            current.Fields.Add(new FieldModel
            {
                Name = fieldName,
                Type = fieldType,
                Offset = off,
                IsStatic = isStatic
            });
        }

        return dump;
    }

    // ═══════════════════════════════════════════════════════════
    //   витягування типу з декларації
    // ═══════════════════════════════════════════════════════════

    // Вхід:   "public System.String _displayName"
    //         lastSpace вказує на позицію пробілу ПЕРЕД іменем поля
    // Вихід:  "System.String"
    static string ExtractType(string decl, int lastSpace)
    {
        if (lastSpace <= 0) return "";

        string beforeField = decl.Substring(0, lastSpace);
        var words = beforeField.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        // модифікатори які треба викинути
        var modifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal",
            "static", "readonly", "const", "sealed",
            "virtual", "override", "abstract", "extern",
            "unsafe", "volatile", "new", "partial"
        };

        var typeParts = new List<string>();
        bool pastModifiers = false;

        foreach (var w in words)
        {
            if (!pastModifiers && modifiers.Contains(w))
                continue;

            pastModifiers = true;
            typeParts.Add(w);
        }

        string type = string.Join(" ", typeParts).Trim();

        // обрізаємо до першого '<' якщо це generic
        // але тільки якщо весь тип — це щось на кшталт "List<T>"
        // для "System.Collections.Generic.List<T>" залишаємо як є
        return type;
    }

    // ═══════════════════════════════════════════════════════════
    //   runtime варіант (поки не використовується)
    // ═══════════════════════════════════════════════════════════

    public static Dump ParseRuntime(string gameId) => throw new NotImplementedException();
}