// language: C#, file: Core/JsonStore.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class JsonStore
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static T Load<T>(string path, Func<T> fallback = null) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return fallback != null ? fallback() : null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return fallback != null ? fallback() : null;
            return JsonSerializer.Deserialize<T>(json, Opts);
        }
        catch
        {
            // backups
            try
            {
                string bak = path + ".bak";
                if (File.Exists(bak))
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(bak), Opts);
            }
            catch { }
            return fallback != null ? fallback() : null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // atomic: write tmp, backup old, rename
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opts));

            if (File.Exists(path))
                File.Copy(path, path + ".bak", true);

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            throw new IOException($"JsonStore.Save failed for {path}: {ex.Message}", ex);
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Opts);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Opts);
}