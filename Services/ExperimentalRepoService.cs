using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public class ExperimentalRepoService
{
    private static readonly Regex GitHubUrlRegex = new(
        @"https?://(www\.)?github\.com/(?<author>[^/]+)/(?<repo>[^/]+)/?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly System.Net.Http.HttpClient SharedHttpClient = CreateHttpClient();

    private static System.Net.Http.HttpClient CreateHttpClient()
    {
        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LlamaServerLauncher/1.0");
        return http;
    }

    public static bool TryParseGitHubUrl(string url, out string author, out string repo)
    {
        author = "";
        repo = "";
        if (string.IsNullOrWhiteSpace(url)) return false;
        var match = GitHubUrlRegex.Match(url.Trim());
        if (!match.Success) return false;
        author = match.Groups["author"].Value;
        repo = match.Groups["repo"].Value;
        return !string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(repo);
    }

    public static string BuildApiUrl(string author, string repo)
    {
        return $"https://api.github.com/repos/{author}/{repo}/releases?per_page=15";
    }

    public static string GetDefaultFilterTags()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "windows,mswin,win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux,ubuntu";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macos,osx,apple";
        return "";
    }

    public static List<ExperimentalRepoInfo> GetDefaultRepos()
    {
        return new List<ExperimentalRepoInfo>
        {
            new ExperimentalRepoInfo
            {
                RepoUrl = "https://github.com/pytraveler/llama-cpp-turboquant",
                DisplayName = "TurboQuant",
                FilterTags = GetDefaultFilterTags(),
                Enabled = true
            },
            new ExperimentalRepoInfo
            {
                RepoUrl = "https://github.com/pytraveler/DiffusionGemma-fork",
                DisplayName = "DiffusionGemma",
                FilterTags = GetDefaultFilterTags(),
                Enabled = true
            }
        };
    }

    public static List<ReleaseAsset> FilterAssetsByTags(List<ReleaseAsset> assets, string filterTags)
    {
        if (string.IsNullOrWhiteSpace(filterTags))
            return assets;

        var tags = filterTags.Split(',')
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tags.Count == 0)
            return assets;

        return assets.Where(a =>
        {
            var name = a.Name.ToLowerInvariant();
            return tags.Any(tag => name.Contains(tag));
        }).ToList();
    }

    public async Task<List<ReleaseInfo>> FetchReleasesAsync(ExperimentalRepoInfo repo)
    {
        if (!TryParseGitHubUrl(repo.RepoUrl, out var author, out var repoName))
            return new List<ReleaseInfo>();

        var url = BuildApiUrl(author, repoName);

        await LlamaCppDownloadService.SharedHttpLock.WaitAsync();
        try
        {
            using var resp = await SharedHttpClient.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return ParseReleases(json);
        }
        finally
        {
            LlamaCppDownloadService.SharedHttpLock.Release();
        }
    }

    private static List<ReleaseInfo> ParseReleases(string json)
    {
        var result = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var info = new ReleaseInfo
            {
                Tag = el.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "",
                Name = el.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                // Body intentionally not cached: the experimental UI never displays it.
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
                        DownloadUrl = a.TryGetProperty("browser_download_url", out var dlUrl) ? dlUrl.GetString() ?? "" : ""
                    });
                }
            }
            result.Add(info);
        }
        return result;
    }
}
