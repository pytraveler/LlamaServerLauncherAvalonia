using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaServerLauncher.Models;

public sealed record HfRepoRef
{
    public const string DefaultEndpoint = "https://huggingface.co";

    private static readonly string[] CanonicalHosts = { "huggingface.co", "hf.co" };
    private static readonly string[] PathMarkers = { "tree", "blob", "resolve", "raw", "commit" };

    public string RepoId { get; init; } = "";
    public string Revision { get; init; } = "main";
    public string Subfolder { get; init; } = "";
    public string Endpoint { get; init; } = DefaultEndpoint;

    public bool IsMirrored => Endpoint.TrimEnd('/') != DefaultEndpoint;

    public string Slug
    {
        get
        {
            if (Revision == "main" && Subfolder.Length == 0) return RepoId;
            var parts = new List<string> { RepoId, "tree", Revision };
            if (Subfolder.Length > 0) parts.Add(Subfolder);
            return string.Join("/", parts);
        }
    }

    public string PageUrl
    {
        get
        {
            var parts = new List<string> { Endpoint, RepoId, "tree", Uri.EscapeDataString(Revision) };
            if (Subfolder.Length > 0) parts.Add(Subfolder);
            return string.Join("/", parts);
        }
    }

    public string TreeUrl
    {
        get
        {
            var url = $"{Endpoint}/api/models/{RepoId}/tree/{EscapePath(Revision)}";
            if (Subfolder.Length > 0) url += "/" + EscapePath(Subfolder.Trim('/'));
            return url + "?recursive=1&limit=1000";
        }
    }

    public string RefsUrl => $"{Endpoint}/api/models/{RepoId}/refs";

    public string ResolveUrl(string path) =>
        $"{Endpoint}/{RepoId}/resolve/{EscapePath(Revision)}/{EscapePath(path)}";

    public override string ToString() =>
        RepoId + (Revision == "main" ? "" : ":" + Revision) + (Subfolder.Length > 0 ? "/" + Subfolder : "");

    public static string NormaliseEndpoint(string? text)
    {
        var raw = (text ?? "").Trim().Trim('"');
        if (raw.Length == 0) return DefaultEndpoint;
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)) return DefaultEndpoint;
        if (parsed.Scheme != "http" && parsed.Scheme != "https") return DefaultEndpoint;
        if (string.IsNullOrEmpty(parsed.Host)) return DefaultEndpoint;
        return $"{parsed.Scheme}://{parsed.Authority}{parsed.AbsolutePath.TrimEnd('/')}";
    }

    public static string DefaultEndpointFromEnvironment()
    {
        var raw = (Environment.GetEnvironmentVariable("HF_ENDPOINT") ?? "").Trim();
        return raw.Length == 0 ? DefaultEndpoint : NormaliseEndpoint(raw);
    }

    public static bool LooksLikeRef(string? text)
    {
        var raw = (text ?? "").Trim().Trim('"', '\'');
        if (raw.Length == 0) return false;
        if (raw.Any(char.IsWhiteSpace)) return false;
        return raw.Contains("://", StringComparison.Ordinal) || raw.Contains('/');
    }

    public static bool TryParse(string? text, string? endpoint, out HfRepoRef result, out string error)
    {
        result = new HfRepoRef();
        error = "";

        var raw = (text ?? "").Trim().Trim('"', '\'');
        if (raw.Length == 0)
        {
            error = "empty";
            return false;
        }

        var host = NormaliseEndpoint(endpoint);
        var allowed = new List<string>(CanonicalHosts);
        if (Uri.TryCreate(host, UriKind.Absolute, out var endpointUri) && endpointUri.Host.Length > 0)
            allowed.Add(endpointUri.Host.ToLowerInvariant());

        bool looksLikeUrl = raw.Contains("://", StringComparison.Ordinal)
            || allowed.Any(h => raw.StartsWith(h + "/", StringComparison.OrdinalIgnoreCase));

        if (looksLikeUrl)
        {
            var candidate = raw.Contains("://", StringComparison.Ordinal) ? raw : "https://" + raw;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            {
                error = "badref";
                return false;
            }

            var parsedHost = parsed.Host.ToLowerInvariant();
            if (parsedHost.Length > 0
                && !allowed.Any(h => parsedHost == h || parsedHost.EndsWith("." + h, StringComparison.Ordinal)))
            {
                error = "otherhost:" + parsedHost;
                return false;
            }

            raw = Uri.UnescapeDataString(parsed.AbsolutePath);

            var prefix = endpointUri?.AbsolutePath.Trim('/') ?? "";
            if (prefix.Length > 0 && parsedHost == endpointUri!.Host.ToLowerInvariant())
            {
                var trimmed = raw.Trim('/');
                if (trimmed == prefix) raw = "";
                else if (trimmed.StartsWith(prefix + "/", StringComparison.Ordinal))
                    raw = trimmed.Substring(prefix.Length + 1);
            }
        }

        var parts = raw.Split('/').Where(p => p.Length > 0 && p != ".").ToList();
        if (parts.Count == 0 || PathMarkers.Contains(parts[0]))
        {
            error = "badref";
            return false;
        }

        if (parts[0].Equals("datasets", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("spaces", StringComparison.OrdinalIgnoreCase))
        {
            error = "notamodel:" + parts[0].ToLowerInvariant();
            return false;
        }

        string repoId;
        List<string> rest;
        if (parts.Count >= 2 && !PathMarkers.Contains(parts[1]))
        {
            repoId = parts[0] + "/" + parts[1];
            rest = parts.Skip(2).ToList();
        }
        else
        {
            repoId = parts[0];
            rest = parts.Skip(1).ToList();
        }

        if (repoId.Length == 0 || PathMarkers.Contains(repoId))
        {
            error = "badref";
            return false;
        }

        if (rest.Count == 0 || !PathMarkers.Contains(rest[0]))
        {
            result = new HfRepoRef { RepoId = repoId, Endpoint = host };
            return true;
        }

        var marker = rest[0];
        rest = rest.Skip(1).ToList();
        var (revision, tail) = SplitRevision(rest);
        if (tail.Count > 0 && (marker == "blob" || marker == "resolve" || marker == "raw"))
            tail = tail.Take(tail.Count - 1).ToList();

        result = new HfRepoRef
        {
            RepoId = repoId,
            Revision = revision,
            Subfolder = string.Join("/", tail),
            Endpoint = host,
        };
        return true;
    }

    private static (string Revision, List<string> Tail) SplitRevision(List<string> parts)
    {
        if (parts.Count == 0) return ("main", new List<string>());
        if (parts[0] == "refs" && parts.Count >= 3)
            return (string.Join("/", parts.Take(3)), parts.Skip(3).ToList());
        return (parts[0], parts.Skip(1).ToList());
    }

    private static string EscapePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
}
