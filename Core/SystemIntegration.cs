using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace PSuite.Core
{
    // Three honest system-integration helpers:
    //  - Windows startup, via the real StartupTask extension declared in
    //    Package.appxmanifest (TaskId "PSuiteStartupTaskId") — NOT a raw
    //    registry Run-key write. That's what makes it show up correctly
    //    in Task Manager's Startup tab and Settings > Apps > Startup, and
    //    what lets Windows itself report "disabled by user/policy" instead
    //    of us lying about the state.
    //  - Elevation check (single source of truth for the whole app).
    //  - System Restore point creation, run through PowerShell with
    //    UTF-8 forced end-to-end so Cyrillic text in error messages
    //    doesn't come back as garbled mojibake, and with a friendly
    //    translation for the failure that actually happens in practice
    //    (Checkpoint-Computer silently requires an elevated process).
    //
    // Every public method reports success/failure instead of throwing,
    // so the UI can show what actually happened.
    public static class SystemIntegration
    {
        private const string StartupTaskId = "PSuiteStartupTaskId";

        public static bool IsRunningElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // ---- Startup (real StartupTask API, not a registry Run key) -----------

        public static async Task<(bool Enabled, StartupTaskState State)> GetStartupStateAsync()
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                var enabled = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                return (enabled, task.State);
            }
            catch
            {
                // Extension not found (e.g. running unpackaged during
                // development) — nothing to report as on.
                return (false, StartupTaskState.Disabled);
            }
        }

        // Returns the state Windows actually settled on, which may not
        // match what was requested. Turning "on" can come back
        // DisabledByUser (turned off previously in Windows Settings —
        // Windows remembers that override, we don't get to silently
        // reverse it) or DisabledByPolicy (blocked by this machine's
        // admin). The caller reverts the toggle on failure instead of
        // showing a state that isn't real.
        public static async Task<(bool Success, string? Message)> SetStartupEnabledAsync(bool enable)
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);

                if (!enable)
                {
                    task.Disable();
                    return (true, null);
                }

                var newState = await task.RequestEnableAsync();
                return newState switch
                {
                    StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => (true, null),
                    StartupTaskState.DisabledByUser => (false,
                        "автозагрузка ранее отключена вручную в Параметры → Приложения → Автозагрузка — это ограничение Windows, не PSuite"),
                    StartupTaskState.DisabledByPolicy => (false,
                        "автозагрузка запрещена политикой на этом компьютере"),
                    _ => (false, $"Windows вернула неожиданное состояние: {newState}")
                };
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ---- System Restore point ---------------------------------------------

        // Creates a real Windows System Restore point via the
        // Checkpoint-Computer PowerShell cmdlet. Checkpoint-Computer
        // needs an elevated process — if PSuite itself isn't running as
        // admin, it fails every time, so that's checked and reported
        // specifically rather than surfacing a generic WMI error.
        public static async Task<(bool Success, string Message)> TryCreateRestorePointAsync(string description)
        {
            if (!IsRunningElevated())
            {
                return (false, "требуются права администратора — запустите PSuite от имени администратора");
            }

            try
            {
                // -EncodedCommand (UTF-16LE, Base64) sidesteps all
                // cmd/PowerShell quoting rules entirely — no risk of the
                // Cyrillic description or embedded quotes breaking the
                // command line. Forcing both PowerShell's console
                // encoding and the .NET read encoding to UTF-8 means
                // error text comes back readable instead of garbled.
                var safeDescription = description.Replace("'", "''");
                var script =
                    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                    "$OutputEncoding = [System.Text.Encoding]::UTF8; " +
                    $"Checkpoint-Computer -Description '{safeDescription}' -RestorePointType MODIFY_SETTINGS";
                var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return (false, "не удалось запустить PowerShell");

                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                var stderr = (await stderrTask).Trim();

                if (process.ExitCode == 0)
                {
                    return (true,
                        "Точка восстановления запрошена. Если System Restore отключён для диска C: или точка уже создавалась за последние 24 часа, Windows могла её не создать — это ограничение самой системы.");
                }

                return (false, TranslateRestorePointError(stderr));
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Maps the Checkpoint-Computer failure that actually happens in
        // practice (System Restore off for C:) to a plain-language
        // explanation instead of a raw WMI exception dump.
        private static string TranslateRestorePointError(string rawStderr)
        {
            if (rawStderr.Contains("ManagementException", StringComparison.OrdinalIgnoreCase) ||
                rawStderr.Contains("GetWMIManagementException", StringComparison.OrdinalIgnoreCase))
            {
                return "защита системы (System Restore) выключена для диска C:. Включить: Параметры → Система → О программе → Защита системы";
            }

            if (string.IsNullOrWhiteSpace(rawStderr))
                return "Checkpoint-Computer завершился с ошибкой без подробностей";

            var firstLine = rawStderr.Split('\n')[0].Trim();
            return firstLine.Length > 0 ? firstLine : "не удалось создать точку восстановления по неизвестной причине";
        }
    }
}
