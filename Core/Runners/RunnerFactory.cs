using System.Collections.Generic;

namespace PSuite.Core.Runners
{
    // "Ядро не запускає BAT напряму. Ядро працює лише через Runner."
    // This is the one place that owns the list of available runners, in
    // priority order (BAT > CMD > REG > PowerShell per the V5 spec).
    // ScriptEngine never picks a runner by extension itself — it asks
    // here. Future Python/Lua/Rust/Native runners are added by appending
    // one line to this list; nothing else in Core changes.
    public static class RunnerFactory
    {
        private static readonly List<IScriptRunner> Runners = new()
        {
            new BatRunner(),
            new CmdRunner(),
            new RegistryRunner(),
            new PowerShellRunner()
        };

        public static IScriptRunner? Resolve(string scriptPath)
        {
            foreach (var runner in Runners)
                if (runner.CanRun(scriptPath))
                    return runner;
            return null;
        }
    }
}
