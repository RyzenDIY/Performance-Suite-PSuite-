using System;
using System.Threading.Tasks;

namespace PSuite.Core.Runners
{
    // Script Engine V5 contract. Core (ScriptEngine) never launches a
    // process directly — it asks RunnerFactory for whichever IScriptRunner
    // CanRun() a given file, then calls RunAsync() on it. Adding a new
    // script format (Python, Lua, native binaries, ...) means writing one
    // new class that implements this interface — nothing else in Core
    // changes.
    public interface IScriptRunner
    {
        // Shown in logs/results (RunnerExecutionResult.RunnerName) and in
        // the Script Engine V5 log line: Date/Module/Runner/Result/...
        string Name { get; }

        // Pure predicate, no side effects — RunnerFactory uses this to
        // pick a runner, it never guesses by extension itself.
        bool CanRun(string scriptPath);

        // capturePath is passed positionally to the script (%1 / $args[0])
        // when not null — used for apply/rollback. detect/validate/status
        // calls pass capturePath: null.
        Task<RunnerExecutionResult> RunAsync(string scriptPath, string? capturePath, TimeSpan timeout);
    }

    // Script Engine V5's "Execution Result": every run returns exactly
    // this, regardless of which runner handled it.
    public class RunnerExecutionResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string ConsoleOutput { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string RunnerName { get; set; } = string.Empty;
    }
}
