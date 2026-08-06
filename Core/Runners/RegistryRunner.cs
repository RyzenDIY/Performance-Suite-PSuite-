using System;
using System.Threading.Tasks;

namespace PSuite.Core.Runners
{
    // New in V5: a module's apply/rollback can be a plain .reg file
    // instead of a wrapper .bat that calls "reg import". `reg.exe` itself
    // doesn't speak our {"success":true,...} JSON contract — it just
    // exits 0/non-zero — so this runner synthesizes that one JSON line
    // from the exit code, meaning ScriptEngine's result-parsing stays
    // identical for every runner and never needs a REG-specific branch.
    //
    // Per the V5 spec: if you use .reg for apply, rollback must still
    // work via CapturePath (the module author's own rollback.reg or
    // rollback.bat is responsible for that — this runner only handles
    // "import this one .reg file", nothing more).
    public class RegistryRunner : IScriptRunner
    {
        public string Name => "Registry Runner";

        public bool CanRun(string scriptPath) =>
            string.Equals(System.IO.Path.GetExtension(scriptPath), ".reg", StringComparison.OrdinalIgnoreCase);

        public async Task<RunnerExecutionResult> RunAsync(string scriptPath, string? capturePath, TimeSpan timeout)
        {
            // capturePath is intentionally unused here: reg import has no
            // concept of "save old values first". A module that wants
            // CapturePath-based rollback with a .reg apply step needs its
            // own apply.bat that reads the old values before calling
            // "reg import" — this runner only performs the import itself.
            var arguments = $"import \"{scriptPath}\"";
            var result = await ProcessRunnerHelper.RunAsync(Name, "reg.exe", arguments, timeout);

            result.ConsoleOutput = result.Success
                ? "{\"success\":true,\"details\":\"Registry import applied.\"}"
                : $"{{\"success\":false,\"error\":{System.Text.Json.JsonSerializer.Serialize(result.ErrorMessage ?? "reg import failed.")}}}";

            return result;
        }
    }
}
