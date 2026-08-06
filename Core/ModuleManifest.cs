using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PSuite.Core
{
    public enum ModuleEngine
    {
        Script,
        Native,
        Hybrid
    }

    public class ModuleEntryPoints
    {
        [JsonPropertyName("detect")]
        public string? Detect { get; set; }

        [JsonPropertyName("apply")]
        public string? Apply { get; set; }

        [JsonPropertyName("rollback")]
        public string? Rollback { get; set; }

        [JsonPropertyName("verify")]
        public string? Verify { get; set; }
    }

    // Only relevant when Engine == "Native": points at a module-supplied
    // .dll (relative to the module folder) and the fully-qualified type
    // inside it that implements IPSuiteModule.
    public class ModuleNativeInfo
    {
        [JsonPropertyName("assembly")]
        public string? Assembly { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    public class ModuleWindowsRange
    {
        [JsonPropertyName("min")]
        public string? Min { get; set; }

        [JsonPropertyName("max")]
        public string? Max { get; set; }
    }

    public class ModuleCompatibility
    {
        [JsonPropertyName("supportedWindows")]
        public List<ModuleWindowsRange> SupportedWindows { get; set; } = new();

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    // Mirrors MODULE_SPEC.md section 2 exactly. Field-for-field with manifest.json.
    public class ModuleManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "Other";

        // Kept as string in the manifest; parsed into Models.TweakRisk by ModuleManager.
        [JsonPropertyName("risk")]
        public string Risk { get; set; } = "Experimental";

        [JsonPropertyName("requiresAdmin")]
        public bool RequiresAdmin { get; set; }

        [JsonPropertyName("supportsRollback")]
        public bool SupportsRollback { get; set; } = true;

        [JsonPropertyName("requiresRestart")]
        public bool RequiresRestart { get; set; }

        [JsonPropertyName("engine")]
        public string Engine { get; set; } = "Script";

        [JsonPropertyName("entry")]
        public ModuleEntryPoints Entry { get; set; } = new();

        // Only present/required when Engine == "Native".
        [JsonPropertyName("native")]
        public ModuleNativeInfo? Native { get; set; }

        [JsonPropertyName("compatibility")]
        public ModuleCompatibility Compatibility { get; set; } = new();

        [JsonPropertyName("knownSideEffects")]
        public List<string> KnownSideEffects { get; set; } = new();

        [JsonPropertyName("expectedEffect")]
        public string? ExpectedEffect { get; set; }

        [JsonPropertyName("benchmarkGuidance")]
        public string? BenchmarkGuidance { get; set; }
    }
}