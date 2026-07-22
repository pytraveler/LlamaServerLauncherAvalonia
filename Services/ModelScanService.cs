using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class ModelScanService
{
    public static Task<List<ModelScanEntry>> ScanAsync(string folder, bool recursive, CancellationToken ct = default) =>
        Task.Run(() => Scan(folder, recursive, ct), ct);

    public static List<ModelScanEntry> Scan(string folder, bool recursive, CancellationToken ct = default)
    {
        var result = new List<ModelScanEntry>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return result;

        foreach (var path in EnumerateGgufSafe(folder, recursive, ct))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (ModelScanFormatting.IsNonFirstShard(name)) continue;

            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }

            var info = GgufMetadataService.TryRead(path);

            string relDir = "";
            try
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                relDir = Path.GetRelativePath(folder, dir);
                if (relDir == ".") relDir = "";
                relDir = relDir.Replace('\\', '/');
            }
            catch { }

            result.Add(new ModelScanEntry
            {
                FullPath = path,
                FileName = name,
                RelativeDir = relDir,
                SizeBytes = size,
                Info = info
            });
        }

        result.Sort((a, b) =>
        {
            int c = string.Compare(a.RelativeDir, b.RelativeDir, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static IEnumerable<string> EnumerateGgufSafe(string root, bool recursive, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] files;
            try { files = Directory.GetFiles(dir, "*.gguf"); }
            catch { files = Array.Empty<string>(); }
            foreach (var f in files) yield return f;

            if (!recursive) continue;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { subs = Array.Empty<string>(); }
            foreach (var s in subs) stack.Push(s);
        }
    }
}
