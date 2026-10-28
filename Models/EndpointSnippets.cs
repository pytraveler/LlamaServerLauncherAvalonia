using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LlamaServerLauncher.Models;

public static class EndpointSnippets
{
    public const string DefaultHost = "127.0.0.1";

    public static List<string> SplitHosts(string? hosts)
    {
        var result = new List<string>();
        foreach (var part in (hosts ?? "").Split(','))
        {
            var h = part.Trim();
            if (h.Length > 0) result.Add(h);
        }
        return result;
    }

    public static bool IsUnixSocket(string? host) =>
        (host ?? "").Trim().EndsWith(".sock", StringComparison.Ordinal);

    public static string? FirstTcpHost(string? hosts)
    {
        foreach (var h in SplitHosts(hosts))
            if (!IsUnixSocket(h)) return h;
        return null;
    }

    public static bool IsSocketOnly(string? hosts) =>
        SplitHosts(hosts).Count > 0 && FirstTcpHost(hosts) == null;

    public static string NormalizeHost(string? host)
    {
        var h = FirstTcpHost(host) ?? "";
        if (h.Length == 0 || h == "0.0.0.0" || h == "*") return DefaultHost;
        if (h == "::" || h == "[::]") return "[::1]";
        if (h.Contains(':') && !h.StartsWith("[")) return "[" + h + "]";
        return h;
    }

    public static string ConnectHost(string? host) =>
        NormalizeHost(host).TrimStart('[').TrimEnd(']');

    public static bool HostsOverlap(string? a, string? b)
    {
        var left = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in OrDefault(a)) left.Add(Bare(h));
        foreach (var h in OrDefault(b))
            if (left.Contains(Bare(h))) return true;
        return false;

        static List<string> OrDefault(string? hosts)
        {
            var list = SplitHosts(hosts);
            if (list.Count == 0) list.Add(DefaultHost);
            return list;
        }

        static string Bare(string h) => h.TrimStart('[').TrimEnd(']');
    }

    public static string BaseUrl(string? host, int port) =>
        "http://" + NormalizeHost(host) + ":" + port.ToString(CultureInfo.InvariantCulture);

    public static string OpenAiBaseUrl(string? host, int port) =>
        BaseUrl(host, port) + "/v1";

    public static string ModelId(string? alias, string? modelPath)
    {
        if (!string.IsNullOrWhiteSpace(alias)) return alias.Trim();
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            try
            {
                var n = Path.GetFileNameWithoutExtension(modelPath.Trim());
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
        }
        return "llama";
    }

    public static string ChatCurl(string? host, int port, string? apiKey, string? alias, string? modelPath)
    {
        var url = OpenAiBaseUrl(host, port) + "/chat/completions";
        var model = JsonEscape(ModelId(alias, modelPath));
        var auth = string.IsNullOrWhiteSpace(apiKey)
            ? ""
            : " -H \"Authorization: Bearer " + apiKey.Trim() + "\"";
        var body = "{\"model\":\"" + model +
                   "\",\"messages\":[{\"role\":\"user\",\"content\":\"Hello!\"}],\"stream\":false}";
        return "curl " + url + " -H \"Content-Type: application/json\"" + auth + " -d '" + body + "'";
    }

    private static string JsonEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
