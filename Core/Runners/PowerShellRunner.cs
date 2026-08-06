using System;
using System.Threading.Tasks;

namespace PSuite.Core.Runners
{
    // Fallback runner — per V5 priority (BAT > CMD > REG > PowerShell),
    // only used when a module author ships a .ps1 because the operation
    // genuinely can't be done in batch.
    public class PowerShellRunner : IScriptRunner
    {
        public string Name => "PowerShell Runner";

        public bool CanRun(string scriptPath) =>
            string.Equals(System.IO.Path.GetExtension(scriptPath), ".ps1", StringComparison.OrdinalIgnoreCase);

        public Task<RunnerExecutionResult> RunAsync(string scriptPath, string? capturePath, TimeSpan timeout)
        {
            var quotedCapture = capturePath == null ? string.Empty : $" \"{capturePath}\"";
            var arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"{quotedCapture}";
            return ProcessRunnerHelper.RunAsync(Name, "powershell.exe", arguments, timeout);
        }
    }
}
