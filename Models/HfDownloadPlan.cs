using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LlamaServerLauncher.Models;

public enum HfResumeDecision
{
    StartFresh,
    Resume,
    AlreadyComplete,
    Conflict,
}

public sealed class HfPartState
{
    public string Url { get; set; } = "";
    public string RepoId { get; set; } = "";
    public string Revision { get; set; } = "main";
    public string Path { get; set; } = "";
    public long ExpectedSize { get; set; }
    public string? Oid { get; set; }
    public DateTime StartedUtc { get; set; }
}

public static class HfDownloadPlan
{
    public const long MinFreeMarginBytes = 256L * 1024 * 1024;
    private const int MaxTargetPathLength = 240;


    public readonly record struct HfLocalState(bool Complete, long HaveBytes)
    {
        public bool Partial => !Complete && HaveBytes > 0;
        public bool Any => Complete || HaveBytes > 0;
    }

    public static long FileLengthOrMissing(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1;
        }
        catch
        {
            return -1;
        }
    }

    public static HfLocalState Inspect(string directory, IReadOnlyList<HfRemoteFile> files,
        Func<string, long>? lengthOf = null)
    {
        var measure = lengthOf ?? FileLengthOrMissing;
        if (string.IsNullOrWhiteSpace(directory) || files.Count == 0)
            return new HfLocalState(false, 0);

        long have = 0;
        bool complete = true;

        foreach (var file in files)
        {
            var target = Path.Combine(directory, file.FileName);
            long targetLength = measure(target);

            if (file.SizeBytes > 0 && targetLength == file.SizeBytes)
            {
                have += file.SizeBytes;
                continue;
            }

            complete = false;

            long partLength = measure(PartPathFor(target));
            if (partLength > 0)
                have += file.SizeBytes > 0 ? Math.Min(partLength, file.SizeBytes) : partLength;
        }

        return new HfLocalState(complete, have);
    }
    public static string PartPathFor(string finalPath) => finalPath + ".part";

    public static string StatePathFor(string finalPath) => finalPath + ".part.json";

    public static HfResumeDecision DecideResume(long partLength, HfPartState? state, long expectedSize,
        string? expectedOid, out long offset)
    {
        offset = 0;
        if (partLength <= 0) return HfResumeDecision.StartFresh;

        if (state == null) return HfResumeDecision.StartFresh;

        if (expectedSize > 0 && state.ExpectedSize > 0 && state.ExpectedSize != expectedSize)
            return HfResumeDecision.Conflict;

        if (!string.IsNullOrEmpty(expectedOid) && !string.IsNullOrEmpty(state.Oid)
            && !string.Equals(expectedOid, state.Oid, StringComparison.OrdinalIgnoreCase))
            return HfResumeDecision.Conflict;

        if (expectedSize > 0)
        {
            if (partLength == expectedSize) return HfResumeDecision.AlreadyComplete;
            if (partLength > expectedSize) return HfResumeDecision.Conflict;
        }

        offset = partLength;
        return HfResumeDecision.Resume;
    }

    public static (long Start, long End, long Total)? ParseContentRange(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var value = header.Trim();
        if (value.StartsWith("bytes", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(5).Trim();

        int slash = value.LastIndexOf('/');
        if (slash < 0) return null;

        var span = value.Substring(0, slash).Trim();
        var totalText = value.Substring(slash + 1).Trim();
        if (!long.TryParse(totalText, NumberStyles.None, CultureInfo.InvariantCulture, out long total))
            return null;

        int dash = span.IndexOf('-');
        if (dash < 0) return null;
        if (!long.TryParse(span.Substring(0, dash).Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out long start))
            return null;
        if (!long.TryParse(span.Substring(dash + 1).Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out long end))
            return null;

        return (start, end, total);
    }

    public static bool RangeHonored(int statusCode, string? contentRange, long requestedOffset)
    {
        if (requestedOffset <= 0) return true;
        if (statusCode != 206) return false;
        var range = ParseContentRange(contentRange);
        return range == null || range.Value.Start == requestedOffset;
    }

    public static long? TotalFromResponse(string? contentRange, long? contentLength, long haveBytes)
    {
        var range = ParseContentRange(contentRange);
        if (range != null) return range.Value.Total;
        if (contentLength is long length && length >= 0) return haveBytes + length;
        return null;
    }

    public static bool ShouldForwardAuth(Uri from, Uri to) =>
        string.Equals(from.Host, to.Host, StringComparison.OrdinalIgnoreCase)
        && !(from.Scheme == "https" && to.Scheme == "http");

    public static bool TrySafeDestination(string baseDirectory, string relativePath, out string result)
    {
        result = "";
        var cleaned = (relativePath ?? "").Replace('\\', '/').Trim('/');
        var parts = cleaned.Split('/').Where(p => p.Length > 0 && p != ".").ToList();
        if (parts.Count == 0 || parts.Any(p => p == "..")) return false;
        if (parts.Any(p => p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;

        var combined = Path.Combine(new[] { baseDirectory }.Concat(parts).ToArray());
        string full, root;
        try
        {
            full = Path.GetFullPath(combined);
            root = Path.GetFullPath(baseDirectory);
        }
        catch
        {
            return false;
        }

        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return false;

        result = full;
        return true;
    }

    public static string RepoFolderName(string repoId)
    {
        var name = (repoId ?? "").Replace('/', '_').Replace('\\', '_').Trim();
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        name = name.Trim('.', ' ');
        return name.Length == 0 ? "huggingface" : name;
    }

    public static string ResolveTargetPath(string directory, string fileName, Func<string, bool> exists)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        int n = 2;
        while (exists(candidate) || exists(PartPathFor(candidate)))
        {
            candidate = Path.Combine(directory,
                stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + ext);
            n++;
            if (n > 999) break;
        }
        return candidate;
    }

    public static bool IsPathTooLong(string path) => path.Length > MaxTargetPathLength;

    public static long RequiredFreeBytes(long neededBytes) =>
        neededBytes <= 0 ? 0 : neededBytes + Math.Max(MinFreeMarginBytes, neededBytes / 100);

    public static bool HasEnoughFreeSpace(string directory, long neededBytes, out long availableBytes)
    {
        availableBytes = -1;
        if (neededBytes <= 0) return true;
        try
        {
            var probe = directory;
            while (probe.Length > 0 && !Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent) || parent == probe) break;
                probe = parent;
            }
            var root = Path.GetPathRoot(Path.GetFullPath(probe));
            if (string.IsNullOrEmpty(root)) return true;
            availableBytes = new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return true;
        }
        return availableBytes >= RequiredFreeBytes(neededBytes);
    }
}
