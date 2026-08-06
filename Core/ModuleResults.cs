namespace PSuite.Core
{
    public enum ModuleState
    {
        Applied,
        NotApplied,
        Partial,
        Unknown
    }

    public class ModuleDetectResult
    {
        public bool Success { get; set; }
        public ModuleState State { get; set; } = ModuleState.Unknown;
        public string? Details { get; set; }
        public string? Error { get; set; }
    }

    public class ModuleApplyResult
    {
        public bool Success { get; set; }
        public bool RequiresRestart { get; set; }
        public string? Details { get; set; }
        public string? Error { get; set; }
    }

    public class ModuleRollbackResult
    {
        public bool Success { get; set; }
        public string? Details { get; set; }
        public string? Error { get; set; }
    }
}