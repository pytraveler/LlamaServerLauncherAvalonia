using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Models;

public static class CommandLineTests
{
    public static void Run(Harness h)
    {
        IsFlag(h);
        ParseArguments(h);
        ArgumentValues(h);
        Normalize(h);
        QuoteAndPath(h);
        Build(h);
        RoundTrip(h);
    }

    private static void IsFlag(Harness h)
    {
        h.Section("CommandLineParser.IsFlag");
        h.Check("long flag", CommandLineParser.IsFlag("--threads"), "--threads");
        h.Check("short flag", CommandLineParser.IsFlag("-t"), "-t");
        h.Check("bare dash is flag", CommandLineParser.IsFlag("-"), "-");
        h.Check("negative int not flag", !CommandLineParser.IsFlag("-1"), "-1");
        h.Check("negative float not flag", !CommandLineParser.IsFlag("-0.5"), "-0.5");
        h.Check("leading-dot negative not flag", !CommandLineParser.IsFlag("-.3"), "-.3");
        h.Check("empty not flag", !CommandLineParser.IsFlag(""), "<empty>");
        h.Check("word not flag", !CommandLineParser.IsFlag("model"), "model");
    }

    private static void ParseArguments(Harness h)
    {
        h.Section("CommandLineParser.ParseArguments");

        var empty = CommandLineParser.ParseArguments("");
        h.Check("empty input yields no tokens", empty.Count == 0, $"count={empty.Count}");

        var simple = CommandLineParser.ParseArguments("--threads 8");
        h.Check("simple split", simple.SequenceEqual(new[] { "--threads", "8" }), Join(simple));

        var quoted = CommandLineParser.ParseArguments("hello \"two words\" 'single q'");
        h.Check("double-quoted keeps spaces", quoted.Count == 3 && quoted[1] == "two words", Join(quoted));
        h.Check("single-quoted keeps spaces", quoted.Count == 3 && quoted[2] == "single q", Join(quoted));

        var collapsed = CommandLineParser.ParseArguments("a    b\t c");
        h.Check("collapses runs of whitespace", collapsed.SequenceEqual(new[] { "a", "b", "c" }), Join(collapsed));

        var escaped = CommandLineParser.ParseArguments("\"a\\\"b\"");
        h.Check("escaped quote preserved inside quotes", escaped.Count == 1 && escaped[0] == "a\\\"b", Join(escaped));
    }

    private static void ArgumentValues(Harness h)
    {
        h.Section("CommandLineParser.GetArgumentValues / GetArgumentFlags");

        var args = new List<string> { "--model", "/p", "--verbose", "--threads", "8" };
        var vals = CommandLineParser.GetArgumentValues(args);
        h.Check("flag with value", vals["--model"] == "/p", $"--model={vals["--model"]}");
        h.Check("valueless flag is null", vals.ContainsKey("--verbose") && vals["--verbose"] == null, "--verbose");
        h.Check("trailing flag with value", vals["--threads"] == "8", $"--threads={vals["--threads"]}");

        var neg = CommandLineParser.GetArgumentValues(new List<string> { "--temp", "-0.5" });
        h.Check("negative number taken as value", neg["--temp"] == "-0.5", $"--temp={neg["--temp"]}");

        var flags = CommandLineParser.GetArgumentFlags(new List<string> { "--a", "x", "--b" });
        h.Check("flag set ignores values", flags.SetEquals(new[] { "--a", "--b" }), Join(flags.ToList()));

        var lookup = CommandLineParser.GetArgumentValues(new List<string> { "--Model", "/p" });
        h.Check("flag lookup is case-insensitive", lookup.ContainsKey("--model"), Join(lookup.Keys.ToList()));
    }

    private static void Normalize(Harness h)
    {
        h.Section("CommandLineParser.NormalizeSpecialCharacters");
        h.Check("real tab to space", CommandLineParser.NormalizeSpecialCharacters("a\tb") == "a b", "a<tab>b");
        h.Check("literal backslash-t to space", CommandLineParser.NormalizeSpecialCharacters("a\\tb") == "a b", "a\\tb");
        h.Check("json double-backslash collapses", CommandLineParser.NormalizeSpecialCharacters("a\\\\b") == "a\\b", "a\\\\b");
        h.Check("empty passes through", CommandLineParser.NormalizeSpecialCharacters("") == "", "<empty>");
    }

    private static void QuoteAndPath(Harness h)
    {
        h.Section("CommandLineBuilder.QuoteValue / UnescapePath / IsPathProperty");

        h.Check("plain value wrapped", CommandLineBuilder.QuoteValue("plain") == "\"plain\"", CommandLineBuilder.QuoteValue("plain"));
        h.Check("spaces wrapped", CommandLineBuilder.QuoteValue("with space") == "\"with space\"", CommandLineBuilder.QuoteValue("with space"));
        h.Check("inner quote escaped", CommandLineBuilder.QuoteValue("a\"b") == "\"a\\\"b\"", CommandLineBuilder.QuoteValue("a\"b"));
        h.Check("existing escape preserved", CommandLineBuilder.QuoteValue("a\\\"b") == "\"a\\\"b\"", CommandLineBuilder.QuoteValue("a\\\"b"));

        h.Check("unescape doubled backslash", CommandLineBuilder.UnescapePath("C:\\\\Users") == "C:\\Users", CommandLineBuilder.UnescapePath("C:\\\\Users"));
        h.Check("unescape empty", CommandLineBuilder.UnescapePath("") == "", "<empty>");

        h.Check("ModelPath is a path property", CommandLineBuilder.IsPathProperty("ModelPath"), "ModelPath");
        h.Check("Port is not a path property", !CommandLineBuilder.IsPathProperty("Port"), "Port");
    }

    private static void Build(Harness h)
    {
        h.Section("CommandLineBuilder.Build");

        var cfg = new ServerConfiguration
        {
            Threads = 8,
            ContextSize = 4096,
            Port = 8080,
            Host = "127.0.0.1"
        };
        var line = CommandLineBuilder.Build(cfg);
        h.Check("emits canonical -t for threads", line.Contains("-t 8"), line);
        h.Check("emits canonical -c for ctx-size", line.Contains("-c 4096"), line);
        h.Check("emits --port", line.Contains("--port 8080"), line);
        h.Check("emits --host", line.Contains("--host"), line);

        var spaced = CommandLineBuilder.Build(new ServerConfiguration { ModelPath = "/models/my model.gguf" });
        h.Check("model flag present as -m", spaced.Contains("-m"), spaced);
        h.Check("spaced model path quoted", spaced.Contains("\""), spaced);

        var full = CommandLineBuilder.BuildFullCommand(new ServerConfiguration { ExecutablePath = "/bin/llama-server" });
        h.Check("full command quotes executable", full.StartsWith("\"/bin/llama-server\""), full);

        var fallback = CommandLineBuilder.BuildFullCommand(new ServerConfiguration());
        h.Check("full command falls back to llama-server", fallback.StartsWith("\"llama-server\""), fallback);
    }

    private static void RoundTrip(Harness h)
    {
        h.Section("Build then parse round-trip");

        var cfg = new ServerConfiguration { Threads = 8, ContextSize = 4096, Port = 8080 };
        var line = CommandLineBuilder.Build(cfg);
        var parsed = CommandLineParser.ParseArguments(line);
        var vals = CommandLineParser.GetArgumentValues(parsed);

        h.Check("threads survives round-trip", vals.TryGetValue("-t", out var t) && t == "8", $"-t={Val(vals, "-t")}");
        h.Check("ctx-size survives round-trip", vals.TryGetValue("-c", out var c) && c == "4096", $"-c={Val(vals, "-c")}");
        h.Check("port survives round-trip", vals.TryGetValue("--port", out var p) && p == "8080", $"--port={Val(vals, "--port")}");
    }

    private static string Join(List<string> items) => "[" + string.Join(", ", items) + "]";

    private static string Val(Dictionary<string, string?> d, string key) => d.TryGetValue(key, out var v) ? v ?? "<null>" : "<missing>";
}
