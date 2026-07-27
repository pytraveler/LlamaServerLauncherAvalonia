using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class AppUpdateInfo
{
    public string Tag { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Body { get; set; } = "";
    public ReleaseAsset Asset { get; set; } = new();
}

public class AppUpdateService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };
    private const string RepoOwner = "pytraveler";
    private const string RepoName = "LlamaServerLauncherAvalonia";

    static AppUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"LlamaServerLauncher/{Models.AppInfo.Version}");
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(TimeSpan? freshFor = null, bool forceRefresh = false)
    {
        try
        {
            var result = await GitHubReleaseSource.GetReleasesAsync(
                RepoOwner, RepoName, 1, freshFor: freshFor, forceRefresh: forceRefresh);
            if (result.Releases.Count == 0) return null;

            var latestRelease = result.Releases[0];
            var asset = FindAssetForCurrentOS(latestRelease.Assets);
            if (asset == null) return null;

            if (!IsUpdateAvailable(latestRelease.Tag, asset)) return null;

            return new AppUpdateInfo
            {
                Tag = latestRelease.Tag,
                PublishedAt = latestRelease.PublishedAt,
                Body = latestRelease.Body,
                Asset = asset
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUpdateAvailable(string tag, ReleaseAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.Digest))
        {
            var localHash = ComputeLocalBinaryHash();
            if (localHash == null) return false;
            return !string.Equals(localHash, asset.Digest, StringComparison.OrdinalIgnoreCase);
        }

        return GitHubReleaseSource.TryParseVersionTag(tag, out var remote) &&
               GitHubReleaseSource.TryParseVersionTag(Models.AppInfo.Version, out var local) &&
               remote > local;
    }

    public async Task<string> DownloadUpdateAsync(ReleaseAsset asset, IProgress<double>? progress, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "LlamaServerLauncher_update_" + Guid.NewGuid().ToString("N")[..8]);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            tempFile += ".exe";

        await LlamaCppDownloadService.SharedHttpLock.WaitAsync(ct);
        try
        {
            using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            var buffer = new byte[81920];
            long bytesRead = 0;

            using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct);
                    bytesRead += read;
                    if (totalBytes > 0)
                        progress?.Report((double)bytesRead / totalBytes);
                }
            }
        }
        finally
        {
            LlamaCppDownloadService.SharedHttpLock.Release();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var chmod = Process.Start("chmod", $"+x \"{tempFile}\"");
                chmod?.WaitForExit(5000);
            }
            catch { }
        }

        return tempFile;
    }

    public void PerformUpdateAndRestart(string downloadedFile)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.GetCommandLineArgs()[0];
        var pid = Environment.ProcessId;
        var scriptFile = Path.Combine(Path.GetTempPath(), "llama_launcher_update_" + Guid.NewGuid().ToString("N")[..8]);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptFile += ".bat";
            var script = $"@echo off\r\n"
                + $":wait\r\n"
                + $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n"
                + $"if %errorlevel%==0 (\r\n"
                + $"    timeout /t 1 /nobreak >nul\r\n"
                + $"    goto wait\r\n"
                + $")\r\n"
                + $"copy /y \"{downloadedFile}\" \"{currentExe}\"\r\n"
                + $"if %errorlevel%==0 (\r\n"
                + $"    start \"\" \"{currentExe}\"\r\n"
                + $")\r\n"
                + $"del \"{downloadedFile}\" 2>nul\r\n"
                + $"del \"%~f0\" 2>nul\r\n";
            File.WriteAllText(scriptFile, script);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptFile}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        else
        {
            scriptFile += ".sh";
            var escapedDownloaded = downloadedFile.Replace("'", "'\\''");
            var escapedExe = currentExe.Replace("'", "'\\''");
            var script = $"#!/bin/sh\n"
                + $"while kill -0 {pid} 2>/dev/null; do sleep 1; done\n"
                + $"cp -f '{escapedDownloaded}' '{escapedExe}'\n"
                + $"chmod +x '{escapedExe}'\n"
                + $"'{escapedExe}'\n"
                + $"rm -f '{escapedDownloaded}'\n"
                + $"rm -f \"$0\"\n";
            File.WriteAllText(scriptFile, script);
            try
            {
                var chmod = Process.Start("chmod", $"+x \"{scriptFile}\"");
                chmod?.WaitForExit(3000);
            }
            catch { }
            Process.Start(new ProcessStartInfo("/bin/sh", scriptFile)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        Environment.Exit(0);
    }

    private static string? ComputeLocalBinaryHash()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Environment.GetCommandLineArgs()[0];
            if (!File.Exists(exePath)) return null;

            using var stream = File.OpenRead(exePath);
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static ReleaseAsset? FindAssetForCurrentOS(List<ReleaseAsset> assets)
    {
        string osPrefix;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            osPrefix = "LlamaServerLauncher_win_";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            osPrefix = "LlamaServerLauncher_osx_";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            osPrefix = "LlamaServerLauncher_linux_";
        else
            return null;

        var archSuffix = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };

        // Try exact match with architecture first
        if (archSuffix != null)
        {
            var exactPrefix = osPrefix + archSuffix;
            var exact = assets.FirstOrDefault(a => a.Name.StartsWith(exactPrefix, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        // Fallback: return first asset matching OS prefix without architecture
        return assets.FirstOrDefault(a => a.Name.StartsWith(osPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
