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

    private const int ReleaseCount = 15;

    private static readonly TimeSpan ReleaseCacheLifetime = TimeSpan.FromMinutes(30);

    public GitHubReleaseResult? LastReleaseFetch { get; private set; }

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
        return $"https://api.github.com/repos/{author}/{repo}/releases?per_page={ReleaseCount}";
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

        var result = await GitHubReleaseSource.GetReleasesAsync(
            author, repoName, ReleaseCount, includeBody: false, freshFor: ReleaseCacheLifetime);
        LastReleaseFetch = result;

        if (!result.HasData && result.Error != null)
            throw new InvalidOperationException(result.Error);

        return result.Releases;
    }
}
