using System;
using System.IO;
using LlamaServerLauncher.Services;

public static class NativeRuntimeProbeTests
{
    public static void Run(Harness h)
    {
        h.Section("NativeRuntimeProbe");

        var report = NativeRuntimeProbe.DescribeMsvcRuntime(null);

        if (!OperatingSystem.IsWindows())
        {
            h.Check("nothing is reported outside windows", report == null, report ?? "null");
            return;
        }

        h.Check("report exists without an executable path", !string.IsNullOrEmpty(report), report ?? "null");
        h.Check("all three runtime dlls are named",
            report!.Contains("msvcp140.dll") && report.Contains("vcruntime140.dll") && report.Contains("vcruntime140_1.dll"),
            report);
        h.Check("system32 is inspected", report.Contains("System32:"), report);
        h.Check("no executable path means no local section", !report.Contains("next to the executable"), report);

        var withExe = NativeRuntimeProbe.DescribeMsvcRuntime(Path.Combine(Path.GetTempPath(), "no-such-llama-server.exe"));
        h.Check("missing executable does not throw", !string.IsNullOrEmpty(withExe), withExe ?? "null");
        h.Check("the executable directory is inspected for local copies",
            withExe!.Contains("next to the executable:"), withExe);

        var garbage = NativeRuntimeProbe.DescribeMsvcRuntime("::not a path::");
        h.Check("unparsable path does not throw", !string.IsNullOrEmpty(garbage), garbage ?? "null");
    }
}
