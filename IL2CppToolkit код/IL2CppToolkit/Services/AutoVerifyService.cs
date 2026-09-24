// language: C#, file: Services/AutoVerifyService.cs
using System;
using System.Collections.Generic;
using System.IO;

public enum VerifyMode
{
    BaseNetworkable,
    MainCamera,
    LocalPlayer
}

public class VerifyResult
{
    public int InsnRva;
    public long TargetRva;
    public bool Valid;
    public string Reason;
    public int DetectedOffset;
    public string DetectedInfo;
}

public static class AutoVerifyService
{
    public static List<VerifyResult> Verify(
        List<int> matches,
        VerifyMode mode,
        byte[] dll,
        int ripOperandOff)
    {
        var results = new List<VerifyResult>();
        if (!MemoryAdapter.IsAttached) return results;
        if (dll == null || matches == null) return results;

        foreach (var insnRva in matches)
        {
            var r = new VerifyResult { InsnRva = insnRva };
            try
            {
                long targetRva = ResolveRip(dll, insnRva, ripOperandOff);
                r.TargetRva = targetRva;
                if (targetRva <= 0) { r.Reason = "rip fail"; results.Add(r); continue; }

                IntPtr slot = MemoryAdapter.ModuleBase + (int)targetRva;
                if (!MemoryAdapter.IsReadable(slot))
                { r.Reason = "slot unreadable"; results.Add(r); continue; }

                switch (mode)
                {
                    case VerifyMode.BaseNetworkable: VerifyBaseNetworkable(slot, r); break;
                    case VerifyMode.MainCamera: VerifyMainCamera(slot, r); break;
                    case VerifyMode.LocalPlayer: VerifyLocalPlayer(slot, r); break;
                }

                results.Add(r);
            }
            catch (Exception ex)
            {
                r.Reason = "EX: " + ex.Message;
                results.Add(r);
            }
        }
        return results;
    }

    // ═══════════════════════════════════════════════════════════
    //   BaseNetworkable — шукає Il2CppClass "BaseNetworkable"
    // ═══════════════════════════════════════════════════════════

    static void VerifyBaseNetworkable(IntPtr slot, VerifyResult r)
    {
        IntPtr val = MemoryAdapter.ReadPtr(slot);
        if (val == IntPtr.Zero) { r.Reason = "ptr null"; return; }
        if (!MemoryAdapter.IsReadable(val)) { r.Reason = "unreadable"; return; }

        // ─── ІНТЕРПРЕТАЦІЯ 1: val = Il2CppClass* BaseNetworkable
        string klassName = ReadKlassName(val);
        if (klassName == "BaseNetworkable")
        {
            if (TryExtractEntityList(val, out int sfOff, out int voOff, out int size, out string info))
            {
                r.Valid = true;
                r.DetectedOffset = sfOff;
                r.DetectedInfo = info;
                r.Reason = "OK (Il2CppClass mode)";
                return;
            }
            r.Reason = $"class OK ({klassName}) but no entities";
            return;
        }

        // ─── ІНТЕРПРЕТАЦІЯ 2: val = BaseNetworkable instance directly
        if (TryExtractEntityListFromInstance(val, out int ceOff, out int vo2, out int size2, out string info2))
        {
            r.Valid = true;
            r.DetectedOffset = ceOff;
            r.DetectedInfo = info2;
            r.Reason = "OK (instance mode)";
            return;
        }

        r.Reason = klassName != null ? $"wrong class: {klassName}" : "not an entity list";
    }

    // ═══════════════════════════════════════════════════════════
    //   MainCamera
    // ═══════════════════════════════════════════════════════════

    static void VerifyMainCamera(IntPtr slot, VerifyResult r)
    {
        IntPtr val = MemoryAdapter.ReadPtr(slot);
        if (val == IntPtr.Zero) { r.Reason = "ptr null"; return; }
        if (!MemoryAdapter.IsReadable(val)) { r.Reason = "unreadable"; return; }

        // ─── ІНТЕРПРЕТАЦІЯ 1: val = Il2CppClass* Camera/MainCamera
        string name = ReadKlassName(val);
        if (name == "MainCamera" || name == "Camera")
        {
            // читаємо static_fields → main
            IntPtr sf = MemoryAdapter.ReadPtr(val + 0xB8);
            if (MemoryAdapter.IsReadable(sf))
            {
                for (int off = 0; off < 0x200; off += 8)
                {
                    IntPtr cam = MemoryAdapter.ReadPtr(sf + off);
                    if (cam == IntPtr.Zero || !MemoryAdapter.IsReadable(cam)) continue;

                    if (TryFindViewMatrix(cam, out int mo, out string info))
                    {
                        r.Valid = true;
                        r.DetectedOffset = off;
                        r.DetectedInfo = $"class {name}, sf=0x{off:X}, {info}";
                        r.Reason = "OK (Il2CppClass mode)";
                        return;
                    }
                }
            }
            r.Reason = $"class {name} OK but no view matrix";
            return;
        }

        // ─── ІНТЕРПРЕТАЦІЯ 2: val = Camera instance
        if (TryFindViewMatrix(val, out int mo2, out string info2))
        {
            r.Valid = true;
            r.DetectedOffset = 0;
            r.DetectedInfo = $"instance mode, {info2}";
            r.Reason = "OK (instance mode)";
            return;
        }

        r.Reason = name != null ? $"wrong class: {name}" : "no view matrix";
    }

    // ═══════════════════════════════════════════════════════════
    //   LocalPlayer
    // ═══════════════════════════════════════════════════════════

    static void VerifyLocalPlayer(IntPtr slot, VerifyResult r)
    {
        IntPtr val = MemoryAdapter.ReadPtr(slot);
        if (val == IntPtr.Zero) { r.Reason = "ptr null"; return; }
        if (!MemoryAdapter.IsReadable(val)) { r.Reason = "unreadable"; return; }

        // ─── ІНТЕРПРЕТАЦІЯ 1: val = Il2CppClass* BasePlayer
        string name = ReadKlassName(val);
        if (name == "BasePlayer" || name == "LocalPlayer")
        {
            IntPtr sf = MemoryAdapter.ReadPtr(val + 0xB8);
            if (MemoryAdapter.IsReadable(sf))
            {
                for (int off = 0; off < 0x400; off += 8)
                {
                    IntPtr player = MemoryAdapter.ReadPtr(sf + off);
                    if (player == IntPtr.Zero || !MemoryAdapter.IsReadable(player)) continue;

                    if (TryFindPlayerName(player, out string pname, out int nameOff, out int hpOff))
                    {
                        r.Valid = true;
                        r.DetectedOffset = off;
                        r.DetectedInfo = $"class {name}, sf=0x{off:X}, player name=\"{pname}\" nameOff=0x{nameOff:X} hpOff=0x{hpOff:X}";
                        r.Reason = "OK (Il2CppClass mode)";
                        return;
                    }
                }
            }
            r.Reason = $"class {name} OK but no player instance";
            return;
        }

        // ─── ІНТЕРПРЕТАЦІЯ 2: val = BasePlayer instance
        if (TryFindPlayerName(val, out string pname2, out int nameOff2, out int hpOff2))
        {
            r.Valid = true;
            r.DetectedOffset = 0;
            r.DetectedInfo = $"instance mode, name=\"{pname2}\" nameOff=0x{nameOff2:X} hpOff=0x{hpOff2:X}";
            r.Reason = "OK (instance mode)";
            return;
        }

        r.Reason = name != null ? $"wrong class: {name}" : "no player name";
    }

    // ═══════════════════════════════════════════════════════════
    //   helpers
    // ═══════════════════════════════════════════════════════════

    // читає ім'я Il2CppClass (BaseNetworkable, BasePlayer, ...)
    // якщо ptr не є Il2CppClass — повертає null
    static string ReadKlassName(IntPtr maybeClass)
    {
        try
        {
            if (!MemoryAdapter.IsReadable(maybeClass + 0x10)) return null;
            IntPtr namePtr = MemoryAdapter.ReadPtr(maybeClass + 0x10);
            if (namePtr == IntPtr.Zero) return null;
            if (!MemoryAdapter.IsReadable(namePtr)) return null;
            string name = MemoryAdapter.ReadCString(namePtr, 64);
            if (string.IsNullOrEmpty(name)) return null;
            if (name.Length > 60) return null;
            // ім'я класу має бути ASCII
            foreach (char c in name)
                if (c < 0x20 || c > 0x7E) return null;
            return name;
        }
        catch { return null; }
    }

    // val = Il2CppClass* → static_fields → шукає ListDictionary з entities
    static bool TryExtractEntityList(
        IntPtr klass, out int sfOff, out int valsOff, out int size, out string info)
    {
        sfOff = valsOff = size = 0;
        info = null;

        IntPtr sf = MemoryAdapter.ReadPtr(klass + 0xB8);
        if (sf == IntPtr.Zero) return false;
        if (!MemoryAdapter.IsReadable(sf)) return false;

        for (int off = 0; off < 0x400; off += 8)
        {
            IntPtr ce = MemoryAdapter.ReadPtr(sf + off);
            if (ce == IntPtr.Zero || !MemoryAdapter.IsReadable(ce)) continue;

            if (TryParseListDictionary(ce, out int vo, out int cnt, out string note))
            {
                sfOff = off; valsOff = vo; size = cnt;
                info = $"class=BaseNetworkable, sf_off=0x{off:X}, vals_off=0x{vo:X}, size={cnt} {note}";
                return true;
            }
        }
        return false;
    }

    // val = BaseNetworkable instance → шукає clientEntities напряму
    static bool TryExtractEntityListFromInstance(
        IntPtr bn, out int ceOff, out int valsOff, out int size, out string info)
    {
        ceOff = valsOff = size = 0;
        info = null;

        for (int ce = 0x30; ce <= 0xC0; ce += 8)
        {
            IntPtr cePtr = MemoryAdapter.ReadPtr(bn + ce);
            if (cePtr == IntPtr.Zero || !MemoryAdapter.IsReadable(cePtr)) continue;

            if (TryParseListDictionary(cePtr, out int vo, out int cnt, out string note))
            {
                ceOff = ce; valsOff = vo; size = cnt;
                info = $"instance, ce_off=0x{ce:X}, vals_off=0x{vo:X}, size={cnt} {note}";
                return true;
            }
        }
        return false;
    }

    // перевіряє чи ptr — це ListDictionary з валідним vals (List<T>)
    static bool TryParseListDictionary(IntPtr cePtr, out int valsOff, out int size, out string note)
    {
        valsOff = size = 0;
        note = null;

        for (int vo = 0x08; vo <= 0x40; vo += 8)
        {
            IntPtr list = MemoryAdapter.ReadPtr(cePtr + vo);
            if (list == IntPtr.Zero || !MemoryAdapter.IsReadable(list)) continue;

            IntPtr items = MemoryAdapter.ReadPtr(list + 0x10);
            int cnt = MemoryAdapter.ReadInt(list + 0x18);

            if (items == IntPtr.Zero || cnt < 1 || cnt > 500000) continue;
            if (!MemoryAdapter.IsReadable(items)) continue;

            // перевіряємо елементи на валідність
            int valid = 0, checked_ = 0;
            for (int i = 0; i < Math.Min(cnt, 6); i++)
            {
                checked_++;
                IntPtr ent = MemoryAdapter.ReadPtr(items + 0x20 + i * 8);
                if (ent == IntPtr.Zero) { valid++; continue; }
                if (!MemoryAdapter.IsReadable(ent)) continue;

                IntPtr ek = MemoryAdapter.ReadPtr(ent);
                if (ek == IntPtr.Zero) continue;
                string en = ReadKlassName(ek);
                if (!string.IsNullOrEmpty(en)) valid++;
            }

            if (valid >= Math.Max(2, checked_ - 1))
            {
                valsOff = vo;
                size = cnt;
                note = $"(valid {valid}/{checked_})";
                return true;
            }
        }
        return false;
    }

    // шукає 4x4 view matrix (bottom row = 0,0,0,1)
    static bool TryFindViewMatrix(IntPtr cam, out int matrixOff, out string info)
    {
        matrixOff = 0;
        info = null;

        for (int mo = 0x100; mo < 0x800; mo += 4)
        {
            float m30 = MemoryAdapter.ReadFloat(cam + mo + 0x30);
            float m31 = MemoryAdapter.ReadFloat(cam + mo + 0x34);
            float m32 = MemoryAdapter.ReadFloat(cam + mo + 0x38);
            float m33 = MemoryAdapter.ReadFloat(cam + mo + 0x3C);

            if (MathF.Abs(m30) > 0.01f) continue;
            if (MathF.Abs(m31) > 0.01f) continue;
            if (MathF.Abs(m32) > 0.01f) continue;
            if (MathF.Abs(m33 - 1f) > 0.01f) continue;

            // перевіряємо що верхні рядки — одиничні
            float r0 = 0, r1 = 0, r2 = 0;
            for (int i = 0; i < 3; i++)
                r0 += MemoryAdapter.ReadFloat(cam + mo + i * 4) * MemoryAdapter.ReadFloat(cam + mo + i * 4);
            for (int i = 0; i < 3; i++)
                r1 += MemoryAdapter.ReadFloat(cam + mo + 0x10 + i * 4) * MemoryAdapter.ReadFloat(cam + mo + 0x10 + i * 4);
            for (int i = 0; i < 3; i++)
                r2 += MemoryAdapter.ReadFloat(cam + mo + 0x20 + i * 4) * MemoryAdapter.ReadFloat(cam + mo + 0x20 + i * 4);

            if (MathF.Abs(r0 - 1f) > 0.15f) continue;
            if (MathF.Abs(r1 - 1f) > 0.15f) continue;
            if (MathF.Abs(r2 - 1f) > 0.15f) continue;

            matrixOff = mo;
            info = $"viewMatrix @ +0x{mo:X}";
            return true;
        }
        return false;
    }

    // шукає string-ім'я гравця + float health
    static bool TryFindPlayerName(IntPtr player, out string name, out int nameOff, out int hpOff)
    {
        name = null;
        nameOff = hpOff = 0;

        for (int no = 0x100; no < 0x1000; no += 8)
        {
            IntPtr str = MemoryAdapter.ReadPtr(player + no);
            if (str == IntPtr.Zero || !MemoryAdapter.IsReadable(str)) continue;

            int len = MemoryAdapter.ReadInt(str + 0x10);
            if (len < 1 || len > 32) continue;

            string s = MemoryAdapter.ReadManagedString(str, 32);
            if (string.IsNullOrEmpty(s)) continue;

            bool printable = true;
            foreach (char c in s)
                if (c < 0x20 || c > 0x2000) { printable = false; break; }
            if (!printable) continue;

            // шукаємо health (float 1..500) далеко від name
            for (int ho = 0x100; ho < 0x1000; ho += 4)
            {
                if (Math.Abs(ho - no) < 0x40) continue;
                float hp = MemoryAdapter.ReadFloat(player + ho);
                if (hp > 1f && hp < 500f)
                {
                    name = s; nameOff = no; hpOff = ho;
                    return true;
                }
            }

            // хоча б ім'я знайшли
            name = s; nameOff = no; hpOff = 0;
            return true;
        }
        return false;
    }

    static long ResolveRip(byte[] dll, int insnRva, int operandOff)
    {
        if (insnRva + operandOff + 4 > dll.Length) return 0;
        int disp = BitConverter.ToInt32(dll, insnRva + operandOff);
        long next = insnRva + operandOff + 4;
        return next + disp;
    }
}