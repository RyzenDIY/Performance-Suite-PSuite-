using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PSuite.Core
{
    public class DiskInfo
    {
        public string Name { get; set; } = string.Empty;
        public double TotalGb { get; set; }
        public double FreeGb { get; set; }
        public string MediaType { get; set; } = string.Empty;
    }

    public class NetworkAdapterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double SpeedMbps { get; set; }
        public bool IsUp { get; set; }
    }

    public class DisplayInfo
    {
        public string Name { get; set; } = string.Empty;
        public int WidthPx { get; set; }
        public int HeightPx { get; set; }
        public double RefreshHz { get; set; }
    }

    public class RamModuleInfo
    {
        public string Manufacturer { get; set; } = "Неизвестно";
        public double CapacityGb { get; set; }
        public int SpeedMhz { get; set; }
        public string Slot { get; set; } = string.Empty;
    }

    public class SystemProfile
    {
        public string CpuName { get; set; } = "Неизвестно";
        public int CpuClockMhz { get; set; }
        public int LogicalProcessors { get; set; }
        public int PhysicalCores { get; set; }
        public int L2CacheKb { get; set; }
        public int L3CacheKb { get; set; }
        public string OsArchitecture { get; set; } = "Неизвестно";

        public double TotalRamGb { get; set; }
        public double AvailableRamGb { get; set; }
        public double TotalPageFileGb { get; set; }
        public double AvailablePageFileGb { get; set; }
        public List<RamModuleInfo> RamModules { get; set; } = new();

        public List<string> GpuNames { get; set; } = new();
        public List<string> GpuDriverVersions { get; set; } = new();
        public List<double> GpuVramGb { get; set; } = new();
        public List<DiskInfo> Disks { get; set; } = new();
        public List<string> PhysicalDiskTypes { get; set; } = new();

        public string MotherboardInfo { get; set; } = "Неизвестно";
        public string BiosInfo { get; set; } = "Неизвестно";

        public string WindowsBuild { get; set; } = "Неизвестно";
        public string ActivationStatus { get; set; } = "Неизвестно";
        public TimeSpan Uptime { get; set; }

        public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = new();

        // null = couldn't determine (not the same as "off" — an honest
        // "unknown" rather than a guess).
        public bool? DefenderRealtimeProtectionEnabled { get; set; }
        public bool? FirewallEnabled { get; set; }
        public bool? TpmPresent { get; set; }

        public bool HasBattery { get; set; }
        public int? BatteryPercent { get; set; }
        public bool? BatteryCharging { get; set; }

        public int StartupAppCount { get; set; }

        public DateTime? WindowsInstallDate { get; set; }
        public int RunningProcessCount { get; set; }
        public List<DisplayInfo> Displays { get; set; } = new();
        public string DotNetRuntimeVersion { get; set; } = "Неизвестно";
        public List<string> UsbDevices { get; set; } = new();

        public string AntivirusProductName { get; set; } = "Неизвестно";
        public bool? VirtualizationFirmwareEnabled { get; set; }
        public long? PageFileSizeMb { get; set; }
        public string PageFileLocation { get; set; } = "Неизвестно";
        public DateTime? LastWindowsUpdateDate { get; set; }
        public int RecentWindowsUpdateCount { get; set; }

        public List<string> Recommendations { get; set; } = new();
    }

    // Read-only system snapshot for the "Анализ" view: CPU/RAM/GPU/disks/
    // motherboard/BIOS/Windows build/uptime plus a couple of
    // security-relevant flags (VBS/HVCI), and a few conservative, static
    // recommendations shown before the person applies tweaks. Registry
    // reads, WMI (System.Management) and one Win32 API call only —
    // nothing here writes anything or needs admin rights. Analyzer
    // informs the person; it never gates or auto-applies tweaks.
    public static class SystemAnalyzer
    {
        public static SystemProfile Analyze()
        {
            var profile = new SystemProfile
            {
                LogicalProcessors = Environment.ProcessorCount,
                OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                CpuName = ReadCpuName(),
                CpuClockMhz = ReadCpuClockMhz(),
                WindowsBuild = ReadWindowsBuild(),
                ActivationStatus = ReadActivationStatus(),
                GpuNames = ReadGpuNames(),
                GpuDriverVersions = ReadGpuDriverVersions(),
                GpuVramGb = ReadGpuVramGb(),
                Disks = ReadDisks(),
                PhysicalDiskTypes = ReadPhysicalDiskTypes(),
                MotherboardInfo = ReadMotherboardInfo(),
                BiosInfo = ReadBiosInfo(),
                RamModules = ReadRamModules(),
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
                NetworkAdapters = ReadNetworkAdapters(),
                DefenderRealtimeProtectionEnabled = ReadDefenderRealtimeProtectionEnabled(),
                FirewallEnabled = ReadFirewallEnabled(),
                TpmPresent = ReadTpmPresent(),
                StartupAppCount = ReadStartupAppCount(),
                WindowsInstallDate = ReadWindowsInstallDate(),
                RunningProcessCount = System.Diagnostics.Process.GetProcesses().Length,
                Displays = ReadDisplays(),
                DotNetRuntimeVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                UsbDevices = ReadUsbDevices(),
                AntivirusProductName = ReadAntivirusProductName(),
                VirtualizationFirmwareEnabled = ReadVirtualizationFirmwareEnabled()
            };

            var pageFile = ReadPageFileInfo();
            profile.PageFileSizeMb = pageFile.sizeMb;
            profile.PageFileLocation = pageFile.location;

            var updates = ReadWindowsUpdateInfo();
            profile.LastWindowsUpdateDate = updates.lastDate;
            profile.RecentWindowsUpdateCount = updates.recentCount;

            var battery = ReadBatteryStatus();
            profile.HasBattery = battery.present;
            profile.BatteryPercent = battery.percent;
            profile.BatteryCharging = battery.charging;

            var cpuDetails = ReadCpuCoresAndCache();
            profile.PhysicalCores = cpuDetails.cores;
            profile.L2CacheKb = cpuDetails.l2Kb;
            profile.L3CacheKb = cpuDetails.l3Kb;

            var mem = ReadMemoryStatus();
            profile.TotalRamGb = mem.totalPhysGb;
            profile.AvailableRamGb = mem.availPhysGb;
            profile.TotalPageFileGb = mem.totalPageFileGb;
            profile.AvailablePageFileGb = mem.availPageFileGb;

            profile.Recommendations = BuildRecommendations(profile);
            return profile;
        }

        // WMI (Win32_BaseBoard) — the standard, documented source for
        // motherboard identity. Returns "Неизвестно" if WMI is
        // unavailable/blocked rather than guessing.
        private static string ReadMotherboardInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var manufacturer = obj["Manufacturer"]?.ToString()?.Trim();
                    var product = obj["Product"]?.ToString()?.Trim();
                    var combined = string.Join(" ", new[] { manufacturer, product }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(combined)) return combined;
                }
            }
            catch
            {
                // WMI can be disabled by policy on locked-down systems —
                // that's a valid state, not a crash.
            }
            return "Неизвестно";
        }

        private static string ReadBiosInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var manufacturer = obj["Manufacturer"]?.ToString()?.Trim();
                    var version = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim();
                    var releaseDate = "";
                    var releaseDateRaw = obj["ReleaseDate"]?.ToString();
                    if (!string.IsNullOrEmpty(releaseDateRaw))
                    {
                        try { releaseDate = ManagementDateTimeConverter.ToDateTime(releaseDateRaw).ToString("yyyy-MM-dd"); }
                        catch { /* unparseable date format — skip it, keep the rest */ }
                    }

                    var parts = new[] { manufacturer, version, releaseDate }.Where(s => !string.IsNullOrWhiteSpace(s));
                    var combined = string.Join(" · ", parts);
                    if (!string.IsNullOrWhiteSpace(combined)) return combined;
                }
            }
            catch
            {
                // as above
            }
            return "Неизвестно";
        }

        // WMI (Win32_PhysicalMemory) — one entry per installed RAM stick,
        // with real manufacturer/speed/slot from SPD data the firmware
        // exposes. This is genuinely not available from the registry.
        // System.Net.NetworkInformation is the reliable, built-in .NET
        // source for adapter info — no WMI needed, works the same on
        // every Windows version. Skips loopback/tunnel pseudo-adapters
        // since those aren't meaningful to show here.
        private static List<NetworkAdapterInfo> ReadNetworkAdapters()
        {
            var result = new List<NetworkAdapterInfo>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                        continue;

                    result.Add(new NetworkAdapterInfo
                    {
                        Name = nic.Name,
                        Type = nic.NetworkInterfaceType.ToString(),
                        SpeedMbps = nic.Speed > 0 ? Math.Round(nic.Speed / 1_000_000.0, 0) : 0,
                        IsUp = nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    });
                }
            }
            catch
            {
                // Empty list is an honest "couldn't enumerate", not a crash.
            }
            return result;
        }

        // MSFT_MpComputerStatus is the same WMI class Windows Security
        // itself reads from. Returns null (not false) if WMI is
        // unavailable or a third-party AV has fully replaced Defender —
        // "unknown" is honest, "off" would be a guess.
        private static bool? ReadDefenderRealtimeProtectionEnabled()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender",
                    "SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["RealTimeProtectionEnabled"] is bool enabled) return enabled;
                }
            }
            catch
            {
                // Namespace doesn't exist when Defender is fully disabled
                // by a third-party AV taking over — genuinely unknown.
            }
            return null;
        }

        // Reads the three standard Windows Firewall profiles directly
        // from the registry (Domain/Private/Public) — the same values
        // netsh/Windows Security surface. Returns true only if ALL
        // present profiles are enabled, false if ANY is off, null if the
        // key can't be read at all.
        private static bool? ReadFirewallEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy");
                if (key == null) return null;

                bool? result = null;
                foreach (var profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
                {
                    using var profileKey = key.OpenSubKey(profile);
                    if (profileKey?.GetValue("EnableFirewall") is int enabled)
                    {
                        result ??= true;
                        if (enabled == 0) result = false;
                    }
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        // Win32_Tpm lives in a dedicated WMI namespace that simply
        // doesn't exist on hardware without a TPM at all — that's a
        // legitimate "no", not an error to hide.
        private static bool? ReadTpmPresent()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\CIMV2\Security\MicrosoftTpm", "SELECT IsActivated_InitialValue FROM Win32_Tpm");
                foreach (ManagementObject _ in searcher.Get())
                {
                    return true; // any row back means a TPM object exists
                }
                return false;
            }
            catch
            {
                return null;
            }
        }

        // Counts entries in both the per-user and machine-wide Run keys
        // — the same two locations Task Manager's Startup tab reads from
        // for classic (non-packaged) startup apps. Doesn't attempt to
        // also enumerate the Startup folder or packaged apps' StartupTask
        // registrations — that would need more surface area than is
        // worth it for a single "how many" number.
        private static int ReadStartupAppCount()
        {
            var count = 0;
            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (hkcu != null) count += hkcu.GetValueNames().Length;
            }
            catch { /* honest zero-contribution on failure */ }

            try
            {
                using var hklm = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (hklm != null) count += hklm.GetValueNames().Length;
            }
            catch { /* honest zero-contribution on failure */ }

            return count;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        // GetSystemPowerStatus is the standard Win32 API for this — works
        // identically whether the machine has a battery or not.
        // BatteryFlag 0x80 = "no system battery" (desktop PC), which we
        // report as present=false rather than 0%.
        private static (bool present, int? percent, bool? charging) ReadBatteryStatus()
        {
            try
            {
                if (!GetSystemPowerStatus(out var status)) return (false, null, null);
                if (status.BatteryFlag == 0x80 || status.BatteryLifePercent == 255) return (false, null, null);

                var charging = (status.BatteryFlag & 0x08) != 0;
                return (true, status.BatteryLifePercent, charging);
            }
            catch
            {
                return (false, null, null);
            }
        }

        // InstallDate lives in the registry as a Unix timestamp — this is
        // literally what "winver" and Settings > About read from.
        private static DateTime? ReadWindowsInstallDate()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key?.GetValue("InstallDate") is int unixSeconds)
                    return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            }
            catch { /* honest unknown */ }
            return null;
        }

        // Win32_VideoController's Current* fields are the actual active
        // mode Windows is driving the display at right now (not just the
        // maximum the panel supports) — the same numbers Settings >
        // Display shows.
        private static List<DisplayInfo> ReadDisplays()
        {
            var result = new List<DisplayInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["CurrentHorizontalResolution"] is uint w && obj["CurrentVerticalResolution"] is uint h && w > 0 && h > 0)
                    {
                        result.Add(new DisplayInfo
                        {
                            Name = obj["Name"] as string ?? "Дисплей",
                            WidthPx = (int)w,
                            HeightPx = (int)h,
                            RefreshHz = obj["CurrentRefreshRate"] is uint hz ? hz : 0
                        });
                    }
                }
            }
            catch
            {
                // Empty list — honest "couldn't enumerate", not a crash.
            }
            return result;
        }

        // Win32_PnPEntity filtered to USB devices — the same source
        // Device Manager reads from. Skips generic USB hub/root-hub
        // entries since those aren't meaningful to show to a person (they
        // exist on every PC and say nothing about what's plugged in).
        private static List<string> ReadUsbDevices()
        {
            var result = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["Name"] is string name &&
                        !name.Contains("Root Hub", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("Generic USB Hub", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name);
                    }
                }
            }
            catch
            {
                // Empty list on failure.
            }
            return result.Distinct().ToList();
        }

        // SecurityCenter2 is the same Windows Security Center registry
        // Windows itself uses to show "which antivirus is protecting
        // you" — it lists Defender too when nothing else is registered,
        // so this naturally covers both cases without special-casing.
        private static string ReadAntivirusProductName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
                var names = new List<string>();
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["displayName"] is string name && !string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
                return names.Count > 0 ? string.Join(", ", names.Distinct()) : "Не определён";
            }
            catch
            {
                return "Неизвестно";
            }
        }

        // VirtualizationFirmwareEnabled reflects whether VT-x/AMD-V is
        // actually turned ON in BIOS/UEFI right now — not just whether
        // the CPU supports it. This is exactly what WSL2/Hyper-V/most VM
        // software checks for.
        private static bool? ReadVirtualizationFirmwareEnabled()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["VirtualizationFirmwareEnabled"] is bool enabled) return enabled;
                }
            }
            catch { /* honest unknown */ }
            return null;
        }

        // Win32_PageFileUsage reports the page file(s) actually in use
        // right now (size + path) — the same numbers Task Manager /
        // System Properties show, not a guess.
        private static (long? sizeMb, string location) ReadPageFileInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"] as string ?? "Неизвестно";
                    var size = obj["AllocatedBaseSize"] is uint mb ? (long)mb : (long?)null;
                    return (size, name);
                }
            }
            catch { /* fall through to honest unknown below */ }
            return (null, "Не найден (возможно отключён)");
        }

        // Win32_QuickFixEngineering is the same source "wmic qfe list"
        // and Settings > Update history read from. InstalledOn parses
        // inconsistently across locales via WMI's date format, so this
        // sorts defensively and just reports the newest parseable date
        // plus how many hotfixes are recorded in total — not a fabricated
        // "last checked" time, since PSuite never actually checks for
        // updates itself.
        private static (DateTime? lastDate, int recentCount) ReadWindowsUpdateInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT InstalledOn FROM Win32_QuickFixEngineering");
                DateTime? newest = null;
                var count = 0;
                foreach (ManagementObject obj in searcher.Get())
                {
                    count++;
                    if (obj["InstalledOn"] is string raw && DateTime.TryParse(raw, out var parsed))
                    {
                        if (newest == null || parsed > newest) newest = parsed;
                    }
                }
                return (newest, count);
            }
            catch
            {
                return (null, 0);
            }
        }

        private static List<RamModuleInfo> ReadRamModules()
        {
            var modules = new List<RamModuleInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Manufacturer, Capacity, Speed, DeviceLocator FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var manufacturer = obj["Manufacturer"]?.ToString()?.Trim();
                    var slot = obj["DeviceLocator"]?.ToString()?.Trim() ?? "";

                    double capacityGb = 0;
                    if (obj["Capacity"] != null && ulong.TryParse(obj["Capacity"].ToString(), out var bytes))
                        capacityGb = bytes / (1024.0 * 1024 * 1024);

                    var speedMhz = 0;
                    if (obj["Speed"] != null) int.TryParse(obj["Speed"].ToString(), out speedMhz);

                    modules.Add(new RamModuleInfo
                    {
                        Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? "Неизвестно" : manufacturer,
                        CapacityGb = capacityGb,
                        SpeedMhz = speedMhz,
                        Slot = slot
                    });
                }
            }
            catch
            {
                // as above — an empty list just means "not detected"
            }
            return modules;
        }

        private static string ReadCpuName()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Неизвестно";
            }
            catch
            {
                return "Неизвестно";
            }
        }

        // "~MHz" is the CPU's rated base clock as reported by firmware at
        // boot — not a live/turbo reading (that needs an MSR-level driver
        // this project deliberately doesn't ship), but a real, documented
        // registry value, not a guess.
        private static int ReadCpuClockMhz()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("~MHz") is int mhz ? mhz : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string ReadWindowsBuild()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var build = key?.GetValue("CurrentBuildNumber") as string;
                var ubr = key?.GetValue("UBR");
                if (build == null) return "Неизвестно";
                return ubr != null ? $"{build}.{ubr}" : build;
            }
            catch
            {
                return "Неизвестно";
            }
        }

        private static List<string> ReadGpuNames()
        {
            var names = new List<string>();
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey == null) return names;

                foreach (var subKeyName in classKey.GetSubKeyNames())
                {
                    if (subKeyName.Length != 4 || !subKeyName.All(char.IsDigit)) continue;

                    using var sub = classKey.OpenSubKey(subKeyName);
                    var desc = sub?.GetValue("DriverDesc") as string;
                    if (!string.IsNullOrWhiteSpace(desc) && !names.Contains(desc))
                        names.Add(desc);
                }
            }
            catch
            {
                // best-effort — an empty list just means "not detected"
            }
            return names;
        }

        // Same registry branch as ReadGpuNames — DriverVersion sits right
        // next to DriverDesc for each adapter subkey.
        private static List<string> ReadGpuDriverVersions()
        {
            var versions = new List<string>();
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey == null) return versions;

                foreach (var subKeyName in classKey.GetSubKeyNames())
                {
                    if (subKeyName.Length != 4 || !subKeyName.All(char.IsDigit)) continue;

                    using var sub = classKey.OpenSubKey(subKeyName);
                    var version = sub?.GetValue("DriverVersion") as string;
                    if (!string.IsNullOrWhiteSpace(version) && !versions.Contains(version))
                        versions.Add(version);
                }
            }
            catch
            {
                // best-effort
            }
            return versions;
        }

        // MSFT_PhysicalDisk (Storage Management WMI namespace) is the
        // documented way to get real SSD/HDD classification — unlike
        // Win32_DiskDrive.MediaType, which usually just says "Fixed hard
        // disk media" for everything. Not correlated to drive letters
        // (that needs a multi-table WMI join); reported as a simple list
        // of physical disks instead.
        private static List<string> ReadPhysicalDiskTypes()
        {
            var result = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT FriendlyName, MediaType FROM MSFT_PhysicalDisk");

                foreach (ManagementObject disk in searcher.Get())
                {
                    var name = disk["FriendlyName"] as string ?? "Диск";
                    var mediaType = Convert.ToInt32(disk["MediaType"] ?? 0) switch
                    {
                        3 => "HDD",
                        4 => "SSD",
                        5 => "SCM",
                        _ => "тип неизвестен"
                    };
                    result.Add($"{name} ({mediaType})");
                }
            }
            catch
            {
                // best-effort — Storage WMI namespace not always available
            }
            return result;
        }

        // Dedicated video memory per adapter, via DXGI (Vortice bindings —
        // same package the GPU benchmark test uses). Wrapped like every
        // other reader here: any DirectX/driver issue just means an empty
        // list, not a crash — Analysis has to stay readable either way.
        private static List<double> ReadGpuVramGb()
        {
            var result = new List<double>();
            try
            {
                using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
                for (uint i = 0; i < 8; i++)
                {
                    var hr = factory.EnumAdapters1(i, out Vortice.DXGI.IDXGIAdapter1 adapter);
                    if (hr.Failure || adapter == null) break;

                    using (adapter)
                    {
                        var desc = adapter.Description1;
                        if (desc.DedicatedVideoMemory > 0)
                            result.Add(desc.DedicatedVideoMemory / (1024d * 1024 * 1024));
                    }
                }
            }
            catch
            {
                // DirectX/driver unavailable — leave empty, not fatal for Analysis.
            }
            return result;
        }

        private static List<DiskInfo> ReadDisks()
        {
            var disks = new List<DiskInfo>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                    disks.Add(new DiskInfo
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        TotalGb = drive.TotalSize / (1024d * 1024 * 1024),
                        FreeGb = drive.AvailableFreeSpace / (1024d * 1024 * 1024)
                    });
                }
            }
            catch
            {
                // best-effort
            }
            return disks;
        }


        // Win32_Processor gives physical core count and cache sizes that
        // the registry simply doesn't expose — this is exactly the kind
        // of thing WMI is for.
        private static (int cores, int l2Kb, int l3Kb) ReadCpuCoresAndCache()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT NumberOfCores, L2CacheSize, L3CacheSize FROM Win32_Processor");

                foreach (ManagementObject cpu in searcher.Get())
                {
                    var cores = Convert.ToInt32(cpu["NumberOfCores"] ?? 0);
                    var l2 = Convert.ToInt32(cpu["L2CacheSize"] ?? 0);
                    var l3 = Convert.ToInt32(cpu["L3CacheSize"] ?? 0);
                    return (cores, l2, l3); // first CPU is enough for a desktop/laptop
                }
            }
            catch
            {
                // best-effort
            }
            return (0, 0, 0);
        }

        // SoftwareLicensingProduct is the same WMI class the built-in
        // "slmgr.vbs /dli" tool queries for activation status — no
        // registry equivalent exists for this.
        private static string ReadActivationStatus()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct " +
                    "WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'");

                foreach (ManagementObject product in searcher.Get())
                {
                    var status = Convert.ToInt32(product["LicenseStatus"] ?? -1);
                    return status switch
                    {
                        1 => "Активирована",
                        0 => "Не активирована",
                        2 => "Пробный период",
                        5 => "Уведомление (истёк грейс-период)",
                        _ => "Неизвестно"
                    };
                }
            }
            catch
            {
                // best-effort — WMI licensing queries can be blocked by policy
            }
            return "Неизвестно";
        }

        private static (double totalPhysGb, double availPhysGb, double totalPageFileGb, double availPageFileGb) ReadMemoryStatus()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status))
                return (0, 0, 0, 0);

            const double bytesPerGb = 1024d * 1024 * 1024;
            return (
                status.ullTotalPhys / bytesPerGb,
                status.ullAvailPhys / bytesPerGb,
                status.ullTotalPageFile / bytesPerGb,
                status.ullAvailPageFile / bytesPerGb);
        }

        // `powercfg /getactivescheme` is the documented CLI for the active
        // power plan — no reliable registry equivalent exists (scheme
        // names are stored as localized resource references, not plain
        // strings). Same short-timeout, best-effort pattern as the
        // bcdedit/manage-bde calls in SecurityAnalyzer.
        public static async Task<string> ReadActivePowerPlanAsync()
        {
            try
            {
                // powercfg.exe writes its output in the console's OEM
                // codepage (e.g. CP866 on a Russian-locale Windows), not
                // UTF-8 — reading those bytes as UTF-8 produces mojibake.
                // Forcing the child console to UTF-8 (chcp 65001) before
                // running powercfg sidesteps that without needing the
                // legacy-codepage NuGet package.
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c chcp 65001>nul && powercfg /getactivescheme",
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) return "Неизвестно";

                var output = await process.StandardOutput.ReadToEndAsync();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cts.Token);

                // Typical line: "Схема питания GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Сбалансированная)"
                var match = System.Text.RegularExpressions.Regex.Match(output, @"\(([^)]+)\)");
                return match.Success ? match.Groups[1].Value : "Неизвестно";
            }
            catch
            {
                return "Неизвестно";
            }
        }

        private static List<string> BuildRecommendations(SystemProfile profile)
        {
            var list = new List<string>();

            if (profile.TotalRamGb > 0 && profile.TotalRamGb < 8.5)
                list.Add("Меньше 8 ГБ ОЗУ — часть твиков для игр даст меньше эффекта, чем на системах с 16+ ГБ.");

            var lowDisk = profile.Disks.FirstOrDefault(d => d.TotalGb > 0 && d.FreeGb / d.TotalGb < 0.1);
            if (lowDisk != null)
                list.Add($"На диске {lowDisk.Name} осталось меньше 10% свободного места — это само по себе может влиять на производительность и создание точек отката.");

            if (list.Count == 0)
                list.Add("Явных предупреждений нет — ориентируйтесь на риск-метку каждого твика.");

            return list;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
