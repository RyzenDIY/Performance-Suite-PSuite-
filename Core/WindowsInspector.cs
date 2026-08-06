using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace PSuite.Core
{
    public class WindowsProfile
    {
        public string Edition { get; set; } = "Неизвестно";
        public string BootMode { get; set; } = "Неизвестно";
        public TimeSpan Uptime { get; set; }

        public bool IsRunningAsAdministrator { get; set; }
        public bool? UacEnabled { get; set; }

        public bool? FastStartupEnabled { get; set; }
        public bool? HagsEnabled { get; set; }          // Hardware-Accelerated GPU Scheduling
        public bool? GameModeEnabled { get; set; }

        public bool? FirewallDomainEnabled { get; set; }
        public bool? FirewallPrivateEnabled { get; set; }
        public bool? FirewallPublicEnabled { get; set; }

        public bool? HyperVInstalled { get; set; }
    }

    // Read-only inspector for OS-level feature toggles that matter for a
    // performance-tweaking tool: Fast Startup, HAGS, Game Mode, firewall
    // profiles, Hyper-V, boot mode, UAC and elevation state. Everything
    // here is a registry read or a single documented Win32 identity
    // check — nothing is written, nothing needs a kernel driver.
    public static class WindowsInspector
    {
        public static WindowsProfile Inspect()
        {
            return new WindowsProfile
            {
                Edition = ReadEdition(),
                BootMode = ReadBootMode(),
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
                IsRunningAsAdministrator = IsRunningAsAdministrator(),
                UacEnabled = ReadUacEnabled(),
                FastStartupEnabled = ReadFastStartupEnabled(),
                HagsEnabled = ReadHagsEnabled(),
                GameModeEnabled = ReadGameModeEnabled(),
                FirewallDomainEnabled = ReadFirewallProfileEnabled("DomainProfile"),
                FirewallPrivateEnabled = ReadFirewallProfileEnabled("StandardProfile"),
                FirewallPublicEnabled = ReadFirewallProfileEnabled("PublicProfile"),
                HyperVInstalled = ReadHyperVInstalled()
            };
        }

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static string ReadEdition()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return key?.GetValue("ProductName") as string ?? "Неизвестно";
            }
            catch
            {
                return "Неизвестно";
            }
        }

        // UEFI systems always have this registry branch; classic BIOS
        // systems don't. This is the standard, documented way to tell
        // them apart without calling into firmware APIs directly.
        private static string ReadBootMode()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                return key != null ? "UEFI" : "Legacy BIOS";
            }
            catch
            {
                return "Неизвестно";
            }
        }

        private static bool? ReadUacEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                return key?.GetValue("EnableLUA") is int i ? i != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadFastStartupEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                // HiberbootEnabled: 1 = Fast Startup on, 0 = off. Absent on
                // systems where hibernation is disabled entirely.
                return key?.GetValue("HiberbootEnabled") is int i ? i != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadHagsEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                // HwSchMode: 2 = HAGS on, 1 = off. Key is absent on
                // driver/OS combinations that don't support HAGS at all.
                return key?.GetValue("HwSchMode") is int i ? i == 2 : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadGameModeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\GameBar");
                return key?.GetValue("AutoGameModeEnabled") is int i ? i != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadFirewallProfileEnabled(string profileKeyName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profileKeyName}");
                return key?.GetValue("EnableFirewall") is int i ? i != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        // Presence of the Hyper-V management service registration is a
        // reasonable proxy for "the Hyper-V Windows feature is installed"
        // without needing the DISM/WMI feature-enumeration API.
        private static bool? ReadHyperVInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\vmms");
                return key != null;
            }
            catch
            {
                return null;
            }
        }
    }
}
