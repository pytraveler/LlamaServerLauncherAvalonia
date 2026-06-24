using System;
using System.Collections.Generic;
using System.Text.Json;
using LlamaServerLauncher.Models;

public static class ProxyProtocolTests
{
    public static void Run(Harness h)
    {
        ParseHead(h);
        ExtractModel(h);
        ModelsList(h);
        Matching(h);
        Routing(h);
        ResponseHeaders(h);
    }

    private static void ParseHead(Harness h)
    {
        h.Section("ProxyProtocol.ParseRequestHead");

        var raw = "POST /v1/chat/completions?foo=bar HTTP/1.1\r\nHost: localhost:8081\r\nContent-Length: 42\r\nAuthorization: Bearer sk-secret\r\n\r\n";
        var head = ProxyProtocol.ParseRequestHead(raw);
        h.Check("not null", head != null, head == null ? "null" : "parsed");
        h.Check("method", head!.Method == "POST", head.Method);
        h.Check("path", head.Path == "/v1/chat/completions", head.Path);
        h.Check("query", head.Query == "foo=bar", head.Query);
        h.Check("host header", head.Headers.GetValueOrDefault("host") == "localhost:8081", head.Headers.GetValueOrDefault("host") ?? "");
        h.Check("content-length", head.ContentLength == 42, head.ContentLength.ToString());
        h.Check("bearer token", head.BearerToken == "sk-secret", head.BearerToken ?? "null");

        var noBody = ProxyProtocol.ParseRequestHead("GET /v1/models HTTP/1.1\r\nHost: x\r\n\r\n");
        h.Check("get no content-length", noBody!.ContentLength == 0, noBody.ContentLength.ToString());
        h.Check("get no bearer", noBody.BearerToken == null, noBody.BearerToken ?? "null");

        h.Check("empty -> null", ProxyProtocol.ParseRequestHead("") == null, "ok");
        h.Check("garbage line -> null", ProxyProtocol.ParseRequestHead("garbage\r\n\r\n") == null, "ok");
    }

    private static void ExtractModel(Harness h)
    {
        h.Section("ProxyProtocol.ExtractModelField");

        h.Check("simple", ProxyProtocol.ExtractModelField("{\"model\":\"my-profile\",\"stream\":true}") == "my-profile", "ok");
        h.Check("with messages", ProxyProtocol.ExtractModelField("{\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"model\":\"p2\"}") == "p2", "ok");
        h.Check("missing -> null", ProxyProtocol.ExtractModelField("{\"stream\":false}") == null, "ok");
        h.Check("empty string -> null", ProxyProtocol.ExtractModelField("{\"model\":\"\"}") == null, "ok");
        h.Check("non-string -> null", ProxyProtocol.ExtractModelField("{\"model\":123}") == null, "ok");
        h.Check("malformed -> null", ProxyProtocol.ExtractModelField("not json") == null, "ok");
        h.Check("array root -> null", ProxyProtocol.ExtractModelField("[1,2,3]") == null, "ok");
        h.Check("blank -> null", ProxyProtocol.ExtractModelField("   ") == null, "ok");
    }

    private static void ModelsList(Harness h)
    {
        h.Section("ProxyProtocol.BuildModelsListJson");

        var json = ProxyProtocol.BuildModelsListJson(new[] { "alpha", "", "beta" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        h.Check("object=list", root.GetProperty("object").GetString() == "list", root.GetProperty("object").GetString() ?? "");
        var data = root.GetProperty("data");
        h.Check("skips blank", data.GetArrayLength() == 2, data.GetArrayLength().ToString());
        h.Check("first id", data[0].GetProperty("id").GetString() == "alpha", data[0].GetProperty("id").GetString() ?? "");
        h.Check("second id", data[1].GetProperty("id").GetString() == "beta", data[1].GetProperty("id").GetString() ?? "");
        h.Check("entry object=model", data[0].GetProperty("object").GetString() == "model", "ok");
    }

    private static void Matching(Harness h)
    {
        h.Section("ProxyProtocol.MatchProfile");

        var profiles = new List<string> { "Coder", "Vision", "Tiny" };
        h.Check("exact", ProxyProtocol.MatchProfile("Vision", profiles, null) == "Vision", "ok");
        h.Check("case-insensitive", ProxyProtocol.MatchProfile("vision", profiles, null) == "Vision", "ok");
        h.Check("fallback when unknown", ProxyProtocol.MatchProfile("nope", profiles, "Tiny") == "Tiny", "ok");
        h.Check("fallback when null request", ProxyProtocol.MatchProfile(null, profiles, "Coder") == "Coder", "ok");
        h.Check("no match no fallback -> null", ProxyProtocol.MatchProfile("nope", profiles, null) == null, "ok");
        h.Check("empty profiles -> null", ProxyProtocol.MatchProfile("Coder", new List<string>(), "Coder") == null, "ok");
        h.Check("exact beats fallback", ProxyProtocol.MatchProfile("Coder", profiles, "Tiny") == "Coder", "ok");
    }

    private static void Routing(Harness h)
    {
        h.Section("ProxyProtocol.IsProxiedApiPath");

        h.Check("chat", ProxyProtocol.IsProxiedApiPath("/v1/chat/completions"), "ok");
        h.Check("completions", ProxyProtocol.IsProxiedApiPath("/v1/completions"), "ok");
        h.Check("embeddings", ProxyProtocol.IsProxiedApiPath("/v1/embeddings"), "ok");
        h.Check("infill", ProxyProtocol.IsProxiedApiPath("/infill"), "ok");
        h.Check("models not proxied", !ProxyProtocol.IsProxiedApiPath("/v1/models"), "ok");
        h.Check("health not proxied", !ProxyProtocol.IsProxiedApiPath("/health"), "ok");
    }

    private static void ResponseHeaders(Harness h)
    {
        h.Section("ProxyProtocol response builders");

        h.Check("keeps content-type", ProxyProtocol.IsForwardableResponseHeader("Content-Type"), "ok");
        h.Check("drops transfer-encoding", !ProxyProtocol.IsForwardableResponseHeader("Transfer-Encoding"), "ok");
        h.Check("drops content-length", !ProxyProtocol.IsForwardableResponseHeader("Content-Length"), "ok");
        h.Check("drops connection", !ProxyProtocol.IsForwardableResponseHeader("connection"), "ok");

        var headers = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", "text/event-stream"),
            new("Transfer-Encoding", "chunked"),
            new("Content-Length", "10"),
        };
        var head = System.Text.Encoding.UTF8.GetString(ProxyProtocol.BuildResponseHead(200, "OK", headers));
        h.Check("status line", head.StartsWith("HTTP/1.1 200 OK\r\n"), "ok");
        h.Check("forwards content-type", head.Contains("Content-Type: text/event-stream"), "ok");
        h.Check("omits transfer-encoding", !head.Contains("Transfer-Encoding"), "ok");
        h.Check("omits upstream content-length", !head.Contains("Content-Length: 10"), "ok");
        h.Check("adds connection close", head.Contains("Connection: close\r\n"), "ok");
        h.Check("ends with blank line", head.EndsWith("\r\n\r\n"), "ok");

        var simple = System.Text.Encoding.UTF8.GetString(ProxyProtocol.BuildSimpleResponse("200 OK", "application/json", "{}"));
        h.Check("simple has content-length", simple.Contains("Content-Length: 2"), "ok");
        h.Check("simple has body", simple.EndsWith("\r\n\r\n{}"), "ok");
    }
}
