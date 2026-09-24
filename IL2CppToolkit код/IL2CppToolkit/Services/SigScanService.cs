// language: C#, file: Services/SigScanService.cs
using System;
using System.Collections.Generic;
using System.IO;

public class SigMatch
{
    public string Id;
    public string Pattern;
    public int InsnRva;
    public long TargetRva;
    public string Note;
}

public static class SigScanService
{
    static byte[] _dll;

    public static bool Load(string path)
    {
        try { _dll = File.ReadAllBytes(path); return _dll != null && _dll.Length > 0; }
        catch { return false; }
    }

    public static bool IsLoaded => _dll != null && _dll.Length > 0;

    // шукає всі входження патерну
    public static List<int> FindAll(string pattern, int limit = 50)
    {
        var results = new List<int>();
        if (_dll == null) return results;

        var (bytes, mask) = Parse(pattern);
        int end = _dll.Length - bytes.Length;

        for (int i = 0; i < end && results.Count < limit; i++)
        {
            bool ok = true;
            for (int j = 0; j < bytes.Length; j++)
                if (mask[j] && _dll[i + j] != bytes[j]) { ok = false; break; }
            if (ok) results.Add(i);
        }
        return results;
    }

    public static int FindFirst(string pattern)
    {
        var list = FindAll(pattern, 1);
        return list.Count > 0 ? list[0] : 0;
    }

    public static long ResolveRip(int insnRva, int operandOffset)
    {
        if (_dll == null) return 0;
        int disp = BitConverter.ToInt32(_dll, insnRva + operandOffset);
        long next = insnRva + operandOffset + 4;
        return next + disp;
    }

    static (byte[], bool[]) Parse(string p)
    {
        var parts = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        var mask = new bool[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "?" || parts[i] == "??") { bytes[i] = 0; mask[i] = false; }
            else { bytes[i] = Convert.ToByte(parts[i], 16); mask[i] = true; }
        }
        return (bytes, mask);
    }
}