using System;
using System.Linq;
using LlamaServerLauncher.Services;

public static class GitHubReleaseSourceTests
{
    private const string AtomFeed = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"" xml:lang=""en-US"">
  <id>tag:github.com,2008:https://github.com/ggml-org/llama.cpp/releases</id>
  <title>Release notes from llama.cpp</title>
  <updated>2026-07-27T13:21:37Z</updated>
  <entry>
    <id>tag:github.com,2008:Repository/612354784/b10152</id>
    <updated>2026-07-27T14:15:17Z</updated>
    <link rel=""alternate"" type=""text/html"" href=""https://github.com/ggml-org/llama.cpp/releases/tag/b10152""/>
    <title>b10152</title>
    <content type=""html"">&lt;p&gt;fit : count nextn blocks&lt;/p&gt;&lt;ul&gt;&lt;li&gt;Ubuntu x64 &amp;amp; arm64&lt;/li&gt;&lt;li&gt;macOS&lt;/li&gt;&lt;/ul&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:Repository/612354784/b10151</id>
    <updated>2026-07-27T13:21:37Z</updated>
    <link rel=""alternate"" type=""text/html"" href=""https://github.com/ggml-org/llama.cpp/releases/tag/b10151""/>
    <title>b10151</title>
    <content type=""html"">&lt;p&gt;previous build&lt;/p&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:Repository/1/no-release-link</id>
    <updated>2026-07-27T10:00:00Z</updated>
    <link rel=""alternate"" type=""text/html"" href=""https://github.com/ggml-org/llama.cpp/commits/master""/>
    <title>not a release</title>
  </entry>
</feed>";

    private const string AssetsFragment = @"<div>
  <li>
    <a href=""/ggml-org/llama.cpp/releases/download/b10152/cudart-llama-bin-win-cuda-12.4-x64.zip"" rel=""nofollow"" class=""Truncate"">
      <span class=""Truncate-text"">cudart-llama-bin-win-cuda-12.4-x64.zip</span>
    </a>
    <span class=""color-fg-muted text-right"">373 MB</span>
    <relative-time datetime=""2026-07-27T14:10:00Z"" class=""no-wrap"">2026-07-27</relative-time>
  </li>
  <li>
    <a href=""/ggml-org/llama.cpp/releases/download/b10152/llama-b10152-bin-macos-arm64.tar.gz"" rel=""nofollow"" class=""Truncate"">
      <span class=""Truncate-text"">llama-b10152-bin-macos-arm64.tar.gz</span>
    </a>
    <span class=""color-fg-muted text-right"">10.4 MB</span>
    <relative-time datetime=""2026-07-27T14:15:17Z"" class=""no-wrap"">2026-07-27</relative-time>
  </li>
  <li>
    <a href=""/ggml-org/llama.cpp/releases/download/b10152/llama-b10152-bin-win-cuda-12.8-.pascal%2Bvolta.zip"" rel=""nofollow"" class=""Truncate"">
      <span class=""Truncate-text"">encoded name</span>
    </a>
    <span class=""color-fg-muted text-right"">512 KB</span>
  </li>
  <li>
    <a href=""/ggml-org/llama.cpp/archive/refs/tags/b10152.zip"" rel=""nofollow"" class=""Truncate"">
      <span class=""Truncate-text"">Source code</span>
    </a>
    <span class=""color-fg-muted text-right"">1.2 MB</span>
  </li>
</div>";

    public static void Run(Harness h)
    {
        h.Section("GitHubReleaseSource - Atom feed");

        var releases = GitHubReleaseSource.ParseAtomFeed(AtomFeed);

        h.Check("entry count", releases.Count == 2,
            $"expected 2 release entries (non-release link skipped), got {releases.Count}");

        var first = releases[0];
        h.Check("tag from alternate link", first.Tag == "b10152", $"tag={first.Tag}");
        h.Check("feed order preserved", releases[1].Tag == "b10151", $"second={releases[1].Tag}");
        h.Check("published date", first.PublishedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") == "2026-07-27 14:15",
            $"published={first.PublishedAt.ToUniversalTime():yyyy-MM-dd HH:mm}");
        h.Check("body de-tagged", !first.Body.Contains('<') && first.Body.Contains("fit : count nextn blocks"),
            $"body={first.Body.Replace("\n", "\\n")}");
        h.Check("body entities decoded", first.Body.Contains("Ubuntu x64 & arm64"),
            $"body={first.Body.Replace("\n", "\\n")}");
        h.Check("list items marked", first.Body.Contains("- macOS"),
            $"body={first.Body.Replace("\n", "\\n")}");

        h.Section("GitHubReleaseSource - assets fragment");

        var assets = GitHubReleaseSource.ParseAssetsFragment(AssetsFragment);

        h.Check("source archives excluded", assets.Count == 3 && assets.All(a => !a.Name.StartsWith("b10152")),
            $"count={assets.Count} names={string.Join(",", assets.Select(a => a.Name))}");
        h.Check("asset name", assets[0].Name == "cudart-llama-bin-win-cuda-12.4-x64.zip", $"name={assets[0].Name}");
        h.Check("absolute download url",
            assets[0].DownloadUrl == "https://github.com/ggml-org/llama.cpp/releases/download/b10152/cudart-llama-bin-win-cuda-12.4-x64.zip",
            $"url={assets[0].DownloadUrl}");
        h.Check("size MB parsed", Math.Abs(assets[0].SizeMB - 373.0) < 0.01, $"sizeMB={assets[0].SizeMB:F2}");
        h.Check("fractional size", Math.Abs(assets[1].SizeMB - 10.4) < 0.01, $"sizeMB={assets[1].SizeMB:F2}");
        h.Check("KB size", assets[2].Size == 512 * 1024, $"size={assets[2].Size}");
        h.Check("size not stolen from next asset", assets[1].Size != assets[0].Size,
            $"first={assets[0].Size} second={assets[1].Size}");
        h.Check("percent-encoded name decoded", assets[2].Name == "llama-b10152-bin-win-cuda-12.8-.pascal+volta.zip",
            $"name={assets[2].Name}");
        h.Check("web assets carry no digest", assets.All(a => a.Digest == null), "digest must stay null off the API path");

        var uploadedAt = GitHubReleaseSource.ParseLatestTimestamp(AssetsFragment);
        h.Check("newest upload timestamp",
            uploadedAt != null && uploadedAt.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") == "2026-07-27 14:15",
            $"uploadedAt={uploadedAt?.ToUniversalTime():yyyy-MM-dd HH:mm}");

        h.Check("empty fragment is safe", GitHubReleaseSource.ParseAssetsFragment("<div></div>").Count == 0,
            "no assets, no throw");

        h.Section("GitHubReleaseSource - version tags");

        h.Check("newer version wins",
            GitHubReleaseSource.TryParseVersionTag("v1.6", out var newer) &&
            GitHubReleaseSource.TryParseVersionTag("v1.5", out var current) && newer > current,
            "v1.6 > v1.5");
        h.Check("same version is not an update",
            GitHubReleaseSource.TryParseVersionTag("v1.5", out var same) &&
            GitHubReleaseSource.TryParseVersionTag("v1.5", out var local) && !(same > local),
            "v1.5 == v1.5");
        h.Check("older version is not an update",
            GitHubReleaseSource.TryParseVersionTag("v1.4", out var older) &&
            GitHubReleaseSource.TryParseVersionTag("v1.5", out var baseline) && !(older > baseline),
            "v1.4 < v1.5");
        h.Check("three-part tag", GitHubReleaseSource.TryParseVersionTag("v1.5.2", out var patch) && patch.Build == 2,
            $"parsed={patch}");
        h.Check("build tag rejected", !GitHubReleaseSource.TryParseVersionTag("b10152", out _),
            "llama.cpp-style tags must not be read as launcher versions");
        h.Check("garbage rejected", !GitHubReleaseSource.TryParseVersionTag("nightly", out _), "unparseable tag");

        h.Section("GitHubReleaseSource - release cache round-trip");

        var cached = new ReleaseInfo
        {
            Tag = "v1.5",
            Name = "v1.5",
            PublishedAt = new DateTime(2026, 7, 22, 22, 47, 0, DateTimeKind.Utc),
            Body = "notes",
            Assets =
            {
                new ReleaseAsset
                {
                    Name = "LlamaServerLauncher_win_x64.exe",
                    Size = 49_876_543,
                    DownloadUrl = "https://github.com/o/r/releases/download/v1.5/LlamaServerLauncher_win_x64.exe",
                    Digest = "8c79a9b226de0000000000000000000000000000000000000000000000000000"
                },
                new ReleaseAsset { Name = "SHA256SUMS", Size = 512, DownloadUrl = "https://example/x" }
            }
        };

        var cacheJson = System.Text.Json.JsonSerializer.Serialize(cached);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ReleaseInfo>(
            cacheJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        h.Check("digest survives the disk cache",
            restored?.Assets[0].Digest == cached.Assets[0].Digest,
            $"digest={restored?.Assets[0].Digest ?? "<null>"}");
        h.Check("size and url survive",
            restored?.Assets[0].Size == 49_876_543 && restored.Assets[0].DownloadUrl == cached.Assets[0].DownloadUrl,
            $"size={restored?.Assets[0].Size}");
        h.Check("null digest omitted from json", !cacheJson.Contains("\"Digest\":null"),
            "keeps cached experimental releases out of AppSettings bloat");
        h.Check("computed SizeMB not serialized", !cacheJson.Contains("SizeMB"), "SizeMB is derived, not stored");
        h.Check("tag and date survive",
            restored?.Tag == "v1.5" && restored.PublishedAt.ToUniversalTime().Hour == 22,
            $"tag={restored?.Tag} published={restored?.PublishedAt:u}");

        h.Section("GitHubReleaseSource - html to text");

        h.Check("br becomes newline", GitHubReleaseSource.HtmlToPlainText("a<br/>b") == "a\nb", "a<br/>b");
        h.Check("blank lines collapsed",
            GitHubReleaseSource.HtmlToPlainText("<p>a</p><p></p><p></p><p>b</p>") == "a\n\nb",
            "no runs of 3+ newlines");
        h.Check("empty input", GitHubReleaseSource.HtmlToPlainText("") == "", "empty stays empty");
    }
}
