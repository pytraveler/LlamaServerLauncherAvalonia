using System.Globalization;
using System.IO;

namespace LlamaServerLauncher.Models;

public static class EndpointSnippets
{
    public static string NormalizeHost(string? host)
    {
        var h = (host ?? "").Trim();
        if (h.Length == 0 || h == "0.0.0.0" || h == "*") return "127.0.0.1";
        if (h == "::" || h == "[::]") return "[::1]";
        if (h.Contains(':') && !h.StartsWith("[")) return "[" + h + "]";
        return h;
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
