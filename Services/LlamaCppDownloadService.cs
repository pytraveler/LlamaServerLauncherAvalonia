using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class ReleaseInfo
{
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Body { get; set; } = "";
    public List<ReleaseAsset> Assets { get; set; } = new();

    public override string ToString() => $"{Tag} — {PublishedAt:yyyy-MM-dd HH:mm}";
}

public class ReleaseAsset
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string DownloadUrl { get; set; } = "";
    public double SizeMB => Size / (1024.0 * 1024.0);
}

public class LlamaCppDownloadService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    internal static readonly SemaphoreSlim SharedHttpLock = new(1, 1);

    private const string RepoApiUrl = "https://api.github.com/repos/ggml-org/llama.cpp/releases";
    private readonly string _installDir;

    static LlamaCppDownloadService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LlamaServerLauncher/1.0");
    }

    public LlamaCppDownloadService(string? appDataPath = null)
    {
        var basePath = appDataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
        _installDir = Path.GetFullPath(Path.Combine(basePath, "llama.cpp"));
    }

    public string InstallDirectory => _installDir;

    public async Task<List<ReleaseInfo>> GetLatestReleasesAsync(int count = 10)
    {
        var url = $"{RepoApiUrl}?per_page={count}";
        await SharedHttpLock.WaitAsync();
        try
        {
            using var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            return ParseReleases(json);
        }
        finally
        {
                SharedHttpLock.Release();
        }
    }

    public async Task<ReleaseInfo?> GetReleaseByTagAsync(string tag)
    {
        var url = $"{RepoApiUrl}/tags/{Uri.EscapeDataString(tag)}";
        await SharedHttpLock.WaitAsync();
        try
        {
            using var resp = await _http.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            return ParseSingleRelease(json);
        }
        finally
        {
                SharedHttpLock.Release();
        }
    }

    public List<ReleaseAsset> FilterAssetsForCurrentOS(List<ReleaseAsset> assets)
    {
        return assets.Where(a =>
        {
            var name = a.Name.ToLowerInvariant();
            if (name.StartsWith("cudart-")) return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return name.Contains("-win-") && name.EndsWith(".zip");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return name.Contains("-ubuntu-") && name.EndsWith(".tar.gz");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return name.Contains("-macos-") && name.EndsWith(".tar.gz");
            return false;
        }).ToList();
    }

    public ReleaseAsset? FindMatchingCudaDllAsset(ReleaseAsset selectedAsset, List<ReleaseAsset>? allAssets)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        if (allAssets == null)
            return null;

        var selName = selectedAsset.Name.ToLowerInvariant();
        if (!selName.Contains("-cuda-"))
            return null;

        // Extract cuda version+arch suffix, e.g. "cuda-12.4-x64" from "llama-b9129-bin-win-cuda-12.4-x64.zip"
        var cudaStart = selName.IndexOf("-cuda-") + 1; // points to 'c' in "cuda-..."
        var rest = selName[cudaStart..];
        var dotIdx = rest.IndexOf('.');
        var cudaSuffix = dotIdx > 0 ? rest[..dotIdx] : rest;

        return allAssets.FirstOrDefault(a =>
        {
            var aName = a.Name.ToLowerInvariant();
            return aName.StartsWith("cudart-") && aName.Contains(cudaSuffix);
        });
    }

    public async Task DownloadAndExtractAsync(ReleaseAsset asset, IProgress<double>? progress, CancellationToken ct, ReleaseAsset? cudaDllAsset = null)
    {
        await DownloadAndExtractAsync(asset, _installDir, progress, ct, cudaDllAsset);
    }

    public async Task DownloadAndExtractAsync(ReleaseAsset asset, string targetDirectory, IProgress<double>? progress, CancellationToken ct, ReleaseAsset? cudaDllAsset = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), asset.Name);
        string? tempDllFile = null;

        try
        {
            var mainProgressMax = cudaDllAsset != null ? 45.0 : 100.0;
            await DownloadFileAsync(asset.DownloadUrl, tempFile, asset.Size, 0, mainProgressMax, progress, ct);

            if (cudaDllAsset != null)
            {
                tempDllFile = Path.Combine(Path.GetTempPath(), cudaDllAsset.Name);
                await DownloadFileAsync(cudaDllAsset.DownloadUrl, tempDllFile, cudaDllAsset.Size, 45.0, 90.0, progress, ct);
            }

            progress?.Report(-1);

            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }
            Directory.CreateDirectory(targetDirectory);

            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractZipWithFlatten(tempFile, targetDirectory);
            }
            else if (asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTarGzWithFlatten(tempFile, targetDirectory);
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var exePath = GetLlamaServerPath(targetDirectory);
                    if (exePath != null && File.Exists(exePath))
                    {
                        var mode = File.GetUnixFileMode(exePath);
                        File.SetUnixFileMode(exePath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    }
                }
                catch { }
            }

            if (tempDllFile != null && File.Exists(tempDllFile))
            {
                ExtractDllsFromZip(tempDllFile, targetDirectory);
            }
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            try { if (tempDllFile != null && File.Exists(tempDllFile)) File.Delete(tempDllFile); } catch { }
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, long expectedSize, double progressStart, double progressEnd, IProgress<double>? progress, CancellationToken ct)
    {
        await SharedHttpLock.WaitAsync(ct);
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
            var buffer = new byte[81920];
            long bytesRead = 0;

            using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct);
                    bytesRead += read;
                    if (totalBytes > 0)
                        progress?.Report(progressStart + (double)bytesRead / totalBytes * (progressEnd - progressStart));
                }
            }
        }
        finally
        {
                SharedHttpLock.Release();
        }
    }

    public string? GetDefaultLlamaServerPath()
    {
        return GetLlamaServerPath(_installDir);
    }

    public string? GetLlamaServerPath(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "llama-server.exe"
            : "llama-server";

        var path = Path.Combine(directory, exeName);
        return File.Exists(path) ? path : null;
    }

    public static string GetUniqueSubfolderPath(string baseDirectory, string name)
    {
        var sanitized = SanitizeFolderName(name);
        var target = Path.Combine(baseDirectory, sanitized);
        if (!Directory.Exists(target) && !File.Exists(target))
            return target;

        int index = 1;
        while (true)
        {
            var candidate = Path.Combine(baseDirectory, $"{sanitized}({index})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
            index++;
        }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (Array.IndexOf(invalid, c) >= 0 || c == '.' || c == ' ')
                sb.Append('_');
            else
                sb.Append(c);
        }
        var result = sb.ToString();
        if (string.IsNullOrEmpty(result))
            result = "llama.cpp";
        return result;
    }

    public bool IsLlamaCppInstalled()
    {
        return Directory.Exists(_installDir) &&
               Directory.EnumerateFileSystemEntries(_installDir).Any();
    }

    public async Task<string?> GetLatestReleaseTagAsync()
    {
        try
        {
            var releases = await GetLatestReleasesAsync(1);
            return releases.Count > 0 ? releases[0].Tag : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetLocalVersionTagAsync(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;

            var psi = new ProcessStartInfo(exePath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = stdout + "\n" + stderr;
            var match = Regex.Match(output, @"version\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && long.TryParse(match.Groups[1].Value, out var number) && number > 0)
            {
                // "version: 0" means the binary has no embedded build number 
                return $"b{number}";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsInPath(string directory)
    {
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var entries = userPath.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var entry in entries)
        {
            var normalizedEntry = entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedEntry, normalizedDir, comparison))
                return true;
        }
        return false;
    }

    public async Task AddToPathIfNeededAsync(string directory)
    {
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var entries = userPath.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var entry in entries)
        {
            var normalizedEntry = entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedEntry, normalizedDir, comparison))
                return;
        }

        var newPath = string.IsNullOrEmpty(userPath) ? normalizedDir : $"{userPath}{separator}{normalizedDir}";
        Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                BroadcastSettingChange();
            }
            catch { }
        }
    }

    private void BroadcastSettingChange()
    {
        var result = NativeMethods.SendMessageTimeout(
            NativeMethods.HWND_BROADCAST,
            NativeMethods.WM_SETTINGCHANGE,
            IntPtr.Zero,
            "Environment",
            NativeMethods.SMTO_ABORTIFHUNG,
            5000,
            out _);
    }

    private static void ExtractDllsFromZip(string archivePath, string destDir)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            entry.ExtractToFile(Path.Combine(destDir, entry.Name), overwrite: true);
        }
    }

    private static void ExtractZipWithFlatten(string archivePath, string destDir)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.ToList();

        var serverEntry = entries.FirstOrDefault(e =>
            e.Name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) ||
            e.Name.Equals("llama-server", StringComparison.OrdinalIgnoreCase));

        if (serverEntry == null)
        {
            archive.ExtractToDirectory(destDir, overwriteFiles: true);
            return;
        }

        var serverFullDir = Path.GetDirectoryName(serverEntry.FullName);
        if (string.IsNullOrEmpty(serverFullDir))
        {
            archive.ExtractToDirectory(destDir, overwriteFiles: true);
            return;
        }

        string prefix = serverFullDir.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/"))
                continue;

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (relativePath.StartsWith(prefix))
                relativePath = relativePath[prefix.Length..];
            else
                continue;

            var destPath = Path.Combine(destDir, relativePath);
            var destEntryDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destEntryDir))
                Directory.CreateDirectory(destEntryDir);

            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    private static void ExtractTarGzWithFlatten(string archivePath, string destDir)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new System.Formats.Tar.TarReader(gzipStream);

        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"llama_cpp_extract_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractDir);

        try
        {
            System.Formats.Tar.TarEntry? entry;
            while ((entry = tarReader.GetNextEntry()) != null)
            {
                if (entry.EntryType != System.Formats.Tar.TarEntryType.RegularFile) continue;
                var destPath = Path.Combine(tempExtractDir, entry.Name);
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                entry.ExtractToFile(destPath, overwrite: true);
            }

            var serverFile = Directory.GetFiles(tempExtractDir, "llama-server", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (serverFile == null)
            {
                CopyDirectoryContents(tempExtractDir, destDir);
                return;
            }

            var serverDir = Path.GetDirectoryName(serverFile);
            if (serverDir == null || serverDir == tempExtractDir)
            {
                CopyDirectoryContents(tempExtractDir, destDir);
                return;
            }

            var relativePrefix = serverDir[tempExtractDir.Length..].TrimStart(Path.DirectorySeparatorChar, '/');

            foreach (var file in Directory.GetFiles(serverDir, "*", SearchOption.AllDirectories))
            {
                var relPath = file[tempExtractDir.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
                if (relPath.StartsWith(relativePrefix))
                    relPath = relPath[relativePrefix.Length..].TrimStart(Path.DirectorySeparatorChar, '/');

                var targetPath = Path.Combine(destDir, relPath);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);
                File.Copy(file, targetPath, overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(serverDir, "*", SearchOption.AllDirectories))
            {
                var relDir = dir[tempExtractDir.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
                if (relDir.StartsWith(relativePrefix))
                    relDir = relDir[relativePrefix.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
                var targetDir = Path.Combine(destDir, relDir);
                Directory.CreateDirectory(targetDir);
            }
        }
        finally
        {
            try { Directory.Delete(tempExtractDir, true); } catch { }
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relPath = file[sourceDir.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
            var targetPath = Path.Combine(destDir, relPath);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static List<ReleaseInfo> ParseReleases(string json)
    {
        var result = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
            result.Add(ParseReleaseElement(el));
        return result;
    }

    private static ReleaseInfo? ParseSingleRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseReleaseElement(doc.RootElement);
    }

    private static ReleaseInfo ParseReleaseElement(JsonElement el)
    {
        var info = new ReleaseInfo
        {
            Tag = el.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "",
            Name = el.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Body = el.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
        };

        if (el.TryGetProperty("published_at", out var pubAt))
        {
            var pubStr = pubAt.GetString();
            if (pubStr != null && DateTime.TryParse(pubStr, out var dt))
                info.PublishedAt = dt;
        }

        if (el.TryGetProperty("assets", out var assetsEl))
        {
            foreach (var a in assetsEl.EnumerateArray())
            {
                info.Assets.Add(new ReleaseAsset
                {
                    Name = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "",
                    Size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
                    DownloadUrl = a.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? "" : ""
                });
            }
        }

        return info;
    }

    private static class NativeMethods
    {
        public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
        public const int WM_SETTINGCHANGE = 0x001A;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    }
}
