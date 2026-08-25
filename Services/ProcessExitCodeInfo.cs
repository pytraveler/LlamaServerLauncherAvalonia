using System;
using System.Globalization;

namespace LlamaServerLauncher.Services;

public static class ProcessExitCodeInfo
{
    private const uint StatusControlCExit = 0xC000013A;

    public static string Describe(int exitCode)
    {
        if (OperatingSystem.IsWindows())
        {
            var status = GetNtStatusName(exitCode);
            return status == null
                ? exitCode.ToString(CultureInfo.InvariantCulture)
                : $"{exitCode} (0x{(uint)exitCode:X8}, {status})";
        }

        var signal = GetSignalName(exitCode);
        return signal == null
            ? exitCode.ToString(CultureInfo.InvariantCulture)
            : $"{exitCode} (killed by {signal})";
    }

    public static bool IsCrash(int exitCode)
    {
        if (OperatingSystem.IsWindows())
        {
            var status = (uint)exitCode;
            if (status == StatusControlCExit)
                return false;

            return (status & 0xF0000000u) == 0xC0000000u;
        }

        return GetSignalName(exitCode) != null;
    }

    public static string? GetCrashHint(int exitCode)
    {
        if (!OperatingSystem.IsWindows())
            return IsCrash(exitCode)
                ? "The process was killed by a fatal signal before it could exit on its own."
                : null;

        switch ((uint)exitCode)
        {
            case 0xC0000005: // STATUS_ACCESS_VIOLATION
            case 0xC000007B: // STATUS_INVALID_IMAGE_FORMAT
            case 0xC0000135: // STATUS_DLL_NOT_FOUND
            case 0xC0000138: // STATUS_ORDINAL_NOT_FOUND
            case 0xC0000139: // STATUS_ENTRYPOINT_NOT_FOUND
            case 0xC0000142: // STATUS_DLL_INIT_FAILED
            case 0xC0000221: // STATUS_IMAGE_CHECKSUM_MISMATCH
                return "This is a native crash, not an out-of-memory condition. Usual causes: a missing or outdated Visual C++ Redistributable (msvcp140.dll, vcruntime140.dll, vcruntime140_1.dll), a broken or mismatched GPU driver / CUDA runtime, a partially extracted or corrupted llama.cpp build, or foreign ggml/cudart/cublas DLLs picked up from PATH. Check Event Viewer -> Windows Logs -> Application for the faulting module name, then reinstall the latest Visual C++ Redistributable (x64) and re-download llama.cpp.";
            case 0xC0000017: // STATUS_NO_MEMORY
                return "The process ran out of memory or address space.";
            case 0xC00000FD: // STATUS_STACK_OVERFLOW
            case 0xC0000374: // STATUS_HEAP_CORRUPTION
            case 0xC0000409: // STATUS_STACK_BUFFER_OVERRUN
                return "This is a native crash inside the process, not an out-of-memory condition.";
            default:
                return null;
        }
    }

    private static string? GetNtStatusName(int exitCode)
    {
        return (uint)exitCode switch
        {
            0xC0000005 => "STATUS_ACCESS_VIOLATION",
            0xC0000017 => "STATUS_NO_MEMORY",
            0xC000001D => "STATUS_ILLEGAL_INSTRUCTION",
            0xC0000022 => "STATUS_ACCESS_DENIED",
            0xC000007B => "STATUS_INVALID_IMAGE_FORMAT",
            0xC0000094 => "STATUS_INTEGER_DIVIDE_BY_ZERO",
            0xC00000FD => "STATUS_STACK_OVERFLOW",
            0xC0000135 => "STATUS_DLL_NOT_FOUND",
            0xC0000138 => "STATUS_ORDINAL_NOT_FOUND",
            0xC0000139 => "STATUS_ENTRYPOINT_NOT_FOUND",
            0xC000013A => "STATUS_CONTROL_C_EXIT",
            0xC0000142 => "STATUS_DLL_INIT_FAILED",
            0xC0000221 => "STATUS_IMAGE_CHECKSUM_MISMATCH",
            0xC0000374 => "STATUS_HEAP_CORRUPTION",
            0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN",
            _ => null
        };
    }

    private static string? GetSignalName(int exitCode)
    {
        return exitCode switch
        {
            132 => "SIGILL",
            134 => "SIGABRT",
            135 => "SIGBUS",
            136 => "SIGFPE",
            137 => "SIGKILL",
            139 => "SIGSEGV",
            _ => null
        };
    }
}
