using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LlamaServerLauncher.Services;

public enum GitHubReleaseOrigin
{
    None,
    Api,
    ApiNotModified,
    Web,
    CacheFresh,
    Cache
}

public class GitHubReleaseResult
{
    public List<ReleaseInfo> Releases { get; set; } = new();
    public GitHubReleaseOrigin Origin { get; set; } = GitHubReleaseOrigin.None;
    public DateTime FetchedAt { get; set; }
    public string? Error { get; set; }

    public bool UsedWebFallback => Origin == GitHubReleaseOrigin.Web;
    public bool IsStale => Origin == GitHubReleaseOrigin.Cache;
    public bool HasData => Releases.Count > 0;
}

public static class GitHubReleaseSource
{
    private const string ApiBase = "https://api.github.com";
    private const string WebBase = "https://github.com";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static readonly HttpClient _httpNoRedirect =
        new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly Dictionary<string, string> _memoryCache = new();
    private static readonly object _cacheLock = new();
    private static readonly JsonSerializerOptions _cacheJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Regex AssetHrefRegex = new(
        @"href=""(?<href>/[^""]+/releases/download/[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AssetSizeRegex = new(
        @">\s*(?<value>[\d.,]+)\s*(?<unit>Bytes|KB|MB|GB|TB)\s*<",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VersionPrefixRegex = new(@"^\d+(\.\d+){0,3}", RegexOptions.Compiled);

    private static readonly Regex RelativeTimeRegex = new(
        @"<relative-time[^>]*\bdatetime=""(?<value>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string? _cacheDirectory;
    private static int _rateRemaining = -1;
    private static DateTime _rateResetUtc = DateTime.MinValue;

    static GitHubReleaseSource()
    {
        var userAgent = $"LlamaServerLauncher/{Models.AppInfo.Version}";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _httpNoRedirect.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public static void ConfigureCacheDirectory(string dataPath)
    {
        try
        {
            var dir = Path.Combine(dataPath, "cache", "github");
            Directory.CreateDirectory(dir);
            _cacheDirectory = dir;
        }
        catch
        {
            _cacheDirectory = null;
        }
    }

    public static bool ApiQuotaExhausted => _rateRemaining == 0 && DateTime.UtcNow < _rateResetUtc;

    public static int ApiQuotaRemaining => _rateRemaining;

    public static DateTime? ApiQuotaResetLocal =>
        _rateResetUtc == DateTime.MinValue ? null : _rateResetUtc.ToLocalTime();

    public static async Task<GitHubReleaseResult> GetReleasesAsync(
        string owner, string repo, int count, bool includeBody = true,
        TimeSpan? freshFor = null, bool forceRefresh = false, CancellationToken ct = default)
    {
        var key = $"{owner}~{repo}~list{count}{(includeBody ? "" : "~nobody")}";
        var apiUrl = $"{ApiBase}/repos/{owner}/{repo}/releases?per_page={count}";

        return await FetchAsync(
            key,
            apiUrl,
            json => Take(ParseApiReleaseArray(json), count, includeBody),
            async () => Take(await FetchReleasesFromWebAsync(owner, repo, count, ct), count, includeBody),
            freshFor,
            forceRefresh,
            ct);
    }

    public static async Task<GitHubReleaseResult> GetReleaseByTagAsync(
        string owner, string repo, string tag,
        TimeSpan? freshFor = null, bool forceRefresh = false, CancellationToken ct = default)
    {
        var key = $"{owner}~{repo}~tag~{tag}";
        var apiUrl = $"{ApiBase}/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";

        return await FetchAsync(
            key,
            apiUrl,
            json =>
            {
                using var doc = JsonDocument.Parse(json);
                return new List<ReleaseInfo> { ParseApiRelease(doc.RootElement) };
            },
            async () => await FetchSingleReleaseFromWebAsync(owner, repo, tag, ct),
            freshFor,
            forceRefresh,
            ct);
    }

    private static List<ReleaseInfo> Take(List<ReleaseInfo> releases, int count, bool includeBody)
    {
        if (releases.Count > count)
            releases = releases.Take(count).ToList();
        if (!includeBody)
        {
            foreach (var r in releases)
                r.Body = "";
        }
        return releases;
    }

    private static async Task<GitHubReleaseResult> FetchAsync(
        string cacheKey,
        string apiUrl,
        Func<string, List<ReleaseInfo>> parseApi,
        Func<Task<List<ReleaseInfo>>> webFallback,
        TimeSpan? freshFor,
        bool forceRefresh,
        CancellationToken ct)
    {
        var cached = LoadCache(cacheKey);
        string? error = null;

        if (!forceRefresh && freshFor != null && cached != null && cached.Releases.Count > 0 &&
            DateTime.Now - cached.FetchedAt < freshFor.Value)
        {
            return new GitHubReleaseResult
            {
                Releases = cached.Releases,
                Origin = GitHubReleaseOrigin.CacheFresh,
                FetchedAt = cached.FetchedAt
            };
        }

        if (ApiQuotaExhausted)
        {
            error = "GitHub API quota exhausted";
        }
        else
        {
            try
            {
                var (status, body, etag) = await SendApiRequestAsync(apiUrl, cached?.ETag, ct);

                if (status == HttpStatusCode.NotModified && cached != null)
                {
                    return new GitHubReleaseResult
                    {
                        Releases = cached.Releases,
                        Origin = GitHubReleaseOrigin.ApiNotModified,
                        FetchedAt = cached.FetchedAt
                    };
                }

                if (status == HttpStatusCode.OK && body != null)
                {
                    var releases = parseApi(body);
                    var now = DateTime.Now;
                    SaveCache(cacheKey, new CacheEntry { ETag = etag, FetchedAt = now, Releases = releases });
                    return new GitHubReleaseResult
                    {
                        Releases = releases,
                        Origin = GitHubReleaseOrigin.Api,
                        FetchedAt = now
                    };
                }

                error = $"GitHub API returned {(int)status}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var releases = await webFallback();
            if (releases.Count > 0)
            {
                var now = DateTime.Now;
                SaveCache(cacheKey, new CacheEntry { ETag = null, FetchedAt = now, Releases = releases });
                return new GitHubReleaseResult
                {
                    Releases = releases,
                    Origin = GitHubReleaseOrigin.Web,
                    FetchedAt = now,
                    Error = error
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (cached != null)
        {
            return new GitHubReleaseResult
            {
                Releases = cached.Releases,
                Origin = GitHubReleaseOrigin.Cache,
                FetchedAt = cached.FetchedAt,
                Error = error
            };
        }

        return new GitHubReleaseResult { Origin = GitHubReleaseOrigin.None, Error = error };
    }

    private static async Task<(HttpStatusCode Status, string? Body, string? ETag)> SendApiRequestAsync(
        string url, string? etag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var token = ResolveToken();
        if (!string.IsNullOrEmpty(token))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        await LlamaCppDownloadService.SharedHttpLock.WaitAsync(ct);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            CaptureRateLimit(response);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return (response.StatusCode, null, etag);
            if (!response.IsSuccessStatusCode)
                return (response.StatusCode, null, null);

            var body = await response.Content.ReadAsStringAsync(ct);
            return (response.StatusCode, body, response.Headers.ETag?.ToString());
        }
        finally
        {
            LlamaCppDownloadService.SharedHttpLock.Release();
        }
    }

    private static void CaptureRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues) &&
            int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
        {
            _rateRemaining = remaining;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out var epochSeconds))
        {
            _rateResetUtc = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
        }
    }

    private static string? ResolveToken()
    {
        foreach (var name in new[] { "LLAMA_LAUNCHER_GITHUB_TOKEN", "GITHUB_TOKEN", "GH_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static async Task<string> GetWebStringAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml");

        await LlamaCppDownloadService.SharedHttpLock.WaitAsync(ct);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        finally
        {
            LlamaCppDownloadService.SharedHttpLock.Release();
        }
    }

    private static async Task<List<ReleaseInfo>> FetchReleasesFromWebAsync(
        string owner, string repo, int count, CancellationToken ct)
    {
        var atom = await GetWebStringAsync($"{WebBase}/{owner}/{repo}/releases.atom", ct);
        var releases = ParseAtomFeed(atom);

        await PromoteLatestReleaseAsync(owner, repo, releases, ct);

        if (releases.Count > count)
            releases = releases.Take(count).ToList();

        foreach (var release in releases)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(release.Tag)) continue;
            try
            {
                var (assets, uploadedAt) = await FetchAssetsFromWebAsync(owner, repo, release.Tag, ct);
                release.Assets.AddRange(assets);
                if (release.PublishedAt == default && uploadedAt != null)
                    release.PublishedAt = uploadedAt.Value;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return releases;
    }

    private static async Task PromoteLatestReleaseAsync(
        string owner, string repo, List<ReleaseInfo> releases, CancellationToken ct)
    {
        string? latestTag;
        try
        {
            latestTag = await ResolveLatestTagAsync(owner, repo, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(latestTag)) return;
        if (string.Equals(releases.FirstOrDefault()?.Tag, latestTag, StringComparison.OrdinalIgnoreCase)) return;

        var existing = releases.FirstOrDefault(r => string.Equals(r.Tag, latestTag, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            releases.Remove(existing);

        releases.Insert(0, existing ?? new ReleaseInfo { Tag = latestTag, Name = latestTag });
    }

    private static async Task<string?> ResolveLatestTagAsync(string owner, string repo, CancellationToken ct)
    {
        await LlamaCppDownloadService.SharedHttpLock.WaitAsync(ct);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, $"{WebBase}/{owner}/{repo}/releases/latest");
            using var response = await _httpNoRedirect.SendAsync(request, ct);
            var location = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location)) return null;
            var tag = ExtractTagFromReleaseUrl(location);
            return string.IsNullOrEmpty(tag) ? null : tag;
        }
        finally
        {
            LlamaCppDownloadService.SharedHttpLock.Release();
        }
    }

    private static async Task<List<ReleaseInfo>> FetchSingleReleaseFromWebAsync(
        string owner, string repo, string tag, CancellationToken ct)
    {
        var (assets, uploadedAt) = await FetchAssetsFromWebAsync(owner, repo, tag, ct);
        if (assets.Count == 0)
            return new List<ReleaseInfo>();

        var release = new ReleaseInfo { Tag = tag, Name = tag, Assets = assets };
        if (uploadedAt != null)
            release.PublishedAt = uploadedAt.Value;

        try
        {
            var atom = await GetWebStringAsync($"{WebBase}/{owner}/{repo}/releases.atom", ct);
            var match = ParseAtomFeed(atom).FirstOrDefault(r =>
                string.Equals(r.Tag, tag, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                release.Name = match.Name;
                release.Body = match.Body;
                release.PublishedAt = match.PublishedAt;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return new List<ReleaseInfo> { release };
    }

    private static async Task<(List<ReleaseAsset> Assets, DateTime? UploadedAt)> FetchAssetsFromWebAsync(
        string owner, string repo, string tag, CancellationToken ct)
    {
        var url = $"{WebBase}/{owner}/{repo}/releases/expanded_assets/{Uri.EscapeDataString(tag)}";
        var html = await GetWebStringAsync(url, ct);
        return (ParseAssetsFragment(html), ParseLatestTimestamp(html));
    }

    internal static DateTime? ParseLatestTimestamp(string html)
    {
        DateTime? newest = null;
        foreach (Match match in RelativeTimeRegex.Matches(html))
        {
            if (!DateTime.TryParse(match.Groups["value"].Value, out var dt)) continue;
            if (newest == null || dt > newest.Value)
                newest = dt;
        }
        return newest;
    }

    internal static List<ReleaseInfo> ParseAtomFeed(string xml)
    {
        var result = new List<ReleaseInfo>();
        XNamespace ns = "http://www.w3.org/2005/Atom";
        var doc = XDocument.Parse(xml);
        if (doc.Root == null) return result;

        foreach (var entry in doc.Root.Elements(ns + "entry"))
        {
            var href = entry.Elements(ns + "link")
                .FirstOrDefault(l => (string?)l.Attribute("rel") == "alternate")
                ?.Attribute("href")?.Value ?? "";

            var tag = ExtractTagFromReleaseUrl(href);
            if (string.IsNullOrEmpty(tag)) continue;

            var info = new ReleaseInfo
            {
                Tag = tag,
                Name = entry.Element(ns + "title")?.Value?.Trim() ?? tag,
                Body = HtmlToPlainText(entry.Element(ns + "content")?.Value ?? "")
            };

            var updated = entry.Element(ns + "updated")?.Value;
            if (!string.IsNullOrEmpty(updated) && DateTime.TryParse(updated, out var dt))
                info.PublishedAt = dt;

            result.Add(info);
        }

        return result;
    }

    private static string ExtractTagFromReleaseUrl(string url)
    {
        const string marker = "/releases/tag/";
        var index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return "";
        var raw = url[(index + marker.Length)..].Trim('/');
        if (raw.Length == 0) return "";
        try { return Uri.UnescapeDataString(raw); } catch { return raw; }
    }

    internal static List<ReleaseAsset> ParseAssetsFragment(string html)
    {
        var result = new List<ReleaseAsset>();
        var matches = AssetHrefRegex.Matches(html);

        for (int i = 0; i < matches.Count; i++)
        {
            var href = matches[i].Groups["href"].Value;
            var name = href[(href.LastIndexOf('/') + 1)..];
            if (name.Length == 0) continue;
            try { name = Uri.UnescapeDataString(name); } catch { }

            var segmentStart = matches[i].Index;
            var segmentEnd = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            var sizeMatch = AssetSizeRegex.Match(html[segmentStart..segmentEnd]);

            result.Add(new ReleaseAsset
            {
                Name = name,
                Size = sizeMatch.Success
                    ? ParseDisplaySize(sizeMatch.Groups["value"].Value, sizeMatch.Groups["unit"].Value)
                    : 0,
                DownloadUrl = WebBase + href
            });
        }

        return result;
    }

    private static long ParseDisplaySize(string value, string unit)
    {
        if (!double.TryParse(value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return 0;

        long multiplier = unit.ToUpperInvariant() switch
        {
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            _ => 1L
        };

        return (long)(number * multiplier);
    }

    internal static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(p|div|li|ul|ol|h\d|tr|pre)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static List<ReleaseInfo> ParseApiReleaseArray(string json)
    {
        var result = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
            result.Add(ParseApiRelease(element));
        return result;
    }

    private static ReleaseInfo ParseApiRelease(JsonElement element)
    {
        var info = new ReleaseInfo
        {
            Tag = element.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "",
            Name = element.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Body = element.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""
        };

        if (element.TryGetProperty("published_at", out var publishedAt))
        {
            var raw = publishedAt.GetString();
            if (raw != null && DateTime.TryParse(raw, out var dt))
                info.PublishedAt = dt;
        }

        if (element.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                info.Assets.Add(new ReleaseAsset
                {
                    Name = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "",
                    Size = asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    DownloadUrl = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? "" : "",
                    Digest = ExtractDigest(asset)
                });
            }
        }

        return info;
    }

    private static string? ExtractDigest(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var digestElement))
            return null;

        var digest = digestElement.GetString() ?? "";
        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..]
            : null;
    }

    public static bool TryParseVersionTag(string text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim().TrimStart('v', 'V');
        var match = VersionPrefixRegex.Match(trimmed);
        if (!match.Success) return false;

        var value = match.Value;
        if (!value.Contains('.')) value += ".0";

        return Version.TryParse(value, out version!);
    }

    private class CacheEntry
    {
        public string? ETag { get; set; }
        public DateTime FetchedAt { get; set; }
        public List<ReleaseInfo> Releases { get; set; } = new();
    }

    private static CacheEntry? LoadCache(string key)
    {
        string? json;
        lock (_cacheLock)
        {
            if (!_memoryCache.TryGetValue(key, out json))
            {
                json = ReadCacheFile(key);
                if (json != null)
                    _memoryCache[key] = json;
            }
        }

        if (json == null) return null;

        try
        {
            var entry = JsonSerializer.Deserialize<CacheEntry>(json, _cacheJsonOptions);
            return entry?.Releases.Count > 0 || entry?.ETag != null ? entry : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(string key, CacheEntry entry)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(entry);
        }
        catch
        {
            return;
        }

        lock (_cacheLock)
        {
            _memoryCache[key] = json;
        }

        var path = GetCacheFilePath(key);
        if (path == null) return;

        try
        {
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
        }
    }

    private static string? ReadCacheFile(string key)
    {
        var path = GetCacheFilePath(key);
        if (path == null || !File.Exists(path)) return null;
        try { return File.ReadAllText(path); } catch { return null; }
    }

    private static string? GetCacheFilePath(string key)
    {
        var dir = _cacheDirectory;
        if (dir == null) return null;

        var sb = new System.Text.StringBuilder(key.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in key)
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c == '~' ? '_' : c);

        return Path.Combine(dir, sb.ToString() + ".json");
    }
}
