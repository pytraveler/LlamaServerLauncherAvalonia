using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LlamaServerLauncher.Services;

public sealed class WindowsProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private IntPtr _handle;
    private bool _disposed;

    private WindowsProcessJob(IntPtr handle)
    {
        _handle = handle;
    }

    public static WindowsProcessJob? TryCreate(LogService? logService = null)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                logService?.Warning($"Job object: creation failed (error {Marshal.GetLastWin32Error()})");
                return null;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

            int length = Marshal.SizeOf(info);
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)length))
                {
                    logService?.Warning($"Job object: setting the limit failed (error {Marshal.GetLastWin32Error()})");
                    CloseHandle(handle);
                    return null;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return new WindowsProcessJob(handle);
        }
        catch (Exception ex)
        {
            logService?.Warning($"Job object: not available ({ex.Message})");
            return null;
        }
    }

    public bool TryAssign(Process process, LogService? logService = null)
    {
        if (_handle == IntPtr.Zero || _disposed)
            return false;

        try
        {
            if (AssignProcessToJobObject(_handle, process.Handle))
                return true;

            logService?.Warning($"Job object: could not take the process (error {Marshal.GetLastWin32Error()})");
            return false;
        }
        catch (Exception ex)
        {
            logService?.Warning($"Job object: could not take the process ({ex.Message})");
            return false;
        }
    }

    public void Terminate()
    {
        if (_handle == IntPtr.Zero || _disposed)
            return;

        try { TerminateJobObject(_handle, 0); } catch { /* the job may already be empty */ }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            try { CloseHandle(_handle); } catch { }
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
