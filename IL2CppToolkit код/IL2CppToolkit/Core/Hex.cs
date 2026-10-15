// language: C#, file: Core/Hex.cs
using System;
using System.Globalization;
using System.Text;

public static class Hex
{
    public static string Fmt(long v, int pad = 0)
        => pad > 0 ? "0x" + v.ToString("X" + pad) : "0x" + v.ToString("X");

    public static string Fmt(IntPtr p, int pad = 0)
        => Fmt(p.ToInt64(), pad);

    public static bool TryParse(string s, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseInt(string s, out int value)
    {
        value = 0;
        if (!TryParse(s, out long l)) return false;
        value = (int)l;
        return true;
    }

    public static string BytesToPattern(byte[] data, bool[] wild = null, int max = 64)
    {
        if (data == null || data.Length == 0) return "";
        int n = Math.Min(data.Length, max);
        var sb = new StringBuilder(n * 3);
        for (int i = 0; i < n; i++)
        {
            if (wild != null && i < wild.Length && wild[i]) sb.Append("? ");
            else sb.Append(data[i].ToString("X2")).Append(' ');
        }
        return sb.ToString().Trim();
    }

    public static string BytesToAscii(byte[] data, int off, int len)
    {
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            byte b = data[off + i];
            sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
        }
        return sb.ToString();
    }
}