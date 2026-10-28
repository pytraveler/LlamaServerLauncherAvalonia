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
        h.Check("get no api key", noBody.ApiKey == null, noBody.ApiKey ?? "null");
        h.Check("api key from bearer", head.ApiKey == "sk-secret", head.ApiKey ?? "null");

        var anthropic = ProxyProtocol.ParseRequestHead("POST /v1/messages HTTP/1.1\r\nx-api-key:  sk-ant \r\n\r\n");
        h.Check("api key from x-api-key", anthropic!.ApiKey == "sk-ant", anthropic.ApiKey ?? "null");
        h.Check("x-api-key is not bearer", anthropic.BearerToken == null, anthropic.BearerToken ?? "null");

        var both = ProxyProtocol.ParseRequestHead("POST /v1/messages HTTP/1.1\r\nAuthorization: Bearer first\r\nX-Api-Key: second\r\n\r\n");
        h.Check("bearer wins over x-api-key", both!.ApiKey == "first", both.ApiKey ?? "null");

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

        h.Section("ProxyProtocol.ExtractRequestedModel");

        var json = System.Text.Encoding.UTF8.GetBytes("{\"model\":\"Coder\"}");
        h.Check("json body", ProxyProtocol.ExtractRequestedModel("application/json", json) == "Coder", "ok");
        h.Check("json without content-type", ProxyProtocol.ExtractRequestedModel(null, json) == "Coder", "ok");
        h.Check("null body", ProxyProtocol.ExtractRequestedModel("application/json", null) == null, "ok");
        h.Check("empty body", ProxyProtocol.ExtractRequestedModel("application/json", Array.Empty<byte>()) == null, "ok");

        var audio = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x0D, 0x0A, 0x0D, 0x0A, 0x2D, 0x2D, 0x00, 0xFF, 0xFE };
        var form = Multipart("xYz123",
            ("file", "speech.wav", audio),
            ("model", null, System.Text.Encoding.UTF8.GetBytes("Whisper Тест")),
            ("language", null, System.Text.Encoding.UTF8.GetBytes("ru")));
        var formModel = ProxyProtocol.ExtractRequestedModel("multipart/form-data; boundary=xYz123", form);
        h.Check("multipart model after binary file", formModel == "Whisper Тест", formModel ?? "null");

        var quoted = ProxyProtocol.ExtractRequestedModel("Multipart/Form-Data; charset=utf-8; boundary=\"xYz123\"", form);
        h.Check("multipart quoted boundary", quoted == "Whisper Тест", quoted ?? "null");

        var noModel = Multipart("b0", ("file", "a.wav", audio), ("modelx", null, System.Text.Encoding.UTF8.GetBytes("nope")));
        h.Check("multipart without model -> null", ProxyProtocol.ExtractRequestedModel("multipart/form-data; boundary=b0", noModel) == null, "ok");

        var filenamed = Multipart("b1", ("file", "model", audio));
        h.Check("filename is not the field name", ProxyProtocol.ExtractRequestedModel("multipart/form-data; boundary=b1", filenamed) == null, "ok");

        var blank = Multipart("b2", ("model", null, System.Text.Encoding.UTF8.GetBytes("  ")));
        h.Check("multipart blank model -> null", ProxyProtocol.ExtractRequestedModel("multipart/form-data; boundary=b2", blank) == null, "ok");

        h.Check("wrong boundary -> null", ProxyProtocol.ExtractRequestedModel("multipart/form-data; boundary=other", form) == null, "ok");
        h.Check("multipart without boundary -> null", ProxyProtocol.ExtractRequestedModel("multipart/form-data", form) == null, "ok");

        var truncated = form.AsSpan(0, form.Length - 12).ToArray();
        var cut = ProxyProtocol.ExtractRequestedModel("multipart/form-data; boundary=xYz123", truncated);
        h.Check("truncated tail still finds earlier field", cut == "Whisper Тест", cut ?? "null");
    }

    private static byte[] Multipart(string boundary, params (string Name, string? FileName, byte[] Content)[] fields)
    {
        using var ms = new System.IO.MemoryStream();
        void Write(string s) { var b = System.Text.Encoding.UTF8.GetBytes(s); ms.Write(b, 0, b.Length); }
        foreach (var f in fields)
        {
            Write($"--{boundary}\r\n");
            Write(f.FileName == null
                ? $"Content-Disposition: form-data; name=\"{f.Name}\"\r\n\r\n"
                : $"Content-Disposition: form-data; name=\"{f.Name}\"; filename=\"{f.FileName}\"\r\nContent-Type: audio/wav\r\n\r\n");
            ms.Write(f.Content, 0, f.Content.Length);
            Write("\r\n");
        }
        Write($"--{boundary}--\r\n");
        return ms.ToArray();
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
        foreach (var p in new[]
        {
            "/chat/completions", "/v1/responses", "/responses", "/v1/messages", "/v1/messages/count_tokens",
            "/embeddings", "/embedding", "/v1/reranking", "/reranking",
            "/v1/audio/transcriptions", "/audio/transcriptions", "/tts", "/v1/systemone",
            "/tokenize", "/detokenize", "/apply-template",
            "/v1/chat/completions/input_tokens", "/v1/responses/input_tokens",
        })
            h.Check($"{p} proxied", ProxyProtocol.IsProxiedApiPath(p), "ok");
        foreach (var p in new[] { "/v1/chat/completions/control", "/props", "/slots", "/lora-adapters", "/v1/realtime", "/metrics" })
            h.Check($"{p} not proxied", !ProxyProtocol.IsProxiedApiPath(p), "ok");
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
