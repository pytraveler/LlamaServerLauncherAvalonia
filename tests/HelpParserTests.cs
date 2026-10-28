using System.Globalization;
using System.Linq;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

public static class HelpParserTests
{
    private const string Help =
        "----- common params -----\n" +
        "\n" +
        "-h,    --help, --usage                  print usage and exit\n" +
        "-cl,   --cache-list                     show list of models in cache\n" +
        "-Cr,   --cpu-range lo-hi                range of CPUs for affinity. Complements --cpu-mask\n" +
        "-lm,   --load-mode MODE                 model loading mode (default: auto)\n" +
        "                                        - auto: mmap, unless a device does not support it\n" +
        "-cb,   --cont-batching, -nocb, --no-cont-batching\n" +
        "                                        whether to enable continuous batching (a.k.a dynamic batching)\n" +
        "                                        (default: enabled)\n" +
        "\n" +
        "----- speculative params -----\n" +
        "\n" +
        "--draft, --draft-n, --draft-max N       the argument has been removed. use --spec-draft-n-max or\n" +
        "                                        --spec-ngram-mod-n-max\n" +
        "\n" +
        "----- example-specific params -----\n" +
        "\n" +
        "-kvu,  --kv-unified, -no-kvu, --no-kv-unified\n" +
        "                                        use single unified KV buffer\n" +
        "--port PORT                             port to listen (default: 9931)\n" +
        "--fim-qwen-1.5b-default                 use default Qwen 2.5 Coder 1.5B (note: can download weights from the\n" +
        "                                        internet)\n" +
        "--mystery-flag N                        a flag the registry has never heard of (default: 7)\n" +
        "--mystery-aside N                       a value with an aside (default: 64, 0 = disabled)\n" +
        "--mystery-semi N                        a value with a semicolon aside (default: -1; -1 = disabled)\n" +
        "--mystery-dash N                        a value with a dash aside (default: 0 - same as --n-ubatch)\n" +
        "--mystery-same N                        a default that is a sentence (default: same as --threads)\n" +
        "--mystery-ref N                         a default that names another flag (default: --cpu-strict-draft)\n" +
        "--mystery-unused URL                    a default that is no value at all (default: unused)\n" +
        "--mystery-list LIST                     a list without spaces (default: GET,POST,DELETE)\n" +
        "-tb,   --threads-batch N                number of threads to use during batch and prompt processing (default:\n" +
        "                                        same as --threads)\n" +
        "--cache-idle-slots, --no-cache-idle-slots\n" +
        "                                        save idle slots to the prompt cache on new task (default: enabled,\n" +
        "                                        requires cache-ram)\n";

    public static void Run(Harness h)
    {
        h.Section("LlamaHelpParserService: flags and descriptions");

        var args = LlamaHelpParserService.ParseArgumentDescriptions(Help);
        HelpArgumentInfo? Find(string flag) => args.FirstOrDefault(a => a.AllFlags.Contains(flag));

        var cacheList = Find("-cl");
        h.Check("a short flag padded after its comma keeps its long name",
            cacheList != null && cacheList.AllFlags.SequenceEqual(new[] { "-cl", "--cache-list" }),
            cacheList == null ? "missing" : string.Join(" ", cacheList.AllFlags));
        h.Check("the long name is the one shown",
            cacheList?.PrimaryFlag == "--cache-list", cacheList?.PrimaryFlag ?? "missing");
        h.Check("and the description is not the long name",
            cacheList?.Description == "show list of models in cache", cacheList?.Description ?? "missing");

        var cpuRange = Find("-Cr");
        h.Check("a value placeholder with a dash is not a flag",
            cpuRange != null && cpuRange.AllFlags.SequenceEqual(new[] { "-Cr", "--cpu-range" }),
            cpuRange == null ? "missing" : string.Join(" ", cpuRange.AllFlags));

        var unified = Find("--kv-unified");
        h.Check("a short flag with a dash inside stays whole",
            unified != null && unified.AllFlags.Contains("-no-kvu") && !unified.AllFlags.Contains("-no"),
            unified == null ? "missing" : string.Join(" ", unified.AllFlags));

        var batching = Find("-cb");
        h.Check("all four spellings on a line without a description are kept",
            batching != null && batching.AllFlags.Count == 4 && batching.Description.StartsWith("Whether to enable"),
            batching == null ? "missing" : $"{string.Join(" ", batching.AllFlags)} / {batching.Description}");

        var dotted = Find("--fim-qwen-1.5b-default");
        h.Check("a flag with a dot in its name is read whole",
            dotted != null && dotted.PrimaryFlag == "--fim-qwen-1.5b-default",
            dotted?.PrimaryFlag ?? string.Join(",", args.Select(a => a.PrimaryFlag)));

        h.Check("a flag llama.cpp has removed is not offered",
            Find("--draft") == null && Find("--draft-max") == null, "dropped");

        var port = Find("--port");
        h.Check("the port default comes from the build, not from the registry",
            port?.DefaultValue == "9931", port?.DefaultValue ?? "null");
        h.Check("the section decides the category of a flag the registry does not override",
            Find("--mystery-flag")?.Category == "server", Find("--mystery-flag")?.Category ?? "missing");

        var mystery = Find("--mystery-flag");
        h.Check("a flag the registry does not know keeps the help text and default",
            mystery?.Description == "a flag the registry has never heard of" && mystery.DefaultValue == "7",
            $"{mystery?.Description} / {mystery?.DefaultValue}");

        var english = Find("--load-mode");
        h.Check("the registry gives the load mode its values",
            english?.AllowedValues != null && english.AllowedValues.Contains("mmap+mlock") && english.AllowedValues.Contains("dio"),
            english?.AllowedValues == null ? "none" : string.Join(",", english.AllowedValues));
        h.Check("and an English description when the UI is in English",
            english?.Description.StartsWith("Model loading mode") == true, english?.Description ?? "missing");

        var saved = LocalizedStrings.CurrentCulture;
        try
        {
            LocalizedStrings.CurrentCulture = new CultureInfo("ru-RU");
            var russian = LlamaHelpParserService.ParseArgumentDescriptions(Help).FirstOrDefault(a => a.AllFlags.Contains("--load-mode"));
            h.Check("and a Russian one when the UI is in Russian",
                russian?.Description.StartsWith("Режим загрузки модели") == true, russian?.Description ?? "missing");
        }
        finally
        {
            LocalizedStrings.CurrentCulture = saved;
        }

        h.Section("LlamaHelpParserService: defaults the picker can insert");

        string? Default(string flag) => Find(flag)?.DefaultValue;
        h.Check("a value with an aside keeps the value", Default("--mystery-aside") == "64", Default("--mystery-aside") ?? "null");
        h.Check("and so after a semicolon", Default("--mystery-semi") == "-1", Default("--mystery-semi") ?? "null");
        h.Check("and after a dash", Default("--mystery-dash") == "0", Default("--mystery-dash") ?? "null");
        h.Check("a default that is a sentence is not inserted as a value",
            Default("--mystery-same") == null, Default("--mystery-same") ?? "null");
        h.Check("nor one that names another flag", Default("--mystery-ref") == null, Default("--mystery-ref") ?? "null");
        h.Check("nor a word that stands for nothing", Default("--mystery-unused") == null, Default("--mystery-unused") ?? "null");
        h.Check("a list without spaces stays whole",
            Default("--mystery-list") == "GET,POST,DELETE", Default("--mystery-list") ?? "null");
        h.Check("a switch that is on with a note gets no value, so it is not followed by junk",
            Default("--cache-idle-slots") == null, Default("--cache-idle-slots") ?? "null");
        h.Check("and the sentence the registry keeps for a flag is not a value either",
            Default("--threads-batch") == null, Default("--threads-batch") ?? "null");

        h.Section("LlamaHelpParserService: the default context of a build");

        h.Check("a new build loads the context from the model",
            LlamaHelpParserService.ParseContextDefault(
                "-c,    --ctx-size N                     size of the prompt context (default: 0, 0 = loaded from model)\n") == 0,
            "0");
        h.Check("an old one still says 4096",
            LlamaHelpParserService.ParseContextDefault(
                "-c,    --ctx-size N                     size of the prompt context (default: 4096, 0 = loaded from model)\n") == 4096,
            "4096");
        h.Check("a help without the flag says nothing",
            LlamaHelpParserService.ParseContextDefault(Help) == null && LlamaHelpParserService.ParseContextDefault(null) == null,
            "null");

        h.Section("LlamaHelpParserService: supported flags");

        var flags = LlamaHelpParserService.ParseFlagsFromHelp(Help);
        h.Check("a dotted flag is supported under its full name",
            flags.Contains("--fim-qwen-1.5b-default") && !flags.Contains("--fim-qwen-1"),
            string.Join(" ", flags.Where(f => f.StartsWith("--fim"))));
        h.Check("both spellings of a padded pair are supported",
            flags.Contains("-cl") && flags.Contains("--cache-list"), "ok");
        h.Check("help itself is not a server flag",
            !flags.Contains("--help") && !flags.Contains("-h"), "excluded");
    }
}
