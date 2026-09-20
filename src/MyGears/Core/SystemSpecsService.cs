using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MyGears.Core;

public class SystemSpecs
{
    public string CpuName { get; set; } = "Unknown CPU";
    public int CpuThreads { get; set; }
    public string GpuName { get; set; } = "Standard Display Adapter";
    public double TotalRamGb { get; set; }
    public double UsedRamGb { get; set; }
    public double FreeRamGb { get; set; }
    public int RamUsagePercent { get; set; }
    public string OsDisplay { get; set; } = "Windows";
    public string HostName { get; set; } = string.Empty;
    public string DiskDisplay { get; set; } = string.Empty;
    public string ActiveNetworkAdapter { get; set; } = "Ethernet/Wi-Fi";
}

public class LiveTelemetry
{
    public double CpuLoadPercent { get; set; }
    public int RamUsagePercent { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public long PingMs { get; set; } = -1;
    public string PingDisplay { get; set; } = "-- ms";
    public double DownloadSpeedKb { get; set; }
    public double UploadSpeedKb { get; set; }
    public string NetworkSpeedDisplay { get; set; } = "↓ 0 KB/s  ↑ 0 KB/s";
    public string ActiveAdapterName { get; set; } = "Đang kết nối";
}

public static class SystemSpecsService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    private static long _prevIdleTime;
    private static long _prevKernelTime;
    private static long _prevUserTime;
    private static double _lastCpuUsage = 0;
    private static DateTime _lastCpuSampleTime = DateTime.MinValue;

    private static long _prevRxBytes = -1;
    private static long _prevTxBytes = -1;
    private static DateTime _prevNetSampleTime = DateTime.MinValue;
    private static double _lastDownKb = 0;
    private static double _lastUpKb = 0;
    private static string _lastAdapterName = "Ethernet";
    private static long _lastPingMs = 30;
    private static bool _isPinging = false;

    private static SystemSpecs? _cachedSpecs;

    public static SystemSpecs GetSpecs()
    {
        if (_cachedSpecs != null)
        {
            UpdateRamSpecs(_cachedSpecs);
            return _cachedSpecs;
        }

        var specs = new SystemSpecs
        {
            HostName = Environment.MachineName,
            CpuThreads = Environment.ProcessorCount
        };

        // 1. CPU
        try
        {
            var rawCpu = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null)?.ToString();
            if (!string.IsNullOrWhiteSpace(rawCpu))
            {
                specs.CpuName = Regex.Replace(rawCpu, @"\s+", " ").Trim();
            }
        }
        catch
        {
            specs.CpuName = "Intel/AMD Processor";
        }

        // 2. GPU
        try
        {
            for (int i = 0; i < 10; i++)
            {
                string sub = i.ToString("D4");
                string? desc = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{sub}", "DriverDesc", null)?.ToString();
                if (!string.IsNullOrWhiteSpace(desc) && 
                    !desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) &&
                    !desc.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                {
                    specs.GpuName = desc.Trim();
                    break;
                }
            }
        }
        catch
        {
            specs.GpuName = "Card Rời Đồ Họa";
        }

        // 3. RAM
        UpdateRamSpecs(specs);

        // 4. OS
        try
        {
            string? prod = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", null)?.ToString();
            string? dispVer = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null)?.ToString();
            string? build = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", null)?.ToString();

            string os = prod ?? "Windows";
            if (int.TryParse(build, out int b) && b >= 22000 && os.StartsWith("Windows 10"))
            {
                os = "Windows 11" + os.Substring("Windows 10".Length);
            }
            if (!string.IsNullOrEmpty(dispVer))
            {
                os += $" {dispVer}";
            }
            if (Environment.Is64BitOperatingSystem)
            {
                os += " 64-bit";
            }
            specs.OsDisplay = os;
        }
        catch
        {
            specs.OsDisplay = Environment.OSVersion.ToString();
        }

        // 5. DISK
        try
        {
            var drive = new DriveInfo("C");
            if (drive.IsReady)
            {
                double freeGb = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 0);
                double totalGb = Math.Round((double)drive.TotalSize / (1024 * 1024 * 1024), 0);
                specs.DiskDisplay = $"Ổ C: {freeGb}GB trống / {totalGb}GB";
            }
        }
        catch
        {
            specs.DiskDisplay = "Ổ C: Đang hoạt động";
        }

        // 6. Network Adapter
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    specs.ActiveNetworkAdapter = ni.Name;
                    break;
                }
            }
        }
        catch { }

        _cachedSpecs = specs;
        return specs;
    }

    private static void UpdateRamSpecs(SystemSpecs specs)
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                specs.TotalRamGb = Math.Round((double)mem.ullTotalPhys / (1024 * 1024 * 1024), 1);
                specs.FreeRamGb = Math.Round((double)mem.ullAvailPhys / (1024 * 1024 * 1024), 1);
                specs.UsedRamGb = Math.Round(specs.TotalRamGb - specs.FreeRamGb, 1);
                specs.RamUsagePercent = (int)mem.dwMemoryLoad;
            }
        }
        catch { }
    }

    public static LiveTelemetry GetLiveTelemetry()
    {
        var tel = new LiveTelemetry();

        // 1. RAM
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                tel.RamTotalGb = Math.Round((double)mem.ullTotalPhys / (1024 * 1024 * 1024), 1);
                double freeGb = Math.Round((double)mem.ullAvailPhys / (1024 * 1024 * 1024), 1);
                tel.RamUsedGb = Math.Round(tel.RamTotalGb - freeGb, 1);
                tel.RamUsagePercent = (int)mem.dwMemoryLoad;
            }
        }
        catch { }

        // 2. CPU
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCpuSampleTime).TotalMilliseconds >= 400 && GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
            {
                if (_prevKernelTime != 0 || _prevUserTime != 0)
                {
                    long usr = userTime - _prevUserTime;
                    long ker = kernelTime - _prevKernelTime;
                    long idl = idleTime - _prevIdleTime;
                    long sys = ker + usr;

                    if (sys > 0)
                    {
                        double cpu = ((sys - idl) * 100.0) / sys;
                        _lastCpuUsage = Math.Clamp(Math.Round(cpu, 1), 0.0, 100.0);
                    }
                }

                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                _lastCpuSampleTime = now;
            }
            tel.CpuLoadPercent = _lastCpuUsage;
        }
        catch { }

        // 3. Network Traffic (Speed)
        try
        {
            long totalRx = 0, totalTx = 0;
            string bestAdapter = _lastAdapterName;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    try
                    {
                        var stats = ni.GetIPv4Statistics();
                        totalRx += stats.BytesReceived;
                        totalTx += stats.BytesSent;
                        bestAdapter = ni.Name;
                    }
                    catch { }
                }
            }
            _lastAdapterName = bestAdapter;

            var now = DateTime.UtcNow;
            if (_prevRxBytes >= 0 && _prevNetSampleTime != DateTime.MinValue)
            {
                double seconds = (now - _prevNetSampleTime).TotalSeconds;
                if (seconds > 0.4)
                {
                    _lastDownKb = Math.Max(0, (totalRx - _prevRxBytes) / 1024.0 / seconds);
                    _lastUpKb = Math.Max(0, (totalTx - _prevTxBytes) / 1024.0 / seconds);
                    _prevRxBytes = totalRx;
                    _prevTxBytes = totalTx;
                    _prevNetSampleTime = now;
                }
            }
            else
            {
                _prevRxBytes = totalRx;
                _prevTxBytes = totalTx;
                _prevNetSampleTime = now;
            }

            tel.DownloadSpeedKb = _lastDownKb;
            tel.UploadSpeedKb = _lastUpKb;
            tel.ActiveAdapterName = _lastAdapterName;

            string downStr = _lastDownKb >= 1024.0 ? $"{_lastDownKb / 1024.0:F1} MB/s" : $"{_lastDownKb:F0} KB/s";
            string upStr = _lastUpKb >= 1024.0 ? $"{_lastUpKb / 1024.0:F1} MB/s" : $"{_lastUpKb:F0} KB/s";
            tel.NetworkSpeedDisplay = $"↓ {downStr}  ↑ {upStr}";
        }
        catch { }

        // 4. Background Ping
        TriggerBackgroundPing();
        tel.PingMs = _lastPingMs;
        tel.PingDisplay = _lastPingMs >= 0 ? $"{_lastPingMs} ms" : "Chờ mạng";

        return tel;
    }

    private static void TriggerBackgroundPing()
    {
        if (_isPinging) return;
        _isPinging = true;

        Task.Run(async () =>
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 1200);
                if (reply.Status == IPStatus.Success)
                {
                    _lastPingMs = reply.RoundtripTime;
                }
                else
                {
                    _lastPingMs = -1;
                }
            }
            catch
            {
                _lastPingMs = -1;
            }
            finally
            {
                await Task.Delay(1200);
                _isPinging = false;
            }
        });
    }

    public static int GetRamUsagePercent()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                return (int)mem.dwMemoryLoad;
            }
        }
        catch { }
        return 0;
    }

    public static double GetCpuUsagePercent()
    {
        return _lastCpuUsage;
    }
}
