using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PSuite.Core
{
    public class SecurityProfile
    {
        public bool? SecureBootEnabled { get; set; }
        public bool? VbsEnabled { get; set; }
        public bool? HvciEnabled { get; set; }              // Memory Integrity / Core Isolation
        public bool? TpmPresent { get; set; }
        public bool? DefenderRealtimeProtectionEnabled { get; set; }
        public bool? TestModeEnabled { get; set; }           // driver signature enforcement OFF
        public bool? KernelDebugEnabled { get; set; }
        public string BitLockerStatus { get; set; } = "Неизвестно";

        public int SecurityScore { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    // Security-posture snapshot: Secure Boot / VBS / HVCI / TPM /
    // BitLocker / Defender / Test Mode / Kernel Debug, plus a simple
    // 0-100 score. Registry reads for everything except BitLocker and
    // Test/Debug mode, which come from the documented `manage-bde` and
    // `bcdedit` command-line tools (parsed output, not scraped internals).
    // Nothing here is written, nothing needs a kernel driver.
    public static class SecurityAnalyzer
    {
        public static async Task<SecurityProfile> AnalyzeAsync()
        {
            var profile = new SecurityProfile
            {
                SecureBootEnabled = ReadSecureBootEnabled(),
                VbsEnabled = ReadVbsEnabled(),
                HvciEnabled = ReadHvciEnabled(),
                TpmPresent = ReadTpmPresent(),
                DefenderRealtimeProtectionEnabled = ReadDefenderRealtimeProtectionEnabled()
            };

            var bcdInfo = await ReadBcdFlagsAsync();
            profile.TestModeEnabled = bcdInfo.testMode;
            profile.KernelDebugEnabled = bcdInfo.kernelDebug;

            profile.BitLockerStatus = await ReadBitLockerStatusAsync();

            profile.SecurityScore = ComputeScore(profile);
            profile.Recommendations = BuildRecommendations(profile);
            return profile;
        }

        private static bool? ReadSecureBootEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                return key?.GetValue("UEFISecureBootEnabled") is int i ? i != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        // The DeviceGuard branch simply doesn't exist on most consumer
        // installs that never had VBS turned on via policy/Core
        // Isolation — that's a real "off", not an unknown. Only a genuine
        // access failure (caught below) is reported as unknown.
        private static bool? ReadVbsEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard");
                if (key == null) return false;
                return key.GetValue("EnableVirtualizationBasedSecurity") is int i ? i != 0 : false;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadHvciEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                if (key == null) return false;
                return key.GetValue("Enabled") is int i ? i != 0 : false;
            }
            catch
            {
                return null;
            }
        }

        // Best-effort: the driver-registration key existing is a weak
        // signal, not a real "TPM is present and ready" check (that needs
        // the root\CIMV2\Security\MicrosoftTpm WMI namespace, which would
        // pull in a System.Management dependency this project doesn't
        // have yet). Documented here rather than hidden.
        private static bool? ReadTpmPresent()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\TPM");
                return key != null;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadDefenderRealtimeProtectionEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
                // DisableRealtimeMonitoring: 1 = OFF. Key/value absent on
                // a default install almost always means it's ON.
                var disabled = key?.GetValue("DisableRealtimeMonitoring") as int?;
                return disabled == null ? true : disabled == 0;
            }
            catch
            {
                return null;
            }
        }

        // `bcdedit /enum` is the documented, supported way to read the
        // active boot entry's flags — it's what msinfo32 and Windows
        // itself use internally. We only parse two known lines.
        private static async Task<(bool? testMode, bool? kernelDebug)> ReadBcdFlagsAsync()
        {
            var output = await RunCommandAsync("bcdedit.exe", "/enum");
            if (output == null) return (null, null);

            bool? testMode = null;
            bool? kernelDebug = null;

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("testsigning", StringComparison.OrdinalIgnoreCase))
                    testMode = line.Contains("Yes", StringComparison.OrdinalIgnoreCase);
                else if (line.StartsWith("debug", StringComparison.OrdinalIgnoreCase) &&
                         !line.StartsWith("debugoptionenabled", StringComparison.OrdinalIgnoreCase))
                    kernelDebug = line.Contains("Yes", StringComparison.OrdinalIgnoreCase);
            }

            return (testMode, kernelDebug);
        }

        // `manage-bde -status` is the documented CLI for BitLocker status
        // (same tool the "Manage BitLocker" control panel applet shells
        // out to). Parsed defensively — different Windows locales/builds
        // phrase the status line slightly differently, so on anything
        // unexpected we report "Неизвестно" rather than guessing.
        private static async Task<string> ReadBitLockerStatusAsync()
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var output = await RunCommandAsync("manage-bde.exe", $"-status {systemDrive}");
            if (output == null) return "Неизвестно (manage-bde недоступен)";

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("Protection Status", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Состояние защиты", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.Contains("Protection On", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("защита включена", StringComparison.OrdinalIgnoreCase))
                    return $"Включено ({systemDrive})";

                if (line.Contains("Protection Off", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("защита выключена", StringComparison.OrdinalIgnoreCase))
                    return $"Выключено ({systemDrive})";

                return line;
            }

            return "Неизвестно";
        }

        private static async Task<string?> RunCommandAsync(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var stdout = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.BeginOutputReadLine();

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                    return null;
                }

                return process.ExitCode == 0 ? stdout.ToString() : null;
            }
            catch
            {
                // Tool missing, blocked by policy, or access denied — a
                // security snapshot should degrade to "unknown", not crash.
                return null;
            }
        }

        private static int ComputeScore(SecurityProfile p)
        {
            int score = 100;

            if (p.SecureBootEnabled == false) score -= 20;
            if (p.VbsEnabled == false) score -= 5;      // often disabled deliberately for gaming — small penalty
            if (p.HvciEnabled == false) score -= 5;     // same trade-off
            if (p.TpmPresent == false) score -= 10;
            if (p.DefenderRealtimeProtectionEnabled == false) score -= 25;
            if (p.TestModeEnabled == true) score -= 30; // disables driver signature enforcement — genuinely risky
            if (p.KernelDebugEnabled == true) score -= 20;
            if (p.BitLockerStatus.StartsWith("Выключено", StringComparison.OrdinalIgnoreCase)) score -= 15;

            return Math.Clamp(score, 0, 100);
        }

        private static List<string> BuildRecommendations(SecurityProfile p)
        {
            var list = new List<string>();

            if (p.TestModeEnabled == true)
                list.Add("Включён Test Mode (testsigning) — отключена проверка подписи драйверов. Это существенно снижает защиту системы.");

            if (p.KernelDebugEnabled == true)
                list.Add("Включён режим отладки ядра (kernel debug) — обычно нужен только разработчикам драйверов.");

            if (p.DefenderRealtimeProtectionEnabled == false)
                list.Add("Защита в реальном времени Defender выключена. Если вместо неё не стоит другой антивирус — система не защищена.");

            if (p.SecureBootEnabled == false)
                list.Add("Secure Boot выключен — часть защиты от буткитов недоступна.");

            if (p.VbsEnabled == false || p.HvciEnabled == false)
                list.Add("VBS/Memory Integrity выключены — это осознанный выбор части геймеров ради FPS, но снижает защиту от эксплойтов уровня ядра.");

            if (list.Count == 0)
                list.Add("Существенных проблем с безопасностью не обнаружено.");

            return list;
        }
    }
}
