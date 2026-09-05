using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;

public static class HfTransferTests
{
    public static void Run(Harness h)
    {
        RunClient(h);
        RunDownload(h);
        RunTokens(h);
    }

    private static void RunClient(Harness h)
    {
        h.Section("HuggingFaceClient: talking to the api");

        var handler = new StubHandler();
        handler.Respond("*/api/models?*", _ => Json(@"[{""id"":""a/b"",""downloads"":10}]"));
        var client = new HuggingFaceClient(handler, Token("hf_secret"));

        var search = Wait(client.SearchModelsAsync("qwen gguf", null, 30, CancellationToken.None));
        h.Check("a search comes back as repositories",
            search.Ok && search.Value!.Count == 1 && search.Value[0].Id == "a/b",
            search.Ok ? search.Value![0].Id : search.Error!.Kind.ToString());
        h.Check("the query is escaped into the url",
            handler.LastRequestUrl.Contains("search=qwen%20gguf", StringComparison.Ordinal),
            handler.LastRequestUrl);
        h.Check("only gguf repositories are asked for",
            handler.LastRequestUrl.Contains("filter=gguf", StringComparison.Ordinal), handler.LastRequestUrl);
        h.Check("the token is sent to the api", handler.LastAuthorization == "Bearer hf_secret",
            Mask(handler.LastAuthorization));
        h.Check("and the launcher names itself",
            handler.LastUserAgent?.StartsWith("LlamaServerLauncher/", StringComparison.Ordinal) == true,
            handler.LastUserAgent ?? "none");

        var paged = new StubHandler();
        int page = 0;
        paged.Respond("*/tree/*", _ =>
        {
            page++;
            var body = page == 1
                ? @"[{""type"":""file"",""path"":""a.gguf"",""size"":1}]"
                : @"[{""type"":""file"",""path"":""b.gguf"",""size"":2}]";
            var response = Json(body);
            if (page == 1)
                response.Headers.TryAddWithoutValidation("Link",
                    "<https://huggingface.co/api/models/o/n/tree/main?cursor=x>; rel=\"next\"");
            return response;
        });
        var pagedClient = new HuggingFaceClient(paged, Token(null));
        HfRepoRef.TryParse("o/n", null, out var repo, out _);
        var listed = Wait(pagedClient.ListFilesAsync(repo, CancellationToken.None));
        h.Check("a paged file list is followed to the end",
            listed.Ok && listed.Value!.Count == 2, listed.Value?.Count.ToString(CultureInfo.InvariantCulture) ?? "err");
        h.Check("no token, no authorization header", paged.LastAuthorization == null,
            Mask(paged.LastAuthorization));

        var gated = new StubHandler();
        gated.Respond("*", _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var gatedResult = Wait(new HuggingFaceClient(gated, Token(null))
            .ListFilesAsync(repo, CancellationToken.None));
        h.Check("a gated repository is named as such",
            !gatedResult.Ok && gatedResult.Error!.Kind == HfErrorKind.Gated,
            gatedResult.Error?.Kind.ToString() ?? "ok");

        var missing = new StubHandler();
        missing.Respond("*", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var missingResult = Wait(new HuggingFaceClient(missing, Token(null))
            .ListFilesAsync(repo, CancellationToken.None));
        h.Check("a repository that is not there is not a server error",
            missingResult.Error!.Kind == HfErrorKind.NotFound, missingResult.Error.Kind.ToString());

        var limited = new StubHandler();
        limited.Respond("*", _ => new HttpResponseMessage((HttpStatusCode)429));
        var limitedResult = Wait(new HuggingFaceClient(limited, Token(null))
            .ListFilesAsync(repo, CancellationToken.None));
        h.Check("a rate limit says so", limitedResult.Error!.Kind == HfErrorKind.RateLimited,
            limitedResult.Error.Kind.ToString());
    }

    private static void RunDownload(Harness h)
    {
        h.Section("HfDownloadService: moving the file");

        var dir = NewDir();
        try
        {
            var payload = Payload(4096);

            var plain = new StubHandler();
            plain.Respond("*", req => Bytes(payload, req));
            var outcome = Download(plain, dir, "model.gguf", payload.Length, null);
            h.Check("a straight download succeeds", outcome.Success, Describe(outcome));
            h.Check("and lands under the chosen name",
                File.Exists(Path.Combine(dir, "model.gguf")), "ok");
            h.Check("with every byte in place",
                FileBytes(Path.Combine(dir, "model.gguf")).SequenceEqual(payload), "identical");
            h.Check("and nothing half written left behind",
                !File.Exists(Path.Combine(dir, "model.gguf.part"))
                && !File.Exists(Path.Combine(dir, "model.gguf.part.json")), "clean");
        }
        finally
        {
            Cleanup(dir);
        }

        h.Section("HfDownloadService: the token and the cdn");

        var redirectDir = NewDir();
        try
        {
            var payload = Payload(512);
            var redirect = new StubHandler();
            redirect.Respond("https://huggingface.co/*", _ =>
            {
                var moved = new HttpResponseMessage(HttpStatusCode.Found);
                moved.Headers.Location = new Uri("https://cdn-lfs.huggingface.co/repos/x?sig=y");
                return moved;
            });
            redirect.Respond("https://cdn-lfs.huggingface.co/*", req => Bytes(payload, req));

            var outcome = Download(redirect, redirectDir, "m.gguf", payload.Length, "hf_secret");
            h.Check("a redirect to the cdn is followed", outcome.Success, Describe(outcome));
            h.Check("the token is sent to huggingface.co",
                redirect.AuthorizationFor("huggingface.co") == "Bearer hf_secret",
                Mask(redirect.AuthorizationFor("huggingface.co")));
            h.Check("but never to the cdn",
                redirect.AuthorizationFor("cdn-lfs.huggingface.co") == null,
                Mask(redirect.AuthorizationFor("cdn-lfs.huggingface.co")));
        }
        finally
        {
            Cleanup(redirectDir);
        }

        h.Section("HfDownloadService: picking up where it stopped");

        var resumeDir = NewDir();
        try
        {
            var payload = Payload(2048);
            var target = Path.Combine(resumeDir, "m.gguf");
            File.WriteAllBytes(target + ".part", payload.Take(800).ToArray());
            File.WriteAllText(target + ".part.json",
                @"{""ExpectedSize"":2048,""Oid"":""abc"",""Path"":""m.gguf""}");

            var resume = new StubHandler();
            resume.Respond("*", req => Bytes(payload, req));
            var outcome = Download(resume, resumeDir, "m.gguf", payload.Length, null, oid: "abc");

            h.Check("a part file is carried on", outcome.Success, Describe(outcome));
            h.Check("the range asked for the rest only", resume.LastRange == "bytes=800-", resume.LastRange ?? "none");
            h.Check("and the finished file is whole",
                FileBytes(target).SequenceEqual(payload), "identical");
        }
        finally
        {
            Cleanup(resumeDir);
        }

        var ignoredDir = NewDir();
        try
        {
            var payload = Payload(1024);
            var target = Path.Combine(ignoredDir, "m.gguf");
            File.WriteAllBytes(target + ".part", payload.Take(400).ToArray());
            File.WriteAllText(target + ".part.json", @"{""ExpectedSize"":1024,""Path"":""m.gguf""}");

            var ignores = new StubHandler();
            ignores.Respond("*", _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload),
                };
                return response;
            });
            var outcome = Download(ignores, ignoredDir, "m.gguf", payload.Length, null);

            h.Check("a server that ignores the range starts the file over", outcome.Success, Describe(outcome));
            h.Check("and the result is still correct, not the two glued together",
                FileBytes(target).Length == payload.Length && FileBytes(target).SequenceEqual(payload),
                FileBytes(target).Length.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            Cleanup(ignoredDir);
        }

        var conflictDir = NewDir();
        try
        {
            var payload = Payload(600);
            var target = Path.Combine(conflictDir, "m.gguf");
            File.WriteAllBytes(target + ".part", Payload(300));
            File.WriteAllText(target + ".part.json",
                @"{""ExpectedSize"":600,""Oid"":""different"",""Path"":""m.gguf""}");

            var stub = new StubHandler();
            stub.Respond("*", req => Bytes(payload, req));
            var outcome = Download(stub, conflictDir, "m.gguf", payload.Length, null, oid: "abc");

            h.Check("a part left over from another file is thrown away", outcome.Success, Describe(outcome));
            h.Check("the download started from the beginning", stub.LastRange == null, stub.LastRange ?? "none");
            h.Check("and the file is the one that was asked for",
                FileBytes(target).SequenceEqual(payload), "identical");
        }
        finally
        {
            Cleanup(conflictDir);
        }

        h.Section("HfDownloadService: when things go wrong");

        var retryDir = NewDir();
        try
        {
            var payload = Payload(256);
            int calls = 0;
            var flaky = new StubHandler();
            flaky.Respond("*", req =>
            {
                calls++;
                if (calls <= 2) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                return Bytes(payload, req);
            });
            var outcome = Download(flaky, retryDir, "m.gguf", payload.Length, null);
            h.Check("a flaky server is retried", outcome.Success, Describe(outcome));
            h.Check("and it took the retries to get there", calls == 3,
                calls.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            Cleanup(retryDir);
        }

        var gatedDir = NewDir();
        try
        {
            var refused = new StubHandler();
            refused.Respond("*", _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
            var outcome = Download(refused, gatedDir, "m.gguf", 100, null);
            h.Check("a gated file is not retried into the ground",
                !outcome.Success && outcome.Error!.Kind == HfErrorKind.Gated, Describe(outcome));
        }
        finally
        {
            Cleanup(gatedDir);
        }

        var shortDir = NewDir();
        try
        {
            var stub = new StubHandler();
            stub.Respond("*", req => Bytes(Payload(100), req));
            var outcome = Download(stub, shortDir, "m.gguf", 999, null);
            h.Check("a file that arrives the wrong size is refused",
                !outcome.Success && outcome.Error!.Kind == HfErrorKind.Verify, Describe(outcome));
            h.Check("and it is not promoted into place",
                !File.Exists(Path.Combine(shortDir, "m.gguf")), "kept out");
            h.Check("while what did arrive is kept for a retry",
                File.Exists(Path.Combine(shortDir, "m.gguf.part")), "kept");
        }
        finally
        {
            Cleanup(shortDir);
        }

        var cancelDir = NewDir();
        try
        {
            var payload = Payload(1 << 20);
            using var cts = new CancellationTokenSource();
            var stub = new StubHandler();
            stub.Respond("*", req =>
            {
                cts.Cancel();
                return Bytes(payload, req);
            });
            var outcome = Download(stub, cancelDir, "m.gguf", payload.Length, null, ct: cts.Token);
            h.Check("cancelling stops the download",
                !outcome.Success && outcome.Cancelled, Describe(outcome));
            h.Check("and says it can be picked up again", outcome.Resumable, "resumable");
            h.Check("the finished file is not created",
                !File.Exists(Path.Combine(cancelDir, "m.gguf")), "absent");
        }
        finally
        {
            Cleanup(cancelDir);
        }

        h.Section("HfDownloadService: shards and folders");

        var shardDir = NewDir();
        try
        {
            var first = Payload(300);
            var second = Payload(500);
            var stub = new StubHandler();
            stub.Respond("*00001*", req => Bytes(first, req));
            stub.Respond("*00002*", req => Bytes(second, req));

            HfRepoRef.TryParse("unsloth/Model-GGUF", null, out var repo, out _);
            var service = new HfDownloadService(stub, Token(null), null, _ => Task.CompletedTask);
            var request = new HfDownloadRequest
            {
                Repo = repo,
                TargetDirectory = shardDir,
                Files = new List<HfRemoteFile>
                {
                    new() { Path = "model-00001-of-00002.gguf", SizeBytes = first.Length },
                    new() { Path = "model-00002-of-00002.gguf", SizeBytes = second.Length },
                },
            };
            var outcome = Wait(service.DownloadAsync(request, null, CancellationToken.None));

            var folder = Path.Combine(shardDir, "unsloth_Model-GGUF");
            h.Check("every part is downloaded", outcome.Success, Describe(outcome));
            h.Check("into a folder named after the repository", Directory.Exists(folder), folder);
            h.Check("keeping the exact part names so they can be found again",
                File.Exists(Path.Combine(folder, "model-00001-of-00002.gguf"))
                && File.Exists(Path.Combine(folder, "model-00002-of-00002.gguf")), "both");
            h.Check("and the first part is what gets selected",
                outcome.PrimaryPath == Path.Combine(folder, "model-00001-of-00002.gguf"),
                outcome.PrimaryPath ?? "null");
            h.Check("the bytes reported cover the whole set",
                outcome.BytesWritten == first.Length + second.Length,
                outcome.BytesWritten.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            Cleanup(shardDir);
        }

        var skipDir = NewDir();
        try
        {
            var payload = Payload(128);
            File.WriteAllBytes(Path.Combine(skipDir, "m.gguf"), payload);
            int calls = 0;
            var stub = new StubHandler();
            stub.Respond("*", req => { calls++; return Bytes(payload, req); });
            var outcome = Download(stub, skipDir, "m.gguf", payload.Length, null);
            h.Check("a file already on disk is not fetched again",
                outcome.Success && calls == 0, calls.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            Cleanup(skipDir);
        }
    }

    private static void RunTokens(Harness h)
    {
        h.Section("HfTokenProvider");

        h.Check("what the user typed wins",
            new HfTokenProvider(() => "  from_settings  ").Resolve() == "from_settings",
            Mask(new HfTokenProvider(() => " from_settings ").Resolve()));

        var name = "HF_TOKEN";
        var saved = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "from_env");
            h.Check("the environment is used when the setting is empty",
                new HfTokenProvider(() => "").Resolve() == "from_env",
                Mask(new HfTokenProvider(() => "").Resolve()));
            h.Check("but never over an explicit setting",
                new HfTokenProvider(() => "explicit").Resolve() == "explicit",
                Mask(new HfTokenProvider(() => "explicit").Resolve()));
            h.Check("having a token is noticed", new HfTokenProvider(() => "x").HasToken, "yes");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, saved);
        }
    }

    private static HfDownloadOutcome Download(StubHandler handler, string directory, string fileName,
        long size, string? token, string? oid = null, CancellationToken ct = default)
    {
        HfRepoRef.TryParse("owner/name", null, out var repo, out _);
        var service = new HfDownloadService(handler, Token(token), null, _ => Task.CompletedTask);
        var request = new HfDownloadRequest
        {
            Repo = repo,
            TargetDirectory = directory,
            Files = new List<HfRemoteFile> { new() { Path = fileName, SizeBytes = size, Oid = oid } },
        };
        return Wait(service.DownloadAsync(request, null, ct));
    }

    private static HfTokenProvider Token(string? value) => new(() => value, useAmbient: false);

    private static string Mask(string? secret) =>
        string.IsNullOrEmpty(secret) ? "none"
        : secret == "Bearer hf_secret" ? "the test token"
        : "a token of " + secret.Length.ToString(CultureInfo.InvariantCulture) + " chars";

    private static string Describe(HfDownloadOutcome outcome) =>
        outcome.Success ? "ok"
        : outcome.Cancelled ? "cancelled"
        : outcome.Error?.Kind.ToString() + " " + outcome.Error?.Subject;

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static byte[] FileBytes(string path) => File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] payload, HttpRequestMessage request)
    {
        long from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
        if (from <= 0)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

        var slice = payload.Skip((int)from).ToArray();
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(slice),
        };
        response.Content.Headers.TryAddWithoutValidation("Content-Range",
            "bytes " + from.ToString(CultureInfo.InvariantCulture) + "-"
            + (payload.Length - 1).ToString(CultureInfo.InvariantCulture) + "/"
            + payload.Length.ToString(CultureInfo.InvariantCulture));
        return response;
    }

    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "hf-transfer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(string Pattern, Func<HttpRequestMessage, HttpResponseMessage> Reply)> _routes = new();
        private readonly Dictionary<string, string?> _authByHost = new(StringComparer.OrdinalIgnoreCase);

        public string LastRequestUrl { get; private set; } = "";
        public string? LastAuthorization { get; private set; }
        public string? LastUserAgent { get; private set; }
        public string? LastRange { get; private set; }

        public void Respond(string pattern, Func<HttpRequestMessage, HttpResponseMessage> reply) =>
            _routes.Add((pattern, reply));

        public string? AuthorizationFor(string host) =>
            _authByHost.TryGetValue(host, out var value) ? value : null;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            LastRequestUrl = request.RequestUri.AbsoluteUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastUserAgent = request.Headers.UserAgent.ToString();
            LastRange = request.Headers.Range?.ToString();
            _authByHost[request.RequestUri.Host] = LastAuthorization;

            foreach (var (pattern, reply) in _routes)
            {
                if (Matches(pattern, url)) return Task.FromResult(reply(request));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static bool Matches(string pattern, string url)
        {
            var parts = pattern.Split('*');
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                int found = url.IndexOf(parts[i], at, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return false;
                if (i == 0 && !pattern.StartsWith("*", StringComparison.Ordinal) && found != 0) return false;
                at = found + parts[i].Length;
            }
            return true;
        }
    }
}
