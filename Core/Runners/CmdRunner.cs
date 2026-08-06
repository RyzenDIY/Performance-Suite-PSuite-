using System;
using System.Threading.Tasks;

namespace PSuite.Core.Runners
{
    // Mechanically identical to BatRunner (both run through cmd.exe) but
    // kept as its own named Runner per the V5 spec's Runner list, so logs
    // and results correctly say "CMD Runner" for .cmd files instead of
    // conflating them with .bat.
    public class CmdRunner : IScriptRunner
    {
        public string Name => "CMD Runner";

        public bool CanRun(string scriptPath) =>
            string.Equals(System.IO.Path.GetExtension(scriptPath), ".cmd", StringComparison.OrdinalIgnoreCase);

        public Task<RunnerExecutionResult> RunAsync(string scriptPath, string? capturePath, TimeSpan timeout)
        {
            var quotedCapture = capturePath == null ? string.Empty : $" \"{capturePath}\"";
            var arguments = $"/d /c \"\"{scriptPath}\"{quotedCapture}\"";
            return ProcessRunnerHelper.RunAsync(Name, "cmd.exe", arguments, timeout);
        }
    }
}
