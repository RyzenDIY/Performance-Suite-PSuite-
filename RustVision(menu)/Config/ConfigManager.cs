using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustVision.Config;

/// <summary>
/// Plain data model describing the current state of every UI control in the app.
/// This has no connection to any external process or game - it is purely local
/// UI state that gets serialized to disk so the window can restore itself.
/// </summary>
public class RustVisionConfig
{
    // AIM tab (UI-only controls, no real assist logic)
    public bool AimEnabled { get; set; } = false;
    public bool AimShowFov { get; set; } = true;
    public bool AimShowTargetIndicator { get; set; } = true;
    public double AimFov { get; set; } = 45;
    public double AimSmoothness { get; set; } = 30;
    public string AimPriority { get; set; } = "Closest";
    public string AimHotkey { get; set; } = "F6";

    // VISUALS tab
    public bool ShowBox { get; set; } = true;
    public bool ShowName { get; set; } = true;
    public bool ShowHealth { get; set; } = true;
    public bool ShowDistance { get; set; } = true;
    public double VisualsOpacity { get; set; } = 85;
    public string LineStyle { get; set; } = "Solid";

    // MISC tab
    public bool NotificationsEnabled { get; set; } = true;
    public bool StartWithApplication { get; set; } = false;
    public bool UiSounds { get; set; } = true;
    public bool AnimationsEnabled { get; set; } = true;
    public double UiScaleMisc { get; set; } = 100;

    // SETTINGS tab
    public string Theme { get; set; } = "Red";
    public double UiScale { get; set; } = 100;
    public bool AlwaysOnTop { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public string Language { get; set; } = "English";

    // New: accent color, auto-save-friendly compact layout, and the
    // window-only visibility hotkey. None of these touch any other process.
    public string AccentColorHex { get; set; } = "#E51E24";
    public bool CompactMode { get; set; } = false;
    public string MenuHotkey { get; set; } = "Insert";

    public string ProfileName { get; set; } = "Default";
}

/// <summary>
/// Reads and writes RustVisionConfig objects to local JSON files under a
/// "configs" folder next to the executable. Never touches any other process.
/// </summary>
public static class ConfigManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ConfigDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs");

    private static string PathFor(string profileName) =>
        Path.Combine(ConfigDirectory, $"{Sanitize(profileName)}.json");

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "default" : name.ToLowerInvariant();
    }

    /// <summary>
    /// Ensures the configs folder exists and seeds it with default profiles
    /// the first time the app runs.
    /// </summary>
    public static void EnsureInitialized()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            var defaultPath = PathFor("default");
            if (!File.Exists(defaultPath))
            {
                Save("default", new RustVisionConfig { ProfileName = "Default" });
            }

            var legitPath = PathFor("legit");
            if (!File.Exists(legitPath))
            {
                Save("legit", new RustVisionConfig
                {
                    ProfileName = "Legit",
                    AimFov = 12,
                    AimSmoothness = 75,
                    ShowBox = false,
                    ShowName = true,
                    ShowHealth = false,
                    ShowDistance = true
                });
            }
        }
        catch
        {
            // Non-fatal: if we can't create the configs folder (e.g. read-only
            // install location) the app should still run using in-memory defaults.
        }
    }

    /// <summary>
    /// Saves a configuration object as JSON under the given profile name.
    /// Returns true on success, false if the write failed for any reason.
    /// </summary>
    public static bool Save(string profileName, RustVisionConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(PathFor(profileName), json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Loads a configuration object from disk. If the file is missing, empty,
    /// or contains invalid JSON, a fresh default configuration is returned
    /// instead of throwing.
    /// </summary>
    public static RustVisionConfig Load(string profileName)
    {
        try
        {
            var path = PathFor(profileName);
            if (!File.Exists(path))
            {
                return new RustVisionConfig { ProfileName = profileName };
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RustVisionConfig { ProfileName = profileName };
            }

            var result = JsonSerializer.Deserialize<RustVisionConfig>(json, SerializerOptions);
            return result ?? new RustVisionConfig { ProfileName = profileName };
        }
        catch
        {
            // Malformed JSON or IO error - fall back to defaults rather than crash.
            return new RustVisionConfig { ProfileName = profileName };
        }
    }
}
