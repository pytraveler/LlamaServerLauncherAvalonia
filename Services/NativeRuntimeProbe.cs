using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LlamaServerLauncher.Services;

public static class NativeRuntimeProbe
{
    private const string Missing = "MISSING";

    private static readonly string[] MsvcRuntimeDlls =
    {
        "msvcp140.dll",
        "vcruntime140.dll",
        "vcruntime140_1.dll"
    };

    public static string? DescribeMsvcRuntime(string? executablePath)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var parts = new List<string>();
        var anyMissingInSystem = false;
        var anyLocalCopy = false;

        var systemDir = TryGetSystemDirectory();
        if (!string.IsNullOrEmpty(systemDir))
        {
            var items = new List<string>();
            foreach (var dll in MsvcRuntimeDlls)
            {
                var state = DescribeDll(systemDir, dll);
                if (state == Missing)
                    anyMissingInSystem = true;
                items.Add($"{dll}={state}");
            }
            parts.Add("System32: " + string.Join(", ", items));
        }

        var exeDir = TryGetDirectory(executablePath);
        if (!string.IsNullOrEmpty(exeDir))
        {
            var items = new List<string>();
            foreach (var dll in MsvcRuntimeDlls)
            {
                var state = DescribeDll(exeDir, dll);
                if (state == Missing)
                    continue;

                anyLocalCopy = true;
                items.Add($"{dll}={state}");
            }
            parts.Add("next to the executable: " + (items.Count == 0 ? "none" : string.Join(", ", items)));
        }

        if (parts.Count == 0)
            return null;

        var report = "MSVC runtime: " + string.Join("; ", parts);

        if (anyMissingInSystem)
            report += ". A runtime DLL is missing from System32 - install the latest Visual C++ Redistributable (x64) from Microsoft.";

        if (anyLocalCopy)
            report += ". A copy next to the executable is loaded instead of the System32 one, so an outdated local copy breaks an otherwise healthy system runtime.";

        return report;
    }

    private static string DescribeDll(string directory, string dllName)
    {
        try
        {
            var path = Path.Combine(directory, dllName);
            if (!File.Exists(path))
                return Missing;

            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return string.IsNullOrWhiteSpace(version) ? "unknown version" : version;
        }
        catch (Exception ex)
        {
            return $"unreadable ({ex.GetType().Name})";
        }
    }

    private static string? TryGetSystemDirectory()
    {
        try { return Environment.SystemDirectory; }
        catch { return null; }
    }

    private static string? TryGetDirectory(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            return Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }
}
