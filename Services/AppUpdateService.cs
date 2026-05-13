using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
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

    private const string RepoApiUrl = "https://api.github.com/repos/pytraveler/LlamaServerLauncherAvalonia/releases";

    public AppUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LlamaServerLauncher/1.0");
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var localHash = ComputeLocalBinaryHash();
            if (localHash == null) return null;

            var url = $"{RepoApiUrl}?per_page=1";
            using var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() == 0) return null;

            var latestRelease = doc.RootElement[0];
            var tag = latestRelease.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            var body = latestRelease.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            var publishedAt = DateTime.MinValue;
            if (latestRelease.TryGetProperty("published_at", out var pubEl))
            {
                var pubStr = pubEl.GetString();
                if (pubStr != null && DateTime.TryParse(pubStr, out var dt))
                    publishedAt = dt;
            }

            if (!latestRelease.TryGetProperty("assets", out var assetsEl)) return null;

            var targetAsset = FindAssetForCurrentOS(assetsEl);
            if (targetAsset == null) return null;

            var remoteHash = ExtractDigest(targetAsset.Value);
            if (remoteHash == null) return null;

            if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                return null;

            var assetEl = targetAsset.Value;
            return new AppUpdateInfo
            {
                Tag = tag,
                PublishedAt = publishedAt,
                Body = body,
                Asset = new ReleaseAsset
                {
                    Name = assetEl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Size = assetEl.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
                    DownloadUrl = assetEl.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : ""
                }
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(ReleaseAsset asset, IProgress<double>? progress, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "LlamaServerLauncher_update_" + Guid.NewGuid().ToString("N")[..8]);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            tempFile += ".exe";

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

    private static JsonElement? FindAssetForCurrentOS(JsonElement assetsEl)
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

        foreach (var asset in assetsEl.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.StartsWith(osPrefix, StringComparison.OrdinalIgnoreCase))
                return asset;
        }
        return null;
    }

    private static string? ExtractDigest(JsonElement asset)
    {
        if (asset.TryGetProperty("digest", out var digestEl))
        {
            var digest = digestEl.GetString() ?? "";
            var prefix = "sha256:";
            if (digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return digest[prefix.Length..];
        }
        return null;
    }
}
