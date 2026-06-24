using System;
using System.Reflection;

namespace LlamaServerLauncher.Models;

public static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            if (plus >= 0)
                informational = informational.Substring(0, plus);
            informational = informational.Trim();
            if (informational.Length > 0)
                return informational;
        }

        var version = asm.GetName().Version;
        return version != null ? $"v{version.Major}.{version.Minor}" : "dev";
    }
}
