// language: C#, file: Adapters/ProcessFinder.cs
using System;
using System.Diagnostics;

public static class ProcessFinder
{
    public static int FindPid(string processName)
    {
        try
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0) return 0;
            return procs[0].Id;
        }
        catch { return 0; }
    }

    public static bool IsRunning(string processName)
        => FindPid(processName) != 0;

    public static (IntPtr baseAddr, int size) FindModule(int pid, string moduleName)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            foreach (ProcessModule m in p.Modules)
            {
                if (m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    return (m.BaseAddress, m.ModuleMemorySize);
            }
        }
        catch { }
        return (IntPtr.Zero, 0);
    }
}