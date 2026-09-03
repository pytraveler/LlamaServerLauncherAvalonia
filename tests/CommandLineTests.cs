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
        SpecAndDraft(h);
        HuggingFace(h);
        CustomArgToggles(h);
        CustomArgAliases(h);
        LoadMode(h);
        ParseBack(h);
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

        var moe = CommandLineBuilder.Build(new ServerConfiguration { CpuMoe = 3 });
        h.Check("emits --n-cpu-moe for CpuMoe", moe.Contains("--n-cpu-moe 3"), moe);

        var verbose = CommandLineBuilder.Build(new ServerConfiguration { VerboseLogging = true });
        h.Check("emits -v when verbose logging is on", verbose.Split(' ').Contains("-v"), verbose);

        var verboseAuto = CommandLineBuilder.Build(new ServerConfiguration { VerboseLogging = null });
        h.Check("auto leaves -v out", !verboseAuto.Split(' ').Contains("-v"), verboseAuto);

        var verboseOff = CommandLineBuilder.Build(new ServerConfiguration { VerboseLogging = false });
        h.Check("off leaves -v out as well", !verboseOff.Split(' ').Contains("-v"), verboseOff);

        var verboseLong = CommandLineBuilder.Build(new ServerConfiguration { VerboseLogging = true }, Flags("--verbose"));
        h.Check("falls back to --verbose when only that is advertised", verboseLong.Split(' ').Contains("--verbose"), verboseLong);

        var verboseUnsupported = CommandLineBuilder.Build(new ServerConfiguration { VerboseLogging = true }, Flags("--port"));
        h.Check("a build without the flag does not get it", !verboseUnsupported.Split(' ').Contains("-v"), verboseUnsupported);

        var verboseTyped = CommandLineBuilder.Build(new ServerConfiguration { CustomArguments = "-v" });
        h.Check("a hand-typed -v is not doubled", verboseTyped.Split(' ').Count(t => t == "-v") == 1, verboseTyped);

        var verboseTypedAndSet = CommandLineBuilder.Build(new ServerConfiguration { CustomArguments = "--verbose", VerboseLogging = true });
        h.Check("the typed spelling wins over the switch", verboseTypedAndSet.Split(' ').Count(t => t == "--verbose" || t == "-v") == 1, verboseTypedAndSet);
    }

    private static void ParseBack(Harness h)
    {
        h.Section("ServerConfigurationExtensions.ParseFromCommandLine");

        h.Check("empty input yields null", ServerConfigurationExtensions.ParseFromCommandLine("") == null, "<empty>");

        var known = ServerConfigurationExtensions.ParseFromCommandLine("-t 8 -c 4096 --n-cpu-moe 3");
        h.Check("threads parsed", known!.Threads == 8, $"threads={known.Threads}");
        h.Check("ctx-size parsed", known.ContextSize == 4096, $"ctx={known.ContextSize}");
        h.Check("n-cpu-moe parsed into CpuMoe", known.CpuMoe == 3, $"cpuMoe={known.CpuMoe}");

        var plain = ServerConfigurationExtensions.ParseFromCommandLine("--unknown-flag value");
        h.Check("unknown flag kept in custom args", plain!.CustomArguments.Contains("--unknown-flag value"), plain.CustomArguments);

        var spaced = ServerConfigurationExtensions.ParseFromCommandLine("--unknown-flag \"two words\"");
        h.Check("spaced unknown value re-quoted", spaced!.CustomArguments.Contains("\"two words\""), spaced.CustomArguments);

        var seenVerbose = ServerConfigurationExtensions.ParseFromCommandLine("-v");
        h.Check("-v parsed as verbose logging on", seenVerbose!.VerboseLogging == true, $"verbose={seenVerbose.VerboseLogging}");

        var noVerbose = ServerConfigurationExtensions.ParseFromCommandLine("-t 8");
        h.Check("no -v leaves verbose logging unset", noVerbose!.VerboseLogging == null, $"verbose={noVerbose.VerboseLogging}");
    }

    private static void SpecAndDraft(Harness h)
    {
        h.Section("CommandLineBuilder speculative-decoding args");

        var spec = CommandLineBuilder.Build(new ServerConfiguration { SpecType = "draft" });
        h.Check("spec-type emitted", spec.Contains("--spec-type draft"), spec);

        var specNone = CommandLineBuilder.Build(new ServerConfiguration { SpecType = "none" });
        h.Check("spec-type=none suppressed", !specNone.Contains("--spec-type"), specNone);

        var specEmpty = CommandLineBuilder.Build(new ServerConfiguration { SpecType = "" });
        h.Check("spec-type empty suppressed", !specEmpty.Contains("--spec-type"), specEmpty);

        var draft = CommandLineBuilder.Build(new ServerConfiguration { SpecDraftModel = @"C:\m\draft.gguf" });
        h.Check("draft model emitted as -md", draft.Contains("-md "), draft);
        h.Check("draft model path backslashes escaped", draft.Contains(@"C:\\m\\draft.gguf"), draft);

        var dp = CommandLineBuilder.Build(new ServerConfiguration
        {
            SpecDraftGpuLayers = "10",
            SpecDraftNMax = 8,
            SpecDraftNMin = 2,
            SpecDraftPSplit = 0.5,
            SpecDraftPMin = 0.1
        });
        h.Check("ngld emitted", dp.Contains("-ngld 10"), dp);
        h.Check("spec-draft-n-max emitted", dp.Contains("--spec-draft-n-max 8"), dp);
        h.Check("spec-draft-n-min emitted", dp.Contains("--spec-draft-n-min 2"), dp);
        h.Check("spec-draft-p-split uses invariant culture", dp.Contains("--spec-draft-p-split 0.5"), dp);
        h.Check("spec-draft-p-min uses invariant culture", dp.Contains("--spec-draft-p-min 0.1"), dp);
    }

    private static void HuggingFace(Harness h)
    {
        h.Section("CommandLineBuilder HuggingFace args");

        var hf = CommandLineBuilder.Build(new ServerConfiguration
        {
            HfRepo = "org/model",
            HfFile = "model.gguf",
            HfRepoDraft = "org/draft",
            Offline = true
        });
        h.Check("hf repo emitted as -hf", hf.Contains("-hf org/model"), hf);
        h.Check("hf file emitted as -hff", hf.Contains("-hff model.gguf"), hf);
        h.Check("hf draft repo emitted as -hfd", hf.Contains("-hfd org/draft"), hf);
        h.Check("offline emitted as bare --offline", hf.Contains("--offline"), hf);

        var online = CommandLineBuilder.Build(new ServerConfiguration { Offline = false });
        h.Check("offline=false omits --offline", !online.Contains("--offline"), online);
    }

    private static void CustomArgToggles(Harness h)
    {
        h.Section("CommandLineBuilder custom-arg toggle states");

        var enabled = CommandLineBuilder.Build(new ServerConfiguration { CustomArguments = "--my-custom foo" });
        h.Check("custom flag present by default", enabled.Contains("--my-custom foo"), enabled);

        var disabled = CommandLineBuilder.Build(new ServerConfiguration
        {
            CustomArguments = "--my-custom foo",
            CustomArgumentToggleStates = new Dictionary<string, bool> { ["--my-custom"] = false }
        });
        h.Check("disabled custom flag skipped", !disabled.Contains("--my-custom"), disabled);
        h.Check("disabled custom flag value skipped", !disabled.Contains("foo"), disabled);

        var toggledOn = CommandLineBuilder.Build(new ServerConfiguration
        {
            CustomArguments = "--my-custom foo",
            CustomArgumentToggleStates = new Dictionary<string, bool> { ["--my-custom"] = true }
        });
        h.Check("explicitly enabled custom flag present", toggledOn.Contains("--my-custom foo"), toggledOn);
    }

    private static void CustomArgAliases(Harness h)
    {
        h.Section("CommandLineBuilder custom-arg alias dedup");

        var ngl = CommandLineBuilder.Build(new ServerConfiguration
        {
            GpuLayers = 65,
            CustomArguments = "--n-gpu-layers 99"
        });
        h.Check("aliased -ngl emitted once", CountOccurrences(ngl, "99") == 1, ngl);
        h.Check("custom value wins over UI value", ngl.Contains("-ngl 99") && !ngl.Contains("-ngl 65"), ngl);
        h.Check("alias not appended a second time", !ngl.Contains("--n-gpu-layers"), ngl);

        var gpuLayers = CommandLineBuilder.Build(new ServerConfiguration { CustomArguments = "--gpu-layers 40" });
        h.Check("--gpu-layers collapses into -ngl", gpuLayers.Contains("-ngl 40") && !gpuLayers.Contains("--gpu-layers"), gpuLayers);

        var fa = CommandLineBuilder.Build(new ServerConfiguration
        {
            FlashAttention = true,
            CustomArguments = "--flash-attn on"
        });
        h.Check("flash-attn alias emitted once", CountOccurrences(fa, "on") == 1, fa);

        var mmproj = CommandLineBuilder.Build(new ServerConfiguration
        {
            MmprojPath = "/models/mm.gguf",
            CustomArguments = "--mmproj /other/mm.gguf"
        });
        h.Check("mmproj alias emitted once", CountOccurrences(mmproj, "mm.gguf") == 1, mmproj);

        var unrelated = CommandLineBuilder.Build(new ServerConfiguration
        {
            GpuLayers = 65,
            CustomArguments = "--n-gpu-layers 99 --my-custom foo"
        });
        h.Check("unknown custom args survive dedup", unrelated.Contains("--my-custom foo"), unrelated);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = haystack.IndexOf(needle, System.StringComparison.Ordinal);
        while (idx >= 0)
        {
            count++;
            idx = haystack.IndexOf(needle, idx + needle.Length, System.StringComparison.Ordinal);
        }
        return count;
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

    private static HashSet<string> Flags(params string[] flags) => new(flags, System.StringComparer.OrdinalIgnoreCase);

    private static void LoadMode(Harness h)
    {
        h.Section("CommandLineBuilder.LoadModeFor");
        h.Check("nothing set -> no flag at all", CommandLineBuilder.LoadModeFor(null, null) == null, "null");
        h.Check("mmap on", CommandLineBuilder.LoadModeFor(true, null) == "mmap", CommandLineBuilder.LoadModeFor(true, null) ?? "null");
        h.Check("mmap off", CommandLineBuilder.LoadModeFor(false, null) == "none", CommandLineBuilder.LoadModeFor(false, null) ?? "null");
        h.Check("mmap on plus mlock", CommandLineBuilder.LoadModeFor(true, true) == "mmap+mlock", CommandLineBuilder.LoadModeFor(true, true) ?? "null");
        h.Check("mmap off plus mlock", CommandLineBuilder.LoadModeFor(false, true) == "mlock", CommandLineBuilder.LoadModeFor(false, true) ?? "null");
        h.Check("mlock alone keeps the default mmap",
            CommandLineBuilder.LoadModeFor(null, true) == "mmap+mlock", CommandLineBuilder.LoadModeFor(null, true) ?? "null");
        h.Check("mlock switched off is not a mode",
            CommandLineBuilder.LoadModeFor(null, false) == null, CommandLineBuilder.LoadModeFor(null, false) ?? "null");

        h.Section("CommandLineBuilder.ResolveLoadModeFlag");
        h.Check("unknown support -> keep the old spelling", CommandLineBuilder.ResolveLoadModeFlag(null) == null, "null");
        h.Check("long form preferred",
            CommandLineBuilder.ResolveLoadModeFlag(Flags("-lm", "--load-mode")) == "--load-mode", "ok");
        h.Check("short form when it is the only one",
            CommandLineBuilder.ResolveLoadModeFlag(Flags("-lm")) == "-lm", "ok");
        h.Check("old binary -> null", CommandLineBuilder.ResolveLoadModeFlag(Flags("--mmap", "--no-mmap", "--mlock")) == null, "ok");

        var modern = Flags("-m", "--mmap", "--no-mmap", "--mlock", "-lm", "--load-mode");
        var legacy = Flags("-m", "--mmap", "--no-mmap", "--mlock");

        h.Section("CommandLineBuilder.Build - new builds get --load-mode");
        var newLine = CommandLineBuilder.Build(new ServerConfiguration { Mmap = false, Mlock = true }, modern);
        h.Check("mode emitted", newLine.Contains("--load-mode mlock"), newLine);
        h.Check("deprecated spelling dropped", !newLine.Contains("--no-mmap") && !newLine.Contains("--mlock"), newLine);

        var newDefault = CommandLineBuilder.Build(new ServerConfiguration(), modern);
        h.Check("nothing set -> nothing emitted", !newDefault.Contains("load-mode"), newDefault);

        h.Section("CommandLineBuilder.Build - old builds keep the old flags");
        var oldLine = CommandLineBuilder.Build(new ServerConfiguration { Mmap = false, Mlock = true }, legacy);
        h.Check("no-mmap kept", oldLine.Contains("--no-mmap"), oldLine);
        h.Check("mlock kept", oldLine.Contains("--mlock"), oldLine);
        h.Check("no load-mode", !oldLine.Contains("load-mode"), oldLine);

        var unknownSupport = CommandLineBuilder.Build(new ServerConfiguration { Mmap = false });
        h.Check("unknown support keeps the old spelling",
            unknownSupport.Contains("--no-mmap") && !unknownSupport.Contains("load-mode"), unknownSupport);

        h.Section("CommandLineBuilder.Build - custom arguments win");
        var manualMode = CommandLineBuilder.Build(
            new ServerConfiguration { Mmap = false, Mlock = true, CustomArguments = "-lm dio" }, modern);
        h.Check("manual -lm passes through", manualMode.Contains("-lm dio"), manualMode);
        h.Check("no second mode synthesized", !manualMode.Contains("--load-mode"), manualMode);
        h.Check("switches not duplicated next to a manual mode",
            !manualMode.Contains("--no-mmap") && !manualMode.Contains("--mlock"), manualMode);

        var directIo = CommandLineBuilder.Build(
            new ServerConfiguration { Mmap = false, CustomArguments = "-dio" }, modern);
        h.Check("direct io left alone", directIo.Contains("-dio"), directIo);
        h.Check("mode not synthesized over direct io", !directIo.Contains("load-mode"), directIo);
        h.Check("old flag used instead", directIo.Contains("--no-mmap"), directIo);

        var manualNoMmap = CommandLineBuilder.Build(
            new ServerConfiguration { Mmap = true, CustomArguments = "--no-mmap" }, modern);
        h.Check("custom --no-mmap kept", manualNoMmap.Contains("--no-mmap"), manualNoMmap);
        h.Check("mode not synthesized over it", !manualNoMmap.Contains("load-mode"), manualNoMmap);
    }
}
