using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public enum HfErrorKind
{
    None,
    Network,
    Auth,
    Gated,
    NotFound,
    RateLimited,
    Server,
    Cancelled,
    NoSpace,
    Verify,
}

public sealed record HfError(HfErrorKind Kind, int StatusCode, string Subject)
{
    public static HfError From(int status, string subject) => status switch
    {
        401 => new HfError(HfErrorKind.Auth, status, subject),
        403 => new HfError(HfErrorKind.Gated, status, subject),
        404 => new HfError(HfErrorKind.NotFound, status, subject),
        429 => new HfError(HfErrorKind.RateLimited, status, subject),
        _ => new HfError(HfErrorKind.Server, status, subject),
    };
}

public sealed class HfResult<T>
{
    public T? Value { get; init; }
    public HfError? Error { get; init; }
    public bool Ok => Error == null;

    public static HfResult<T> Success(T value) => new() { Value = value };

    public static HfResult<T> Failure(HfError error) => new() { Error = error };
}

public sealed class HfTokenProvider
{
    private readonly Func<string?> _fromSettings;
    private readonly bool _useAmbient;

    public HfTokenProvider(Func<string?>? fromSettings = null, bool useAmbient = true)
    {
        _fromSettings = fromSettings ?? (() => null);
        _useAmbient = useAmbient;
    }

    public bool HasToken => !string.IsNullOrEmpty(Resolve());

    public string? Resolve()
    {
        var configured = _fromSettings();
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (!_useAmbient) return null;

        foreach (var name in new[] { "HF_TOKEN", "HUGGING_FACE_HUB_TOKEN", "HUGGINGFACEHUB_API_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return ReadTokenFile();
    }

    public static string? ReadTokenFile()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return null;
            var path = Path.Combine(home, ".cache", "huggingface", "token");
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class HuggingFaceClient
{
    public const int DefaultSearchLimit = 30;
    private const int MaxTreePages = 40;

    private static readonly HttpClient SharedApi = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly HttpClient _http;
    private readonly HfTokenProvider _tokens;

    public HuggingFaceClient(HfTokenProvider? tokens = null)
    {
        _tokens = tokens ?? new HfTokenProvider();
        _http = SharedApi;
    }

    internal HuggingFaceClient(HttpMessageHandler handler, HfTokenProvider? tokens = null)
    {
        _tokens = tokens ?? new HfTokenProvider();
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static string UserAgent => "LlamaServerLauncher/" + AppInfo.Version;

    public static string SearchUrl(string endpoint, string query, int limit) =>
        endpoint + "/api/models?search=" + Uri.EscapeDataString(query ?? "")
        + "&filter=gguf&sort=downloads&direction=-1&limit="
        + Math.Clamp(limit, 1, 100).ToString(CultureInfo.InvariantCulture);

    public async Task<HfResult<List<HfRepoSummary>>> SearchModelsAsync(string query, string? endpoint,
        int limit, CancellationToken ct)
    {
        var host = HfRepoRef.NormaliseEndpoint(endpoint);
        var body = await GetStringAsync(SearchUrl(host, query, limit), ct).ConfigureAwait(false);
        if (!body.Ok) return HfResult<List<HfRepoSummary>>.Failure(body.Error!);
        return HfResult<List<HfRepoSummary>>.Success(HfApiParser.ParseSearch(body.Value));
    }

    public async Task<HfResult<List<HfRemoteFile>>> ListFilesAsync(HfRepoRef repo, CancellationToken ct)
    {
        var files = new List<HfRemoteFile>();
        var url = repo.TreeUrl;

        for (int page = 0; page < MaxTreePages && !string.IsNullOrEmpty(url); page++)
        {
            var response = await SendAsync(url!, repo.RepoId, null, ct).ConfigureAwait(false);
            if (!response.Ok) return HfResult<List<HfRemoteFile>>.Failure(response.Error!);

            using var message = response.Value!;
            var body = await message.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            files.AddRange(HfApiParser.ParseTree(body));

            url = message.Headers.TryGetValues("Link", out var link)
                ? HfApiParser.NextPageUrl(string.Join(",", link))
                : null;
        }

        return HfResult<List<HfRemoteFile>>.Success(files);
    }

    public async Task<List<string>> ListRevisionsAsync(HfRepoRef repo, CancellationToken ct)
    {
        var body = await GetStringAsync(repo.RefsUrl, ct).ConfigureAwait(false);
        return body.Ok ? HfApiParser.ParseRefs(body.Value, repo.Revision) : new List<string> { repo.Revision };
    }

    public async Task<HfResult<byte[]>> GetRangeAsync(string url, long from, long toInclusive,
        CancellationToken ct)
    {
        var response = await SendAsync(url, url, request =>
        {
            request.Headers.Range = new RangeHeaderValue(from, toInclusive);
            request.Headers.AcceptEncoding.ParseAdd("identity");
        }, ct).ConfigureAwait(false);
        if (!response.Ok) return HfResult<byte[]>.Failure(response.Error!);

        using var message = response.Value!;
        var bytes = await message.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return HfResult<byte[]>.Success(bytes);
    }

    private async Task<HfResult<string>> GetStringAsync(string url, CancellationToken ct)
    {
        var response = await SendAsync(url, url, null, ct).ConfigureAwait(false);
        if (!response.Ok) return HfResult<string>.Failure(response.Error!);

        using var message = response.Value!;
        var body = await message.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return HfResult<string>.Success(body);
    }

    private async Task<HfResult<HttpResponseMessage>> SendAsync(string url, string subject,
        Action<HttpRequestMessage>? decorate, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("application/json");

            var token = _tokens.Resolve();
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            decorate?.Invoke(request);

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if ((int)response.StatusCode >= 400)
            {
                var error = HfError.From((int)response.StatusCode, subject);
                response.Dispose();
                return HfResult<HttpResponseMessage>.Failure(error);
            }

            return HfResult<HttpResponseMessage>.Success(response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return HfResult<HttpResponseMessage>.Failure(new HfError(HfErrorKind.Cancelled, 0, subject));
        }
        catch (Exception ex)
        {
            return HfResult<HttpResponseMessage>.Failure(new HfError(HfErrorKind.Network, 0, ex.Message));
        }
    }
}
