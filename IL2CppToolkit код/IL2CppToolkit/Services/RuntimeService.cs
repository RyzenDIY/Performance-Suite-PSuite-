// // language: C#, file: Services/RuntimeService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class RuntimeService
{
    public static bool IsAttached => MemoryAdapter.IsAttached;
    public static IntPtr ModuleBase => MemoryAdapter.ModuleBase;
    public static int ModuleSize => MemoryAdapter.ModuleSize;
    public static int Pid => MemoryAdapter.Pid;

    public static bool AttachRust() => MemoryAdapter.Attach("RustClient", "GameAssembly.dll");
    public static void Detach() => MemoryAdapter.Detach();

    // ═══════════════════════════════════════════════════════════
    //   читання значення поля за FieldModel
    // ═══════════════════════════════════════════════════════════

    public static string ReadFieldValue(IntPtr obj, FieldModel f)
    {
        try
        {
            if (!MemoryAdapter.IsAttached) return "—";
            IntPtr at = obj + f.Offset;
            if (!MemoryAdapter.IsReadable(at)) return "unreadable";

            string t = f.Type ?? "";

            if (t.Contains("Int32")) return MemoryAdapter.ReadInt(at).ToString();
            if (t.Contains("UInt32")) return MemoryAdapter.ReadUInt(at).ToString();
            if (t.Contains("Int16")) return ((short)MemoryAdapter.ReadInt(at)).ToString();
            if (t.Contains("UInt16")) return ((ushort)MemoryAdapter.ReadInt(at)).ToString();
            if (t.Contains("Int64")) return MemoryAdapter.ReadLong(at).ToString();
            if (t.Contains("UInt64")) return MemoryAdapter.ReadULong(at).ToString();
            if (t.Contains("Single") || t.EndsWith(".float") || t == "float") return MemoryAdapter.ReadFloat(at).ToString("F3");
            if (t.Contains("Double")) return MemoryAdapter.ReadStruct<double>(at).ToString("F3");
            if (t.Contains("Boolean") || t == "bool") return MemoryAdapter.ReadInt(at) != 0 ? "true" : "false";
            if (t.Contains("Byte"))
            {
                byte[] b = MemoryAdapter.Read(at, 1);
                return (b != null && b.Length > 0) ? b[0].ToString() : "?";
            }
            if (t.Contains("Char")) return ((char)MemoryAdapter.ReadInt(at)).ToString();

            if (t.Contains("String"))
            {
                IntPtr str = MemoryAdapter.ReadPtr(at);
                if (str == IntPtr.Zero) return "null";
                string s = MemoryAdapter.ReadManagedString(str, 64);
                return string.IsNullOrEmpty(s) ? "empty" : "\"" + s + "\"";
            }

            if (t.Contains("Vector3"))
            {
                float x = MemoryAdapter.ReadFloat(at);
                float y = MemoryAdapter.ReadFloat(at + 4);
                float z = MemoryAdapter.ReadFloat(at + 8);
                return $"({x:F1}, {y:F1}, {z:F1})";
            }
            if (t.Contains("Vector2"))
            {
                float x = MemoryAdapter.ReadFloat(at);
                float y = MemoryAdapter.ReadFloat(at + 4);
                return $"({x:F1}, {y:F1})";
            }
            if (t.Contains("Vector4") || t.Contains("Quaternion") || t.Contains("Color"))
            {
                float x = MemoryAdapter.ReadFloat(at);
                float y = MemoryAdapter.ReadFloat(at + 4);
                float z = MemoryAdapter.ReadFloat(at + 8);
                float w = MemoryAdapter.ReadFloat(at + 12);
                return $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";
            }

            // fallback — pointer
            IntPtr p = MemoryAdapter.ReadPtr(at);
            if (p != IntPtr.Zero && MemoryAdapter.IsReadable(p))
                return $"ptr 0x{p.ToInt64():X}";
            return $"0x{p.ToInt64():X}";
        }
        catch { return "?"; }
    }

    // ═══════════════════════════════════════════════════════════
    //   hex дамп
    // ═══════════════════════════════════════════════════════════

    public static string HexDump(IntPtr addr, int rows = 16)
    {
        var sb = new StringBuilder();
        for (int row = 0; row < rows; row++)
        {
            IntPtr at = addr + row * 16;
            var hex = new StringBuilder();
            var ascii = new StringBuilder();

            for (int i = 0; i < 16; i++)
            {
                byte[] b = MemoryAdapter.Read(at + i, 1);
                if (b == null || b.Length == 0) { hex.Append("?? "); ascii.Append('.'); continue; }
                hex.Append(b[0].ToString("X2")).Append(' ');
                ascii.Append(b[0] >= 0x20 && b[0] < 0x7F ? (char)b[0] : '.');
            }
            sb.AppendLine($"{at.ToInt64():X12}   {hex}   {ascii}");
        }
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════
    //   chain walker
    // ═══════════════════════════════════════════════════════════

    // йде по ланцюгу: base(+rva) + offsets[0] → ptr → + offsets[1] → ptr → ...
    public static IntPtr WalkChain(long baseRva, IEnumerable<int> offsets, out List<string> steps)
    {
        steps = new List<string>();
        if (!MemoryAdapter.IsAttached) return IntPtr.Zero;

        IntPtr cur = baseRva == 0
            ? MemoryAdapter.ModuleBase
            : MemoryAdapter.ModuleBase + (int)baseRva;

        steps.Add($"base = 0x{cur.ToInt64():X}");

        var list = offsets.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!MemoryAdapter.IsReadable(cur))
            {
                steps.Add($"+ 0x{list[i]:X}  →  unreadable");
                return IntPtr.Zero;
            }

            bool last = (i == list.Count - 1);
            if (last)
            {
                IntPtr at = cur + list[i];
                steps.Add($"+ 0x{list[i]:X}  →  0x{at.ToInt64():X} (final address)");
                return at;
            }

            IntPtr next = MemoryAdapter.ReadPtr(cur + list[i]);
            steps.Add($"+ 0x{list[i]:X}  →  ptr 0x{next.ToInt64():X}");
            if (next == IntPtr.Zero) return IntPtr.Zero;
            cur = next;
        }
        return cur;
    }

    // ═══════════════════════════════════════════════════════════
    //   читання за типом
    // ═══════════════════════════════════════════════════════════

    public static string ReadAs(IntPtr addr, string type)
    {
        try
        {
            if (!MemoryAdapter.IsReadable(addr)) return "unreadable";
            switch ((type ?? "").ToLowerInvariant())
            {
                case "int32": return MemoryAdapter.ReadInt(addr).ToString();
                case "uint32": return MemoryAdapter.ReadUInt(addr).ToString();
                case "int64": return MemoryAdapter.ReadLong(addr).ToString();
                case "uint64": return MemoryAdapter.ReadULong(addr).ToString();
                case "single":
                case "float": return MemoryAdapter.ReadFloat(addr).ToString("F4");
                case "double": return MemoryAdapter.ReadStruct<double>(addr).ToString("F4");
                case "bool": return MemoryAdapter.ReadInt(addr) != 0 ? "true" : "false";
                case "string":
                    IntPtr sp = MemoryAdapter.ReadPtr(addr);
                    if (sp == IntPtr.Zero) return "null";
                    string s = MemoryAdapter.ReadManagedString(sp, 128);
                    return string.IsNullOrEmpty(s) ? "empty" : "\"" + s + "\"";
                case "vector3":
                    float x = MemoryAdapter.ReadFloat(addr);
                    float y = MemoryAdapter.ReadFloat(addr + 4);
                    float z = MemoryAdapter.ReadFloat(addr + 8);
                    return $"({x:F1}, {y:F1}, {z:F1})";
                case "pointer":
                    IntPtr p = MemoryAdapter.ReadPtr(addr);
                    return $"0x{p.ToInt64():X}";
                default:
                    return $"0x{MemoryAdapter.ReadPtr(addr).ToInt64():X}";
            }
        }
        catch (Exception ex) { return "err: " + ex.Message; }
    }

    // ═══════════════════════════════════════════════════════════
    //   entity list: BaseNetworkable → clientEntities → List
    // ═══════════════════════════════════════════════════════════

    public class EntityInfo
    {
        public IntPtr Address;
        public string ClassName;
        public string Name;
        public float Health;
        public ulong Flags;
        public float X, Y, Z;
        public bool IsLocal;
    }

    public static List<EntityInfo> ReadEntities(
        long rvaBaseNetworkable, int offClientEntities,
        int offListDictVals, int offListItems, int offListSize, int offArrayData,
        int offName, int offHealth, int offFlags, int offTransform, int offPos,
        IntPtr localPlayer, Action<string> log = null)
    {
        var result = new List<EntityInfo>();
        if (!MemoryAdapter.IsAttached) { log?.Invoke("not attached"); return result; }

        try
        {
            IntPtr bn = rvaBaseNetworkable == 0
                ? MemoryAdapter.ModuleBase
                : MemoryAdapter.ModuleBase + (int)rvaBaseNetworkable;

            bn = MemoryAdapter.ReadPtr(bn);
            if (bn == IntPtr.Zero) { log?.Invoke("BaseNetworkable ptr null"); return result; }

            IntPtr ce = MemoryAdapter.ReadPtr(bn + offClientEntities);
            if (ce == IntPtr.Zero) { log?.Invoke("clientEntities null"); return result; }

            IntPtr vals = MemoryAdapter.ReadPtr(ce + offListDictVals);
            if (vals == IntPtr.Zero) { log?.Invoke("vals null"); return result; }

            IntPtr items = MemoryAdapter.ReadPtr(vals + offListItems);
            int size = MemoryAdapter.ReadInt(vals + offListSize);
            log?.Invoke($"list size = {size}, items @ 0x{items.ToInt64():X}");

            if (items == IntPtr.Zero || size < 1 || size > 200000) return result;

            for (int i = 0; i < size && i < 5000; i++)
            {
                IntPtr ent = MemoryAdapter.ReadPtr(items + offArrayData + i * 8);
                if (ent == IntPtr.Zero) continue;

                IntPtr klass = MemoryAdapter.ReadPtr(ent);
                if (klass == IntPtr.Zero) continue;
                IntPtr np = MemoryAdapter.ReadPtr(klass + 0x10);
                string cn = MemoryAdapter.ReadCString(np, 64);
                if (cn != "BasePlayer") continue;

                var e = new EntityInfo { Address = ent, ClassName = cn };

                // name
                IntPtr ns = MemoryAdapter.ReadPtr(ent + offName);
                if (ns != IntPtr.Zero) e.Name = MemoryAdapter.ReadManagedString(ns, 64);

                e.Health = MemoryAdapter.ReadFloat(ent + offHealth);
                e.Flags = MemoryAdapter.ReadULong(ent + offFlags);

                // transform → position
                IntPtr tf = MemoryAdapter.ReadPtr(ent + offTransform);
                if (tf != IntPtr.Zero)
                {
                    e.X = MemoryAdapter.ReadFloat(tf + offPos);
                    e.Y = MemoryAdapter.ReadFloat(tf + offPos + 4);
                    e.Z = MemoryAdapter.ReadFloat(tf + offPos + 8);
                }

                e.IsLocal = (ent == localPlayer);
                if (string.IsNullOrEmpty(e.Name)) e.Name = "(empty)";
                result.Add(e);
            }
            log?.Invoke($"found {result.Count} BasePlayers");
        }
        catch (Exception ex) { log?.Invoke("EX: " + ex.Message); }

        return result;
    }
}