using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LlamaServerLauncher.Services;

public static class SystemMetrics
{
    public static bool CpuRamSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static (double Percent, double UsedGb, double TotalGb)? ReadRam()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ReadRamWindows();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return ReadRamLinux();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return ReadRamMac();
        }
        catch { }
        return null;
    }

    public static (ulong Idle, ulong Total)? ReadCpuTimes()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ReadCpuWindows();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return ReadCpuLinux();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return ReadCpuMac();
        }
        catch { }
        return null;
    }

    private static (double, double, double)? ReadRamWindows()
    {
        var mem = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(mem)) return null;
        const double gib = 1024.0 * 1024.0 * 1024.0;
        var totalGb = mem.ullTotalPhys / gib;
        var usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / gib;
        return (mem.dwMemoryLoad, usedGb, totalGb);
    }

    private static (ulong, ulong)? ReadCpuWindows()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var i = ToUInt64(idle);
        var total = ToUInt64(kernel) + ToUInt64(user); // kernel time already includes idle
        return (i, total);
    }

    private static (double, double, double)? ReadRamLinux()
    {
        ulong total = 0, avail = 0;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = ParseMeminfoKb(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) avail = ParseMeminfoKb(line);
            if (total != 0 && avail != 0) break;
        }
        if (total == 0) return null;
        const double gib = 1024.0 * 1024.0; // kB -> GiB
        var used = total - avail;
        return ((double)used / total * 100.0, used / gib, total / gib);
    }

    private static (ulong, ulong)? ReadCpuLinux()
    {
        var first = File.ReadLines("/proc/stat").FirstOrDefault();
        if (first == null || !first.StartsWith("cpu ", StringComparison.Ordinal)) return null;
        var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ulong total = 0, idle = 0;
        for (var i = 1; i < parts.Length; i++)
        {
            if (!ulong.TryParse(parts[i], out var v)) continue;
            total += v;
            if (i == 4 || i == 5) idle += v; // idle + iowait
        }
        return total == 0 ? null : (idle, total);
    }

    private static (ulong, ulong)? ReadCpuMac()
    {
        var info = new uint[4];
        uint count = 4;
        if (host_statistics(mach_host_self(), HostCpuLoadInfo, info, ref count) != 0) return null;
        ulong user = info[0], system = info[1], idle = info[2], nice = info[3];
        var total = user + system + idle + nice;
        return total == 0 ? null : (idle, total);
    }

    private static (double, double, double)? ReadRamMac()
    {
        var total = SysctlUInt64("hw.memsize");
        if (total == 0) return null;
        if (host_page_size(mach_host_self(), out var pageSizePtr) != 0) return null;
        var pageSize = (ulong)pageSizePtr;
        if (pageSize == 0) return null;

        var info = new uint[64];
        uint count = 38; // HOST_VM_INFO64_COUNT
        if (host_statistics64(mach_host_self(), HostVmInfo64, info, ref count) != 0) return null;

        ulong active = info[1], wire = info[3], compressor = info[32];
        var usedBytes = (active + wire + compressor) * pageSize;
        if (usedBytes > total) usedBytes = total;

        const double gib = 1024.0 * 1024.0 * 1024.0;
        return ((double)usedBytes / total * 100.0, usedBytes / gib, total / gib);
    }

    private static ulong SysctlUInt64(string name)
    {
        ulong value = 0;
        var len = (UIntPtr)sizeof(ulong);
        return sysctlbyname(name, ref value, ref len, IntPtr.Zero, UIntPtr.Zero) == 0 ? value : 0;
    }

    private static ulong ParseMeminfoKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && ulong.TryParse(parts[1], out var v) ? v : 0;
    }

    private static ulong ToUInt64(FileTime ft) => ((ulong)ft.High << 32) | ft.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime lpIdleTime, out FileTime lpKernelTime, out FileTime lpUserTime);

    private const int HostCpuLoadInfo = 3;
    private const int HostVmInfo64 = 4;

    [DllImport("libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics(uint host, int flavor, uint[] info, ref uint count);

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics64(uint host, int flavor, uint[] info, ref uint count);

    [DllImport("libSystem.dylib")]
    private static extern int host_page_size(uint host, out UIntPtr pageSize);

    [DllImport("libSystem.dylib", CharSet = CharSet.Ansi)]
    private static extern int sysctlbyname(string name, ref ulong oldp, ref UIntPtr oldlenp, IntPtr newp, UIntPtr newlen);
}
