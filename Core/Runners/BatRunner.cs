using System;
using System.Threading.Tasks;

namespace PSuite.Core.Runners
{
    // BAT is the default/primary format — new modules should be written
    // in .bat unless something genuinely requires PowerShell.
    public class BatRunner : IScriptRunner
    {
        public string Name => "BAT Runner";

        public bool CanRun(string scriptPath) =>
            string.Equals(System.IO.Path.GetExtension(scriptPath), ".bat", StringComparison.OrdinalIgnoreCase);

        public Task<RunnerExecutionResult> RunAsync(string scriptPath, string? capturePath, TimeSpan timeout)
        {
            var quotedCapture = capturePath == null ? string.Empty : $" \"{capturePath}\"";
            var arguments = $"/d /c \"\"{scriptPath}\"{quotedCapture}\"";
            return ProcessRunnerHelper.RunAsync(Name, "cmd.exe", arguments, timeout);
        }
    }
}
