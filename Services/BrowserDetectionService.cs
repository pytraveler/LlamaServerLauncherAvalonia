using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using LlamaServerLauncher.Models;

#pragma warning disable CA1416 // Registry access is guarded by RuntimeInformation.IsOSPlatform

namespace LlamaServerLauncher.Services;

/// <summary>
/// Discovers web browsers installed on the current OS so the user can pick one for the
/// "custom browser path" setting instead of typing the executable path by hand.
/// </summary>
public static class BrowserDetectionService
{
    public static List<BrowserInfo> DetectInstalledBrowsers()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return DetectWindows();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return DetectLinux();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return DetectMacOS();
        }
        catch
        {
            // Detection is best-effort; the user can always type a path manually.
        }
        return new List<BrowserInfo>();
    }

    // Windows registers browsers under Clients\StartMenuInternet; each subkey's default
    // value is the friendly name and shell\open\command holds the launch command line.
    private static List<BrowserInfo> DetectWindows()
    {
        var result = new List<BrowserInfo>();
        var seenPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(RegistryKey root, string subPath)
        {
            using var startMenu = root.OpenSubKey(subPath);
            if (startMenu == null) return;

            foreach (var keyName in startMenu.GetSubKeyNames())
            {
                try
                {
                    using var browserKey = startMenu.OpenSubKey(keyName);
                    if (browserKey == null) continue;

                    var displayName = browserKey.GetValue(null) as string;
                    using var cmdKey = browserKey.OpenSubKey(@"shell\open\command");
                    var path = ExtractExePath(cmdKey?.GetValue(null) as string);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    if (!seenPath.Add(path)) continue;

                    result.Add(new BrowserInfo
                    {
                        Name = string.IsNullOrWhiteSpace(displayName) ? keyName : displayName!,
                        Path = path
                    });
                }
                catch
                {
                    // Skip malformed entries.
                }
            }
        }

        Scan(Registry.LocalMachine, @"SOFTWARE\Clients\StartMenuInternet");
        Scan(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet");
        Scan(Registry.CurrentUser, @"SOFTWARE\Clients\StartMenuInternet");
        return result;
    }

    private static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();

        if (command.StartsWith("\""))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command.Substring(1, end - 1) : command.Trim('"');
        }

        // Unquoted: cut right after the first ".exe" (handles paths with spaces).
        var exeIdx = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx >= 0) return command.Substring(0, exeIdx + 4);

        var space = command.IndexOf(' ');
        return space > 0 ? command.Substring(0, space) : command;
    }

    private static List<BrowserInfo> DetectLinux()
    {
        var candidates = new[]
        {
            ("firefox", "Firefox"),
            ("google-chrome", "Google Chrome"),
            ("google-chrome-stable", "Google Chrome"),
            ("chromium", "Chromium"),
            ("chromium-browser", "Chromium"),
            ("brave-browser", "Brave"),
            ("microsoft-edge", "Microsoft Edge"),
            ("microsoft-edge-stable", "Microsoft Edge"),
            ("opera", "Opera"),
            ("vivaldi-stable", "Vivaldi"),
            ("vivaldi", "Vivaldi"),
        };

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<BrowserInfo>();
        var seenName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (binary, name) in candidates)
        {
            if (seenName.Contains(name)) continue;
            foreach (var dir in pathDirs)
            {
                var full = Path.Combine(dir, binary);
                if (File.Exists(full))
                {
                    result.Add(new BrowserInfo { Name = name, Path = full });
                    seenName.Add(name);
                    break;
                }
            }
        }
        return result;
    }

    private static List<BrowserInfo> DetectMacOS()
    {
        var candidates = new[]
        {
            ("Safari.app", "Safari", "Safari"),
            ("Google Chrome.app", "Google Chrome", "Google Chrome"),
            ("Firefox.app", "Firefox", "firefox"),
            ("Microsoft Edge.app", "Microsoft Edge", "Microsoft Edge"),
            ("Brave Browser.app", "Brave", "Brave Browser"),
            ("Opera.app", "Opera", "Opera"),
            ("Vivaldi.app", "Vivaldi", "Vivaldi"),
            ("Chromium.app", "Chromium", "Chromium"),
        };

        var roots = new[]
        {
            "/Applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications")
        };

        var result = new List<BrowserInfo>();
        foreach (var (app, name, exe) in candidates)
        {
            foreach (var root in roots)
            {
                var exePath = Path.Combine(root, app, "Contents", "MacOS", exe);
                if (File.Exists(exePath))
                {
                    result.Add(new BrowserInfo { Name = name, Path = exePath });
                    break;
                }
            }
        }
        return result;
    }
}
