using System;
using LlamaServerLauncher.Services;

public static class ProcessExitCodeInfoTests
{
    public static void Run(Harness h)
    {
        h.Section("ProcessExitCodeInfo");

        h.Check("clean exit is not a crash", !ProcessExitCodeInfo.IsCrash(0), "ok");
        h.Check("error exit 1 is not a crash", !ProcessExitCodeInfo.IsCrash(1), "ok");
        h.Check("exit 1 stays a bare number", ProcessExitCodeInfo.Describe(1) == "1", ProcessExitCodeInfo.Describe(1));

        if (OperatingSystem.IsWindows())
        {
            var accessViolation = unchecked((int)0xC0000005);
            var described = ProcessExitCodeInfo.Describe(accessViolation);
            h.Check("access violation keeps the raw code", described.Contains("-1073741819"), described);
            h.Check("access violation shows hex", described.Contains("0xC0000005"), described);
            h.Check("access violation is named", described.Contains("STATUS_ACCESS_VIOLATION"), described);
            h.Check("access violation is a crash", ProcessExitCodeInfo.IsCrash(accessViolation), "ok");
            h.Check("access violation has a hint",
                ProcessExitCodeInfo.GetCrashHint(accessViolation)?.Contains("not an out-of-memory") == true,
                ProcessExitCodeInfo.GetCrashHint(accessViolation) ?? "null");
            h.Check("hint names the msvc runtime",
                ProcessExitCodeInfo.GetCrashHint(accessViolation)?.Contains("Visual C++ Redistributable") == true,
                "ok");

            var dllInitFailed = unchecked((int)0xC0000142);
            h.Check("dll init failure is a crash", ProcessExitCodeInfo.IsCrash(dllInitFailed), "ok");
            h.Check("dll init failure has a hint", !string.IsNullOrEmpty(ProcessExitCodeInfo.GetCrashHint(dllInitFailed)), "ok");

            var unknownStatus = unchecked((int)0xC0000123);
            h.Check("unknown NTSTATUS is still a crash", ProcessExitCodeInfo.IsCrash(unknownStatus), "ok");
            h.Check("unknown NTSTATUS stays a bare number",
                ProcessExitCodeInfo.Describe(unknownStatus) == unknownStatus.ToString(),
                ProcessExitCodeInfo.Describe(unknownStatus));

            var ctrlC = unchecked((int)0xC000013A);
            h.Check("ctrl+c exit is not treated as a crash", !ProcessExitCodeInfo.IsCrash(ctrlC), "ok");
            h.Check("exit 139 is not a crash on windows", !ProcessExitCodeInfo.IsCrash(139), "ok");
        }
        else
        {
            h.Check("SIGSEGV is a crash", ProcessExitCodeInfo.IsCrash(139), "ok");
            h.Check("SIGSEGV is named", ProcessExitCodeInfo.Describe(139).Contains("SIGSEGV"), ProcessExitCodeInfo.Describe(139));
            h.Check("SIGSEGV has a hint", !string.IsNullOrEmpty(ProcessExitCodeInfo.GetCrashHint(139)), "ok");
            h.Check("exit 42 is not a crash", !ProcessExitCodeInfo.IsCrash(42), "ok");
        }
    }
}
