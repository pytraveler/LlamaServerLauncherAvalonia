using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LlamaServerLauncher.Models;

public sealed class ProxyRequestHead
{
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public string Query { get; init; } = "";
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public int ContentLength =>
        Headers.TryGetValue("Content-Length", out var v) && int.TryParse(v, out var n) && n >= 0 ? n : 0;

    public string? BearerToken =>
        Headers.TryGetValue("Authorization", out var v) && v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? v.Substring(7).Trim()
            : null;

    public string? ApiKey =>
        BearerToken ?? (Headers.TryGetValue("X-Api-Key", out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null);
}

public static class ProxyProtocol
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length",
    };

    private static readonly HashSet<string> ProxiedApiPaths = new(StringComparer.Ordinal)
    {
        "/v1/chat/completions", "/chat/completions", "/v1/completions", "/completion", "/completions",
        "/v1/responses", "/responses", "/v1/messages", "/infill",
        "/v1/embeddings", "/embeddings", "/embedding",
        "/v1/rerank", "/rerank", "/v1/reranking", "/reranking",
        "/v1/audio/transcriptions", "/audio/transcriptions", "/tts", "/v1/systemone",
        "/tokenize", "/detokenize", "/apply-template",
        "/v1/chat/completions/input_tokens", "/chat/completions/input_tokens",
        "/v1/responses/input_tokens", "/responses/input_tokens", "/v1/messages/count_tokens",
    };

    public static ProxyRequestHead? ParseRequestHead(string head)
    {
        if (string.IsNullOrEmpty(head)) return null;

        var lines = head.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var target = requestLine[1];
        var path = target;
        var query = "";
        var q = target.IndexOf('?');
        if (q >= 0)
        {
            path = target.Substring(0, q);
            query = target.Substring(q + 1);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var c = line.IndexOf(':');
            if (c <= 0) continue;
            headers[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
        }

        return new ProxyRequestHead
        {
            Method = requestLine[0],
            Path = path,
            Query = query,
            Headers = headers,
        };
    }

    public static string? ExtractModelField(string jsonBody)
    {
        if (string.IsNullOrWhiteSpace(jsonBody)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
            {
                var s = m.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ExtractRequestedModel(string? contentType, byte[]? body)
    {
        if (body is not { Length: > 0 }) return null;
        var boundary = MultipartBoundary(contentType);
        return boundary == null
            ? ExtractModelField(Encoding.UTF8.GetString(body))
            : ExtractMultipartField(body, boundary, "model");
    }

    private static string? MultipartBoundary(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var parts = contentType.Split(';');
        if (!parts[0].Trim().Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var p in parts.Skip(1))
        {
            var kv = p.Trim();
            if (!kv.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase)) continue;
            var b = kv.Substring(9).Trim().Trim('"');
            return b.Length == 0 ? null : b;
        }
        return null;
    }

    private static string? ExtractMultipartField(byte[] body, string boundary, string field)
    {
        var data = body.AsSpan();
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var pos = data.IndexOf(delimiter);
        while (pos >= 0)
        {
            var part = data.Slice(pos + delimiter.Length);
            var headerEnd = part.IndexOf("\r\n\r\n"u8);
            if (part.StartsWith("--"u8) || headerEnd < 0) return null;
            var content = part.Slice(headerEnd + 4);
            var next = content.IndexOf(delimiter);
            if (next < 0) return null;
            if (FormFieldName(Encoding.UTF8.GetString(part.Slice(0, headerEnd))) == field)
            {
                var value = Encoding.UTF8.GetString(content.Slice(0, next)).Trim();
                return value.Length == 0 ? null : value;
            }
            pos += delimiter.Length + headerEnd + 4 + next;
        }
        return null;
    }

    private static string? FormFieldName(string partHeaders)
    {
        foreach (var line in partHeaders.Split("\r\n"))
        {
            var c = line.IndexOf(':');
            if (c <= 0 || !line.Substring(0, c).Trim().Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var p in line.Substring(c + 1).Split(';'))
            {
                var kv = p.Trim();
                if (kv.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    return kv.Substring(5).Trim('"');
            }
        }
        return null;
    }

    public static string BuildModelsListJson(IEnumerable<string> profileNames)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var data = profileNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => new
            {
                id = n,
                @object = "model",
                created,
                owned_by = "llama-server-launcher",
            })
            .ToList();
        return JsonSerializer.Serialize(new { @object = "list", data });
    }

    public static string? MatchProfile(string? requested, IReadOnlyList<string> profiles, string? fallback)
    {
        if (profiles == null || profiles.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exact = profiles.FirstOrDefault(p => string.Equals(p, requested, StringComparison.Ordinal));
            if (exact != null) return exact;
            var ci = profiles.FirstOrDefault(p => string.Equals(p, requested, StringComparison.OrdinalIgnoreCase));
            if (ci != null) return ci;
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            var fb = profiles.FirstOrDefault(p => string.Equals(p, fallback, StringComparison.Ordinal));
            if (fb != null) return fb;
        }

        return null;
    }

    public static bool IsProxiedApiPath(string path) => ProxiedApiPaths.Contains(path);

    public static bool IsForwardableResponseHeader(string name) => !HopByHopHeaders.Contains(name);

    public static byte[] BuildSimpleResponse(string status, string contentType, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var result = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, result, headerBytes.Length, bodyBytes.Length);
        return result;
    }

    public static byte[] BuildResponseHead(int statusCode, string reasonPhrase, IEnumerable<KeyValuePair<string, string>> headers)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reasonPhrase).Append("\r\n");
        foreach (var h in headers)
        {
            if (!IsForwardableResponseHeader(h.Key)) continue;
            sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        }
        sb.Append("Connection: close\r\n\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
