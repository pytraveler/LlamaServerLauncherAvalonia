using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.Models;

public sealed record HfRemoteFile
{
    public string Path { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool IsLfs { get; init; }
    public string? Oid { get; init; }

    public string FileName => Path.Split('/').Last();
}

public sealed record HfRepoSummary
{
    public string Id { get; init; } = "";
    public long Downloads { get; init; }
    public long Likes { get; init; }
    public DateTime? LastModified { get; init; }
    public bool IsGated { get; init; }
    public bool IsPrivate { get; init; }

    public string BadgeText => IsPrivate ? "private" : IsGated ? "gated" : "";
    public bool HasBadge => IsPrivate || IsGated;

    public string StatsText
    {
        get
        {
            var parts = new List<string>();
            if (Downloads > 0) parts.Add(HfFormatting.Count(Downloads) + " downloads");
            if (Likes > 0) parts.Add(HfFormatting.Count(Likes) + " likes");
            if (LastModified is DateTime when) parts.Add(when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return string.Join(" - ", parts);
        }
    }
}

public sealed class HfQuantEntry : INotifyPropertyChanged
{
    public string RepoId { get; init; } = "";
    public string Revision { get; init; } = "main";
    public string DisplayName { get; init; } = "";
    public string SubDir { get; init; } = "";
    public IReadOnlyList<HfRemoteFile> Files { get; init; } = Array.Empty<HfRemoteFile>();
    public long TotalBytes { get; init; }
    public string? Quant { get; init; }
    public bool IsProjector { get; init; }

    public bool IsShardSet => Files.Count > 1;
    public int ShardCount => Files.Count;
    public HfRemoteFile PrimaryFile => Files[0];

    public string SizeText => ModelScanFormatting.FormatSize(TotalBytes);
    public bool HasSubDir => SubDir.Length > 0;

    private bool _isLocal;
    private long _partialBytes;
    private VramFit _fit = VramFit.Unknown;
    private long _fitBytes;

    public bool IsLocal => _isLocal;
    public bool HasLocalState => _isLocal || _partialBytes > 0;

    public string LocalText => _isLocal
        ? "on disk"
        : _partialBytes > 0 ? "partial " + ModelScanFormatting.FormatSize(_partialBytes) : "";

    public VramFit Fit => _fit;
    public bool FitsEasily => _fit == VramFit.Fits;
    public bool FitsTight => _fit == VramFit.Tight;
    public bool FitsNot => _fit == VramFit.DoesNotFit;

    public string FitText => _fit == VramFit.Unknown
        ? ""
        : "VRAM " + ModelScanFormatting.FormatSize(_fitBytes);


    public void SetLocal(bool isLocal, long partialBytes)
    {
        if (_isLocal == isLocal && _partialBytes == partialBytes) return;
        _isLocal = isLocal;
        _partialBytes = partialBytes;
        Raise(nameof(IsLocal));
        Raise(nameof(HasLocalState));
        Raise(nameof(LocalText));
    }

    public void SetFit(VramFit fit, long fitBytes)
    {
        if (_fit == fit && _fitBytes == fitBytes) return;
        _fit = fit;
        _fitBytes = fitBytes;
        Raise(nameof(Fit));
        Raise(nameof(FitsEasily));
        Raise(nameof(FitsTight));
        Raise(nameof(FitsNot));
        Raise(nameof(FitText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class HfFormatting
{
    public static string Count(long value)
    {
        if (value >= 1_000_000) return (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (value >= 1_000) return (value / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string Speed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "";
        return ModelScanFormatting.FormatSize((long)bytesPerSecond) + "/s";
    }

    public static string Eta(TimeSpan? eta)
    {
        if (eta is not TimeSpan span || span < TimeSpan.Zero) return "";
        if (span.TotalHours >= 1)
            return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture)
                + span.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
        return span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}

public static class HfApiParser
{
    private static readonly Regex ShardPattern =
        new(@"^(?<stem>.+)-(?<no>\d{5})-of-(?<total>\d{5})\.gguf$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuantPattern =
        new(@"(?<q>(IQ|Q)\d+(_[A-Z0-9]+)*|BF16|F16|F32|MXFP4)(?=\.gguf$|-\d{5}-of-)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NextLink = new(@"<([^>]+)>\s*;\s*rel=""next""", RegexOptions.Compiled);

    public static string? NextPageUrl(string? linkHeader)
    {
        if (string.IsNullOrEmpty(linkHeader)) return null;
        var match = NextLink.Match(linkHeader);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static List<HfRepoSummary> ParseSearch(string? json)
    {
        var found = new List<HfRepoSummary>();
        if (string.IsNullOrWhiteSpace(json)) return found;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return found;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = Text(item, "id") ?? Text(item, "modelId");
                if (string.IsNullOrEmpty(id)) continue;
                found.Add(new HfRepoSummary
                {
                    Id = id,
                    Downloads = Number(item, "downloads"),
                    Likes = Number(item, "likes"),
                    LastModified = Timestamp(item, "lastModified"),
                    IsGated = Gated(item),
                    IsPrivate = item.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True,
                });
            }
        }
        catch (JsonException)
        {
        }
        return found;
    }

    public static List<HfRemoteFile> ParseTree(string? json)
    {
        var found = new List<HfRemoteFile>();
        if (string.IsNullOrWhiteSpace(json)) return found;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return found;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (Text(item, "type") != "file") continue;
                var path = Text(item, "path");
                if (string.IsNullOrEmpty(path)) continue;

                long size = Number(item, "size");
                string? oid = null;
                bool isLfs = false;
                if (item.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
                {
                    isLfs = true;
                    long lfsSize = Number(lfs, "size");
                    if (lfsSize > 0) size = lfsSize;
                    oid = Text(lfs, "oid") ?? Text(lfs, "sha256");
                }

                found.Add(new HfRemoteFile { Path = path, SizeBytes = size, IsLfs = isLfs, Oid = oid });
            }
        }
        catch (JsonException)
        {
        }
        return found;
    }


    public static List<string> ParseRefs(string? json, string current)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var group in new[] { "branches", "tags" })
                {
                    if (!doc.RootElement.TryGetProperty(group, out var list)) continue;
                    if (list.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in list.EnumerateArray())
                    {
                        var name = Text(item, "name");
                        if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        if (names.Count == 0) return new List<string> { current };
        names.Sort((a, b) =>
        {
            bool am = a == "main", bm = b == "main";
            if (am != bm) return am ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });
        if (!names.Contains(current)) names.Add(current);
        return names;
    }
    public static (string Stem, int Index, int Total)? ParseShard(string fileName)
    {
        var match = ShardPattern.Match(fileName);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups["no"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int no))
            return null;
        if (!int.TryParse(match.Groups["total"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int total))
            return null;
        if (total < 1 || total > 999 || no < 1) return null;
        return (match.Groups["stem"].Value, no, total);
    }

    public static string? QuantLabel(string fileName)
    {
        var match = QuantPattern.Match(fileName);
        return match.Success ? match.Groups["q"].Value.ToUpperInvariant() : null;
    }


    private static readonly Regex FolderQuantPattern =
        new(@"^((IQ|Q)\d+(_[A-Z0-9]+)*|BF16|F16|F32|MXFP4)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? QuantFromFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        var segment = folder.Split('/').Last();
        var match = FolderQuantPattern.Match(segment);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }
    public static List<HfQuantEntry> GroupQuants(string repoId, string revision, IReadOnlyList<HfRemoteFile> files)
    {
        var groups = new Dictionary<string, List<HfRemoteFile>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var file in files)
        {
            if (!file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;

            var dir = DirectoryOf(file.Path);
            var shard = ParseShard(file.FileName);
            var key = shard is (string stem, _, _) ? dir.Length + ":" + dir + "/" + stem : file.Path;

            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = new List<HfRemoteFile>();
                groups[key] = bucket;
                order.Add(key);
            }
            bucket.Add(file);
        }

        var result = new List<HfQuantEntry>();
        foreach (var key in order)
        {
            var bucket = groups[key];
            bucket.Sort((a, b) =>
            {
                int ai = ParseShard(a.FileName)?.Index ?? 0;
                int bi = ParseShard(b.FileName)?.Index ?? 0;
                return ai != bi ? ai.CompareTo(bi) : string.CompareOrdinal(a.Path, b.Path);
            });

            var first = bucket[0];
            var shard = ParseShard(first.FileName);
            var name = shard is (string stem, _, int total)
                ? stem + ".gguf (" + total.ToString(CultureInfo.InvariantCulture) + " parts)"
                : first.FileName;

            result.Add(new HfQuantEntry
            {
                RepoId = repoId,
                Revision = revision,
                DisplayName = name,
                SubDir = DirectoryOf(first.Path),
                Files = bucket,
                TotalBytes = bucket.Sum(f => f.SizeBytes),
                Quant = QuantLabel(first.FileName) ?? QuantFromFolder(DirectoryOf(first.Path)),
                IsProjector = first.FileName.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase),
            });
        }

        result.Sort((a, b) =>
        {
            if (a.IsProjector != b.IsProjector) return a.IsProjector ? 1 : -1;
            int dir = string.Compare(a.SubDir, b.SubDir, StringComparison.OrdinalIgnoreCase);
            return dir != 0 ? dir : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static string DirectoryOf(string path)
    {
        int cut = path.LastIndexOf('/');
        return cut < 0 ? "" : path.Substring(0, cut);
    }

    private static bool Gated(JsonElement item)
    {
        if (!item.TryGetProperty("gated", out var gated)) return false;
        return gated.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => gated.GetString() is string s && s.Length > 0
                && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)) return n;
        return 0;
    }

    private static DateTime? Timestamp(JsonElement item, string name)
    {
        var raw = Text(item, name);
        if (raw == null) return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when) ? when : null;
    }
}
