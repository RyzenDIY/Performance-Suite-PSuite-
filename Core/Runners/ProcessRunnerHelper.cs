using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PSuite.Core.Runners
{
    // Shared by BatRunner/CmdRunner/PowerShellRunner: launches a process,
    // captures combined stdout, enforces the timeout, and always returns a
    // RunnerExecutionResult — never throws out to the caller. Not itself
    // an IScriptRunner; each concrete runner owns the CanRun()/Name/
    // command-line-building decisions and calls this to do the actual work.
    internal static class ProcessRunnerHelper
    {
        public static async Task<RunnerExecutionResult> RunAsync(
            string runnerName, string fileName, string arguments, TimeSpan timeout)
        {
            var result = new RunnerExecutionResult { RunnerName = runnerName };
            var sw = Stopwatch.StartNew();

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = new CancellationTokenSource(timeout);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    sw.Stop();
                    result.Success = false;
                    result.TimedOut = true;
                    result.ExecutionTime = sw.Elapsed;
                    result.ErrorMessage = $"Скрипт превысил время ожидания ({timeout.TotalSeconds} сек).";
                    return result;
                }

                sw.Stop();
                result.ExitCode = process.ExitCode;
                result.ExecutionTime = sw.Elapsed;
                result.ConsoleOutput = stdout.ToString();

                if (process.ExitCode != 0)
                {
                    result.Success = false;
                    var stderrText = stderr.ToString().Trim();
                    result.ErrorMessage = string.IsNullOrEmpty(stderrText)
                        ? $"Скрипт завершился с кодом {process.ExitCode}."
                        : stderrText;
                }
                else
                {
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Success = false;
                result.ExecutionTime = sw.Elapsed;
                result.ErrorMessage = $"Не удалось запустить скрипт: {ex.Message}";
            }

            return result;
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best-effort */ }
        }
    }
}
