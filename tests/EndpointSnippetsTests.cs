using LlamaServerLauncher.Models;

public static class EndpointSnippetsTests
{
    public static void Run(Harness h)
    {
        h.Section("EndpointSnippets.NormalizeHost");
        h.Check("empty -> loopback", EndpointSnippets.NormalizeHost("") == "127.0.0.1", EndpointSnippets.NormalizeHost(""));
        h.Check("null -> loopback", EndpointSnippets.NormalizeHost(null) == "127.0.0.1", EndpointSnippets.NormalizeHost(null));
        h.Check("0.0.0.0 -> loopback", EndpointSnippets.NormalizeHost("0.0.0.0") == "127.0.0.1", EndpointSnippets.NormalizeHost("0.0.0.0"));
        h.Check("wildcard -> loopback", EndpointSnippets.NormalizeHost("*") == "127.0.0.1", EndpointSnippets.NormalizeHost("*"));
        h.Check("normal host kept", EndpointSnippets.NormalizeHost("192.168.1.5") == "192.168.1.5", EndpointSnippets.NormalizeHost("192.168.1.5"));
        h.Check("named host kept", EndpointSnippets.NormalizeHost("localhost") == "localhost", EndpointSnippets.NormalizeHost("localhost"));
        h.Check(":: -> [::1]", EndpointSnippets.NormalizeHost("::") == "[::1]", EndpointSnippets.NormalizeHost("::"));
        h.Check("ipv6 literal bracketed", EndpointSnippets.NormalizeHost("fe80::1") == "[fe80::1]", EndpointSnippets.NormalizeHost("fe80::1"));
        h.Check("already bracketed kept", EndpointSnippets.NormalizeHost("[fe80::1]") == "[fe80::1]", EndpointSnippets.NormalizeHost("[fe80::1]"));
        h.Check("trims", EndpointSnippets.NormalizeHost("  10.0.0.1  ") == "10.0.0.1", EndpointSnippets.NormalizeHost("  10.0.0.1  "));

        h.Section("EndpointSnippets: host lists");
        h.Check("the first address of a list is used",
            EndpointSnippets.NormalizeHost("192.168.1.5, 127.0.0.1") == "192.168.1.5",
            EndpointSnippets.NormalizeHost("192.168.1.5, 127.0.0.1"));
        h.Check("a wildcard in a list still means loopback",
            EndpointSnippets.NormalizeHost("0.0.0.0,::") == "127.0.0.1", EndpointSnippets.NormalizeHost("0.0.0.0,::"));
        h.Check(":: in a list is reached over ipv6, since it binds ipv6 only there",
            EndpointSnippets.NormalizeHost("::,192.168.1.5") == "[::1]", EndpointSnippets.NormalizeHost("::,192.168.1.5"));
        h.Check("unix sockets are skipped for http",
            EndpointSnippets.NormalizeHost("/tmp/llama.sock,10.0.0.2") == "10.0.0.2",
            EndpointSnippets.NormalizeHost("/tmp/llama.sock,10.0.0.2"));
        h.Check("a socket alone is a host the launcher cannot reach",
            EndpointSnippets.IsSocketOnly("/tmp/llama.sock") && EndpointSnippets.IsSocketOnly(" a.sock , b.sock "),
            "socket only");
        h.Check("a socket next to an address is not",
            !EndpointSnippets.IsSocketOnly("/tmp/llama.sock,127.0.0.1"), "has tcp");
        h.Check("and neither is an empty field, which means loopback",
            !EndpointSnippets.IsSocketOnly("") && !EndpointSnippets.IsSocketOnly(null) && !EndpointSnippets.IsSocketOnly(" , "),
            "default");
        h.Check("a socket alone falls back to loopback",
            EndpointSnippets.NormalizeHost("/tmp/llama.sock") == "127.0.0.1", EndpointSnippets.NormalizeHost("/tmp/llama.sock"));
        h.Check("empty items are ignored",
            EndpointSnippets.SplitHosts(" ,127.0.0.1,, ").Count == 1, EndpointSnippets.SplitHosts(" ,127.0.0.1,, ").Count.ToString());
        h.Check("base url of a list",
            EndpointSnippets.BaseUrl("127.0.0.1,192.168.1.5", 8080) == "http://127.0.0.1:8080",
            EndpointSnippets.BaseUrl("127.0.0.1,192.168.1.5", 8080));
        h.Check("ipv6 base url is bracketed",
            EndpointSnippets.BaseUrl("::1", 8080) == "http://[::1]:8080", EndpointSnippets.BaseUrl("::1", 8080));
        h.Check("socket connect host has no brackets",
            EndpointSnippets.ConnectHost("::1") == "::1" && EndpointSnippets.ConnectHost("0.0.0.0") == "127.0.0.1",
            EndpointSnippets.ConnectHost("::1"));

        h.Section("EndpointSnippets.HostsOverlap");
        h.Check("same host overlaps", EndpointSnippets.HostsOverlap("127.0.0.1", "127.0.0.1"), "yes");
        h.Check("different hosts do not", !EndpointSnippets.HostsOverlap("127.0.0.1", "192.168.1.5"), "no");
        h.Check("a shared address in two lists overlaps",
            EndpointSnippets.HostsOverlap("127.0.0.1,192.168.1.5", "10.0.0.1, 192.168.1.5"), "yes");
        h.Check("an empty host is the default one",
            EndpointSnippets.HostsOverlap("", "127.0.0.1") && EndpointSnippets.HostsOverlap(null, " "), "yes");
        h.Check("brackets and case do not matter",
            EndpointSnippets.HostsOverlap("[FE80::1]", "fe80::1"), "yes");

        h.Section("EndpointSnippets.BaseUrl / OpenAiBaseUrl");
        h.Check("base url", EndpointSnippets.BaseUrl("127.0.0.1", 8080) == "http://127.0.0.1:8080", EndpointSnippets.BaseUrl("127.0.0.1", 8080));
        h.Check("base url normalizes 0.0.0.0", EndpointSnippets.BaseUrl("0.0.0.0", 9000) == "http://127.0.0.1:9000", EndpointSnippets.BaseUrl("0.0.0.0", 9000));
        h.Check("openai base url", EndpointSnippets.OpenAiBaseUrl("127.0.0.1", 8080) == "http://127.0.0.1:8080/v1", EndpointSnippets.OpenAiBaseUrl("127.0.0.1", 8080));

        h.Section("EndpointSnippets.ModelId");
        h.Check("alias wins", EndpointSnippets.ModelId("my-alias", "C:/models/foo.gguf") == "my-alias", EndpointSnippets.ModelId("my-alias", "C:/models/foo.gguf"));
        h.Check("filename stem fallback", EndpointSnippets.ModelId("", "C:/models/qwen3-8b-Q4_K_M.gguf") == "qwen3-8b-Q4_K_M", EndpointSnippets.ModelId("", "C:/models/qwen3-8b-Q4_K_M.gguf"));
        h.Check("default when nothing", EndpointSnippets.ModelId("", "") == "llama", EndpointSnippets.ModelId("", ""));

        h.Section("EndpointSnippets.ChatCurl");
        var noKey = EndpointSnippets.ChatCurl("127.0.0.1", 8080, "", "my-model", "");
        h.Check("has endpoint", noKey.Contains("http://127.0.0.1:8080/v1/chat/completions"), noKey);
        h.Check("has content-type", noKey.Contains("-H \"Content-Type: application/json\""), noKey);
        h.Check("no auth header without key", !noKey.Contains("Authorization"), noKey);
        h.Check("has model", noKey.Contains("\"model\":\"my-model\""), noKey);
        h.Check("has messages", noKey.Contains("\"messages\""), noKey);
        h.Check("body single quoted", noKey.Contains(" -d '{") && noKey.EndsWith("}'"), noKey);

        var withKey = EndpointSnippets.ChatCurl("0.0.0.0", 8080, "secret123", "", "C:/m/foo.gguf");
        h.Check("auth header with key", withKey.Contains("-H \"Authorization: Bearer secret123\""), withKey);
        h.Check("model from filename", withKey.Contains("\"model\":\"foo\""), withKey);
        h.Check("host normalized in curl", withKey.Contains("http://127.0.0.1:8080/"), withKey);

        var quoted = EndpointSnippets.ChatCurl("127.0.0.1", 8080, "", "weird\"name", "");
        h.Check("model json-escaped", quoted.Contains("\"model\":\"weird\\\"name\""), quoted);
    }
}
