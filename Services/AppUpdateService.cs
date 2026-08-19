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

    public bool UsedWebFallback { get; set; }
    public bool FromStaleCache { get; set; }
    public DateTime FetchedAt { get; set; }
}

public enum AppUpdateVerdict
{
    Newer,
    Rebuilt,
    NotNewer,
    Unknown
}


public class AppUpdateService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };
    private const string RepoOwner = "pytraveler";
    private const string RepoName = "LlamaServerLauncherAvalonia";

    private readonly Action<string>? _log;

    public AppUpdateService(Action<string>? log = null) => _log = log;

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

            var source = result.UsedWebFallback ? "github.com pages" : result.Origin.ToString();
            if (result.Releases.Count == 0)
            {
                _log?.Invoke($"App update check: source={source}, no releases returned"
                    + (string.IsNullOrEmpty(result.Error) ? "" : $" ({result.Error})"));
                return null;
            }

            var latestRelease = result.Releases[0];
            var asset = FindAssetForCurrentOS(latestRelease.Assets);
            if (asset == null)
            {
                _log?.Invoke($"App update check: source={source}, latest={latestRelease.Tag}, no asset for this OS");
                return null;
            }

            var verdict = Decide(latestRelease.Tag, Models.AppInfo.Version, asset.Digest, ComputeLocalBinaryHash);
            var digestNote = string.IsNullOrEmpty(asset.Digest) ? "no digest" : "digest available";
            _log?.Invoke($"App update check: source={source}, latest={latestRelease.Tag}, "
                + $"local={Models.AppInfo.Version}, {digestNote}, verdict={verdict}");

            if (verdict != AppUpdateVerdict.Newer && verdict != AppUpdateVerdict.Rebuilt) return null;

            return new AppUpdateInfo
            {
                Tag = latestRelease.Tag,
                PublishedAt = latestRelease.PublishedAt,
                Body = latestRelease.Body,
                Asset = asset,
                UsedWebFallback = result.UsedWebFallback,
                FromStaleCache = result.IsStale,
                FetchedAt = result.FetchedAt
            };
        }
        catch (Exception ex)
        {
            _log?.Invoke($"App update check failed: {ex.Message}");
            return null;
        }
    }

    public static AppUpdateVerdict Decide(string? remoteTag, string? localVersion, string? assetDigest,
                                          Func<string?> localBinaryHash)
    {
        var remoteOk = GitHubReleaseSource.TryParseVersionTag(remoteTag ?? "", out var remote);
        var localOk = GitHubReleaseSource.TryParseVersionTag(localVersion ?? "", out var local);

        if (remoteOk && localOk)
        {
            if (remote > local) return AppUpdateVerdict.Newer;
            if (remote < local) return AppUpdateVerdict.NotNewer;
            return BinaryDiffers(assetDigest, localBinaryHash) ? AppUpdateVerdict.Rebuilt : AppUpdateVerdict.NotNewer;
        }

        if (BinaryDiffers(assetDigest, localBinaryHash)) return AppUpdateVerdict.Rebuilt;
        return string.IsNullOrEmpty(assetDigest) ? AppUpdateVerdict.Unknown : AppUpdateVerdict.NotNewer;
    }

    private static bool BinaryDiffers(string? assetDigest, Func<string?> localBinaryHash)
    {
        if (string.IsNullOrEmpty(assetDigest)) return false;
        var local = localBinaryHash();
        if (string.IsNullOrEmpty(local)) return false;
        return !string.Equals(local, assetDigest, StringComparison.OrdinalIgnoreCase);
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
                        progress?.Report((double)bytesRead / totalBytes * 100);
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
