// language: C#, file: Services/LiveClassFinder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class LiveClassFinder
{
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll")]
    static extern int VirtualQueryEx(IntPtr h, IntPtr a, out MEMORY_BASIC_INFORMATION m, uint l);

    const uint MEM_COMMIT = 0x1000;
    const uint PAGE_GUARD = 0x100;
    const uint PAGE_NOACCESS = 0x01;

    // execute flags — пропускаємо (там код, не дані)
    const uint PAGE_EXECUTE = 0x10;
    const uint PAGE_EXECUTE_READ = 0x20;
    const uint PAGE_EXECUTE_READWRITE = 0x40;
    const uint PAGE_EXECUTE_WRITECOPY = 0x80;

    // ═══════════════════════════════════════════════════════════
    //   моделі
    // ═══════════════════════════════════════════════════════════

    public class Candidate
    {
        public int Offset;
        public IntPtr Value;
        public string Note = "ptr";
        public bool IsEntityList;
        public int ListSize;
    }

    public class ClassInfo
    {
        public string Name;
        public IntPtr ClassPtr;
        public long ClassRva;
        public IntPtr StaticFields;
        public List<Candidate> Candidates = new();
    }

    // ═══════════════════════════════════════════════════════════
    //   public API
    // ═══════════════════════════════════════════════════════════

    public static ClassInfo FindClass(string className, Action<string> log = null)
        => FindClassAsync(className, log, CancellationToken.None);

    public static ClassInfo FindClassAsync(
        string className,
        Action<string> log,
        CancellationToken cancel)
    {
        if (!MemoryAdapter.IsAttached) { log?.Invoke("✗ not attached"); return null; }

        log?.Invoke($"▸ шукаю \"{className}\" ...");

        // ═══ крок 1: шукаємо рядок "ClassName\0"
        byte[] strPat = Encoding.ASCII.GetBytes(className + "\0");

        // швидкий скан module (metadata майже завжди тут)
        log?.Invoke("  [1/2] module...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IntPtr nameAddr = FastScan(strPat, true, log, cancel);
        log?.Invoke($"  [{sw.ElapsedMilliseconds}ms] module done");

        // якщо в module нема — скан heap
        if (nameAddr == IntPtr.Zero)
        {
            log?.Invoke("  [2/2] heap...");
            sw.Restart();
            nameAddr = FastScan(strPat, false, log, cancel);
            log?.Invoke($"  [{sw.ElapsedMilliseconds}ms] heap done");
        }

        if (nameAddr == IntPtr.Zero)
        {
            log?.Invoke("✗ string NOT found");
            return null;
        }
        log?.Invoke($"  ✓ string @ 0x{nameAddr.ToInt64():X}");

        // ═══ крок 2: шукаємо pointer на цей рядок
        byte[] ptrPat = BitConverter.GetBytes(nameAddr.ToInt64());

        log?.Invoke("  шукаю ptr на string...");
        IntPtr holder = FastScan(ptrPat, true, log, cancel,
            nameAddr.ToInt64() - 0x100000,
            nameAddr.ToInt64() + 0x100000);

        if (holder == IntPtr.Zero)
        {
            log?.Invoke("✗ ptr NOT found");
            return null;
        }
        log?.Invoke($"  holder @ 0x{holder.ToInt64():X}");

        // ═══ крок 3: klass = holder - 0x10
        IntPtr klass = holder - 0x10;
        long klassRva = klass.ToInt64() - MemoryAdapter.ModuleBase.ToInt64();

        if (MemoryAdapter.ReadPtr(klass + 0x10) != nameAddr)
        {
            log?.Invoke("✗ validation failed");
            return null;
        }
        if (klassRva < 0 || klassRva > MemoryAdapter.ModuleSize)
        {
            log?.Invoke($"✗ klass outside module");
            return null;
        }
        log?.Invoke($"  ✓ class @ 0x{klass.ToInt64():X}  (RVA 0x{klassRva:X})");

        var info = new ClassInfo
        {
            Name = className,
            ClassPtr = klass,
            ClassRva = klassRva,
        };

        // ═══ крок 4: static_fields
        IntPtr sf = MemoryAdapter.ReadPtr(klass + 0xB8);
        info.StaticFields = sf;

        if (sf == IntPtr.Zero || !MemoryAdapter.IsReadable(sf))
        {
            log?.Invoke("✗ static_fields null");
            return info;
        }
        log?.Invoke($"  static_fields @ 0x{sf.ToInt64():X}");

        // ═══ крок 5: паралельне сканування slots
        log?.Invoke("  сканую slots...");
        int processed = 0;
        Parallel.For(0, 0x800 / 8, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = cancel
        }, idx =>
        {
            int off = idx * 8;
            IntPtr val = MemoryAdapter.ReadPtr(sf + off);
            if (val == IntPtr.Zero) return;
            if (!MemoryAdapter.IsReadable(val)) return;

            var c = new Candidate { Offset = off, Value = val, Note = "ptr" };

            // спроба 1: Il2CppClass
            try
            {
                IntPtr np = MemoryAdapter.ReadPtr(val + 0x10);
                if (np != IntPtr.Zero && MemoryAdapter.IsReadable(np))
                {
                    string cn = MemoryAdapter.ReadCString(np, 48);
                    if (!string.IsNullOrEmpty(cn) && cn.Length < 48 && IsPrintable(cn))
                        c.Note = "class: " + cn;
                }
            }
            catch { }

            // спроба 2: ListDictionary
            try
            {
                for (int vo = 0x08; vo <= 0x40; vo += 8)
                {
                    IntPtr list = MemoryAdapter.ReadPtr(val + vo);
                    if (list == IntPtr.Zero || !MemoryAdapter.IsReadable(list)) continue;

                    IntPtr items = MemoryAdapter.ReadPtr(list + 0x10);
                    int size = MemoryAdapter.ReadInt(list + 0x18);

                    if (items == IntPtr.Zero || !MemoryAdapter.IsReadable(items)) continue;
                    if (size < 5 || size > 200000) continue;

                    int valid = 0;
                    int total = Math.Min(size, 6);
                    for (int i = 0; i < total; i++)
                    {
                        IntPtr ent = MemoryAdapter.ReadPtr(items + 0x20 + i * 8);
                        if (ent == IntPtr.Zero) { valid++; continue; }
                        if (!MemoryAdapter.IsReadable(ent)) continue;
                        IntPtr ek = MemoryAdapter.ReadPtr(ent);
                        if (ek == IntPtr.Zero) continue;
                        string en = ReadKlassNameAt(ek);
                        if (!string.IsNullOrEmpty(en)) valid++;
                    }

                    if (valid >= Math.Max(2, total - 1))
                    {
                        c.IsEntityList = true;
                        c.ListSize = size;
                        c.Note = $"LIST size={size} (valid {valid}/{total}, vals@+0x{vo:X})";
                        break;
                    }
                }
            }
            catch { }

            lock (info.Candidates) info.Candidates.Add(c);
            Interlocked.Increment(ref processed);
        });

        info.Candidates.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        log?.Invoke($"  ✓ {info.Candidates.Count} candidates");
        return info;
    }

    // ═══════════════════════════════════════════════════════════
    //   verify candidate
    // ═══════════════════════════════════════════════════════════

    public static string VerifyCandidate(IntPtr staticFieldSlot, int limit = 8)
    {
        try
        {
            IntPtr val = MemoryAdapter.ReadPtr(staticFieldSlot);
            if (val == IntPtr.Zero) return "ptr = null";
            if (!MemoryAdapter.IsReadable(val)) return "ptr unreadable";

            IntPtr items = MemoryAdapter.ReadPtr(val + 0x10);
            int size = MemoryAdapter.ReadInt(val + 0x18);
            if (items != IntPtr.Zero && MemoryAdapter.IsReadable(items)
                && size > 0 && size < 200000)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"LIST size = {size}");
                sb.AppendLine($"items @ 0x{items.ToInt64():X}");
                sb.AppendLine();

                int shown = 0;
                for (int i = 0; i < size && shown < limit; i++)
                {
                    IntPtr ent = MemoryAdapter.ReadPtr(items + 0x20 + i * 8);
                    if (ent == IntPtr.Zero) continue;
                    if (!MemoryAdapter.IsReadable(ent)) continue;

                    IntPtr ek = MemoryAdapter.ReadPtr(ent);
                    string en = "?";
                    if (ek != IntPtr.Zero && MemoryAdapter.IsReadable(ek))
                        en = ReadKlassNameAt(ek) ?? "?";
                    sb.AppendLine($"  [{i,3}] 0x{ent.ToInt64():X}  class={en}");
                    shown++;
                }
                return sb.ToString();
            }

            string clsName = ReadKlassNameAt(val);
            if (!string.IsNullOrEmpty(clsName)) return $"class: {clsName}";

            return $"ptr 0x{val.ToInt64():X}";
        }
        catch (Exception ex) { return "ERR: " + ex.Message; }
    }

    // ═══════════════════════════════════════════════════════════
    //   helpers
    // ═══════════════════════════════════════════════════════════

    static string ReadKlassNameAt(IntPtr klass)
    {
        try
        {
            IntPtr np = MemoryAdapter.ReadPtr(klass + 0x10);
            if (np == IntPtr.Zero || !MemoryAdapter.IsReadable(np)) return null;
            string s = MemoryAdapter.ReadCString(np, 48);
            return string.IsNullOrEmpty(s) || s.Length > 48 || !IsPrintable(s) ? null : s;
        }
        catch { return null; }
    }

    static bool IsPrintable(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (c < 0x20 || c > 0x7E) return false;
        return true;
    }

    // ═══════════════════════════════════════════════════════════
    //   FAST SCANNER — Boyer-Moore-Horspool + parallel regions
    // ═══════════════════════════════════════════════════════════

    static IntPtr FastScan(
        byte[] pattern,
        bool moduleOnly,
        Action<string> log,
        CancellationToken cancel,
        long minAddr = 0,
        long maxAddr = 0)
    {
        if (pattern.Length == 0) return IntPtr.Zero;

        // ─── BMH bad-char table
        int[] badChar = new int[256];
        for (int i = 0; i < 256; i++) badChar[i] = pattern.Length;
        for (int i = 0; i < pattern.Length - 1; i++) badChar[pattern[i]] = pattern.Length - 1 - i;

        int skip0 = badChar[pattern[0]];
        int lastIdx = pattern.Length - 1;
        byte lastByte = pattern[lastIdx];

        // ─── збираємо регіони
        var regions = new List<(IntPtr addr, long size)>();
        IntPtr addr = IntPtr.Zero;
        int mbiSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

        while (VirtualQueryEx(MemoryAdapter.Handle, addr, out var mbi, (uint)mbiSize) != 0)
        {
            long size = mbi.RegionSize.ToInt64();
            long baseAddr = mbi.BaseAddress.ToInt64();

            bool readable = mbi.State == MEM_COMMIT
                         && (mbi.Protect & PAGE_GUARD) == 0
                         && (mbi.Protect & PAGE_NOACCESS) == 0;

            // skip executable regions (код, не дані)
            uint p = mbi.Protect;
            bool isExec = (p & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
            if (isExec) readable = false;

            if (moduleOnly)
            {
                long rel = baseAddr - MemoryAdapter.ModuleBase.ToInt64();
                if (rel < 0 || rel > MemoryAdapter.ModuleSize) readable = false;
            }

            if (minAddr != 0 && baseAddr + size < minAddr) readable = false;
            if (maxAddr != 0 && baseAddr > maxAddr) readable = false;

            if (readable && size > 0 && size < 0x40000000)
                regions.Add((mbi.BaseAddress, size));

            long next = baseAddr + size;
            if (next <= 0 || next >= 0x7FFFFFFFFFFF) break;
            addr = new IntPtr(next);
        }

        if (regions.Count == 0) return IntPtr.Zero;

        // ─── паралельно скануємо регіони
        IntPtr found = IntPtr.Zero;
        long totalMB = regions.Sum(r => r.size) / 1024 / 1024;
        log?.Invoke($"  регіонів: {regions.Count}, розмір: {totalMB} МБ");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long bytesDone = 0;
        long lastLog = 0;

        try
        {
            Parallel.ForEach(regions, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancel
            }, (region, state) =>
            {
                if (Volatile.Read(ref found) != IntPtr.Zero) { state.Stop(); return; }

                IntPtr hit = ScanRegion(region.addr, region.size, pattern, badChar, skip0, lastIdx, lastByte, cancel);
                if (hit != IntPtr.Zero)
                {
                    Interlocked.CompareExchange(ref found, hit, IntPtr.Zero);
                    state.Stop();
                }

                long done = Interlocked.Add(ref bytesDone, region.size);
                if (done - lastLog > 200 * 1024 * 1024)
                {
                    lastLog = done;
                    long mb = done / 1024 / 1024;
                    long sec = Math.Max(1, sw.ElapsedMilliseconds / 1000);
                    log?.Invoke($"  ... {mb} МБ ({mb / sec} МБ/с)");
                }
            });
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        log?.Invoke($"  скан: {sw.ElapsedMilliseconds}ms, {bytesDone / 1024 / 1024} МБ");
        return found;
    }

    static IntPtr ScanRegion(
        IntPtr baseAddr,
        long size,
        byte[] pattern,
        int[] badChar,
        int skip0,
        int lastIdx,
        byte lastByte,
        CancellationToken cancel)
    {
        const int ChunkSize = 0x400000; // 4 MB
        byte[] buf = new byte[ChunkSize + pattern.Length];

        long pos = 0;
        int plen = pattern.Length;

        while (pos < size)
        {
            if (cancel.IsCancellationRequested) return IntPtr.Zero;

            int toRead = (int)Math.Min(ChunkSize, size - pos);
            if (!MemoryAdapter.ReadProcessMemory(MemoryAdapter.Handle,
                baseAddr + (int)pos, buf, toRead, out _))
            {
                pos += toRead;
                continue;
            }

            // ─── Boyer-Moore-Horspool
            int i = 0;
            int end = toRead - plen;
            while (i <= end)
            {
                // швидка перевірка останнього байту
                if (buf[i + lastIdx] == lastByte)
                {
                    bool ok = true;
                    for (int j = lastIdx - 1; j >= 0; j--)
                    {
                        if (buf[i + j] != pattern[j]) { ok = false; break; }
                    }
                    if (ok) return baseAddr + (int)pos + i;
                }
                i += badChar[buf[i + lastIdx]];
            }

            pos += toRead - plen + 1;
        }
        return IntPtr.Zero;
    }
}