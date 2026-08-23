using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Benchmarking;

namespace LlamaServerLauncher.Services.Benchmarking;

public class BenchmarkStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly DataPathResolver _dataPathResolver;
    private readonly LogService? _log;

    public BenchmarkStorageService(DataPathResolver dataPathResolver, LogService? log = null)
    {
        _dataPathResolver = dataPathResolver;
        _log = log;
    }

    public string BenchmarksRoot => Path.Combine(_dataPathResolver.ResolveDataPath(), "benchmarks");

    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "profile";
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        safe = safe.Trim().Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "profile" : safe;
    }

    public async Task<string> SaveRunAsync(BenchmarkRun run, string log, string? rawMetrics, string reportMd, string? promptRunMd = null)
    {
        var root = BenchmarksRoot;
        var profileDir = Path.Combine(root, SafeName(run.ProfileName));
        Directory.CreateDirectory(profileDir);

        var baseId = run.CreatedAt.ToString("yyyy-MM-dd_HH-mm-ss");
        var id = baseId;
        int suffix = 1;
        while (Directory.Exists(Path.Combine(profileDir, id)))
            id = $"{baseId}_{suffix++}";

        run.Id = id;
        var runDir = Path.Combine(profileDir, id);
        Directory.CreateDirectory(runDir);
        Directory.CreateDirectory(Path.Combine(runDir, "pinned"));
        run.DirectoryPath = runDir;

        await File.WriteAllTextAsync(Path.Combine(runDir, "run.json"), JsonSerializer.Serialize(run, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(runDir, "command.txt"), run.Command ?? string.Empty);
        await File.WriteAllTextAsync(Path.Combine(runDir, "server.log"), log ?? string.Empty);
        if (rawMetrics != null)
            await File.WriteAllTextAsync(Path.Combine(runDir, "metrics.txt"), rawMetrics);
        await File.WriteAllTextAsync(Path.Combine(runDir, "report.md"), reportMd ?? string.Empty);
        if (!string.IsNullOrEmpty(promptRunMd))
            await File.WriteAllTextAsync(Path.Combine(runDir, "prompt-run.md"), promptRunMd);

        return runDir;
    }

    public List<BenchmarkRun> LoadAllRuns()
    {
        var runs = new List<BenchmarkRun>();
        var root = BenchmarksRoot;
        if (!Directory.Exists(root))
            return runs;

        foreach (var profileDir in Directory.GetDirectories(root))
        {
            foreach (var runDir in Directory.GetDirectories(profileDir))
            {
                var run = LoadRun(runDir);
                if (run != null)
                    runs.Add(run);
            }
        }

        return runs.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public BenchmarkRun? LoadRun(string runDir)
    {
        try
        {
            var file = Path.Combine(runDir, "run.json");
            if (!File.Exists(file))
                return null;
            var run = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(file), JsonOptions);
            if (run == null)
                return null;
            run.DirectoryPath = runDir;
            if (string.IsNullOrEmpty(run.Id))
                run.Id = Path.GetFileName(runDir);
            return run;
        }
        catch (Exception ex)
        {
            _log?.Error($"Failed to load benchmark run '{runDir}': {ex.Message}");
            return null;
        }
    }

    public string GetRunDir(string profileName, string runId) =>
        Path.Combine(BenchmarksRoot, SafeName(profileName), runId);

    public void DeleteRun(BenchmarkRun run)
    {
        if (string.IsNullOrEmpty(run.DirectoryPath) || !Directory.Exists(run.DirectoryPath))
            return;
        try
        {
            Directory.Delete(run.DirectoryPath, recursive: true);
        }
        catch (Exception ex)
        {
            _log?.Error($"Failed to delete benchmark run '{run.DirectoryPath}': {ex.Message}");
        }
    }

    public string GetPinnedDir(BenchmarkRun run) =>
        Path.Combine(run.DirectoryPath, "pinned");

    public async Task<int> CopyIntoPinnedAsync(BenchmarkRun run, IReadOnlyList<string> paths)
    {
        if (string.IsNullOrEmpty(run.DirectoryPath) || !Directory.Exists(run.DirectoryPath))
            throw new DirectoryNotFoundException($"Benchmark folder not found: {run.DirectoryPath}");

        var pinnedDir = GetPinnedDir(run);
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(pinnedDir);
            int copied = 0;
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Copy(path, Path.Combine(pinnedDir, Path.GetFileName(path)), overwrite: true);
                        copied++;
                    }
                    else if (Directory.Exists(path))
                    {
                        // Guard against copying the pinned folder (or an ancestor) into itself.
                        if (Path.GetFullPath(pinnedDir).StartsWith(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                            continue;
                        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                        if (string.IsNullOrEmpty(name))
                            name = "folder";
                        CopyDirectory(path, Path.Combine(pinnedDir, name));
                        copied++;
                    }
                }
                catch (Exception ex)
                {
                    _log?.Error($"Failed to pin '{path}': {ex.Message}");
                }
            }
            return copied;
        });
    }

    public async Task ExportRunToZipAsync(BenchmarkRun run, string targetZip)
    {
        if (string.IsNullOrEmpty(run.DirectoryPath) || !Directory.Exists(run.DirectoryPath))
            throw new DirectoryNotFoundException($"Benchmark folder not found: {run.DirectoryPath}");

        if (File.Exists(targetZip))
            File.Delete(targetZip);

        await Task.Run(() => ZipFile.CreateFromDirectory(run.DirectoryPath, targetZip));
    }

    public async Task ExportRunsToZipAsync(IReadOnlyList<BenchmarkRun> runs, string comparisonMd, string targetZip)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "llsl-bench-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            foreach (var run in runs)
            {
                if (string.IsNullOrEmpty(run.DirectoryPath) || !Directory.Exists(run.DirectoryPath))
                    continue;
                var dest = Path.Combine(tempDir, SafeName(run.ProfileName) + "__" + run.Id);
                CopyDirectory(run.DirectoryPath, dest);
            }
            await File.WriteAllTextAsync(Path.Combine(tempDir, "comparison.md"), comparisonMd ?? string.Empty);

            if (File.Exists(targetZip))
                File.Delete(targetZip);
            await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, targetZip));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    public List<BenchmarkComparisonSet> LoadComparisons()
    {
        try
        {
            var file = Path.Combine(BenchmarksRoot, "comparisons.json");
            if (!File.Exists(file))
                return new List<BenchmarkComparisonSet>();
            return JsonSerializer.Deserialize<List<BenchmarkComparisonSet>>(File.ReadAllText(file), JsonOptions)
                   ?? new List<BenchmarkComparisonSet>();
        }
        catch (Exception ex)
        {
            _log?.Error($"Failed to load benchmark comparisons: {ex.Message}");
            return new List<BenchmarkComparisonSet>();
        }
    }

    public async Task SaveComparisonsAsync(List<BenchmarkComparisonSet> comparisons)
    {
        Directory.CreateDirectory(BenchmarksRoot);
        var file = Path.Combine(BenchmarksRoot, "comparisons.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(comparisons, JsonOptions));
    }

    public List<string>? LoadMetricSelection()
    {
        try
        {
            var file = Path.Combine(BenchmarksRoot, "metric-selection.json");
            if (!File.Exists(file))
                return null;
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file), JsonOptions);
        }
        catch (Exception ex)
        {
            _log?.Error($"Failed to load benchmark metric selection: {ex.Message}");
            return null;
        }
    }

    public void SaveMetricSelection(IReadOnlyList<string> keys)
    {
        try
        {
            Directory.CreateDirectory(BenchmarksRoot);
            var file = Path.Combine(BenchmarksRoot, "metric-selection.json");
            File.WriteAllText(file, JsonSerializer.Serialize(keys, JsonOptions));
        }
        catch (Exception ex)
        {
            _log?.Error($"Failed to save benchmark metric selection: {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
